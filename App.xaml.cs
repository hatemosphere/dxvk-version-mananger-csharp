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
            // Initialize logging
            Log.Logger = new LoggerConfiguration()
                .WriteTo.File("logs/app.log", rollingInterval: RollingInterval.Day)
                .WriteTo.Debug()
                .CreateLogger();
            
            Log.Information("Application starting up");
            
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
        
        // Register services
        services.AddSingleton<ISteamService>(provider => new SteamService(dataPath));
        services.AddSingleton<IDxvkVersionService>(provider => new DxvkVersionService(dataPath));
        services.AddSingleton<IDxvkManagerService>(provider => 
            new DxvkManagerService(
                provider.GetRequiredService<ISteamService>(),
                dataPath
            ));
        services.AddSingleton<LoggingService>();
        
        // Register view models
        services.AddTransient<MainViewModel>();
        services.AddTransient<InstalledGamesViewModel>();
        services.AddTransient<DxvkVersionsViewModel>();
        services.AddTransient<DxvkGplasyncViewModel>();
        
        // Register views
        services.AddTransient<MainWindow>();
    }
    
    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Application shutting down");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}