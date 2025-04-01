using System.Windows;
using DxvkVersionManager.Services.Implementations;
using DxvkVersionManager.Services.Interfaces;
using DxvkVersionManager.ViewModels;
using DxvkVersionManager.Views;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Diagnostics;
using System.IO;

namespace DxvkVersionManager;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        try
        {
            // Initialize logging with one file per session
            var logFileName = $"app_{DateTime.Now:yyyyMMdd_HHmmss_fff}.log";
            var logFilePath = Path.Combine(AppContext.BaseDirectory, "logs", logFileName);
            
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logFilePath, 
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.Debug()
                .CreateLogger();
            
            Log.Information($"Application starting up. Logging to: {logFilePath}");
            
            // Configure services
            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();
            
            Log.Information("Services initialized successfully");
            
            // Create and show main window
            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application failed to start");
            MessageBox.Show($"Failed to start application: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }
    
    private static void ConfigureServices(IServiceCollection services)
    {
        // Set up data paths
        var dataPath = Path.Combine(AppContext.BaseDirectory, "data");
        if (!Directory.Exists(dataPath))
        {
            Directory.CreateDirectory(dataPath);
            Log.Information($"Created data directory: {dataPath}");
        }
        
        // Remove userDataPath - everything uses dataPath now
        Log.Information("Configuring services with dataPath: " + dataPath);
        
        // Register services with explicit order
        // 1. Register singleton services first
        Log.Information("Registering PCGamingWikiService");
        services.AddSingleton<IPCGamingWikiService, PCGamingWikiService>();
        
        // 2. Register services that depend on previously registered services
        Log.Information("Registering SteamService with PCGamingWikiService dependency");
        services.AddSingleton<ISteamService>(provider => {
            var pcgwService = provider.GetRequiredService<IPCGamingWikiService>();
            Log.Information("Retrieved PCGamingWikiService for SteamService");
            return new SteamService(pcgwService, dataPath);
        });
        
        // 3. Register remaining services
        Log.Information("Registering DxvkVersionService");
        services.AddSingleton<IDxvkVersionService>(provider => new DxvkVersionService(dataPath));
        
        Log.Information("Registering DxvkManagerService");
        services.AddSingleton<IDxvkManagerService>(provider => new DxvkManagerService(
            provider.GetRequiredService<ISteamService>(),
            provider.GetRequiredService<IDxvkVersionService>(),
            dataPath
        ));
        
        // LoggingService is now a static singleton using the main logger, no need to register
        // services.AddSingleton<LoggingService>();
        
        // Register view models
        services.AddTransient<MainViewModel>();
        services.AddTransient<InstalledGamesViewModel>();
        services.AddTransient<DxvkVersionsViewModel>();
        services.AddTransient<DxvkGplasyncViewModel>();
        
        // Register views
        services.AddTransient<MainWindow>();
        
        Log.Information("Service registration completed");
    }
    
    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Application shutting down");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}