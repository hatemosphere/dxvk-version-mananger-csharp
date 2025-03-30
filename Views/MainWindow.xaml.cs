using System.Windows;
using DxvkVersionManager.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace DxvkVersionManager.Views;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public partial class MainWindow : Window
{
    public MainWindow(IServiceProvider serviceProvider)
    {
        try
        {
            InitializeComponent();
            DataContext = serviceProvider.GetRequiredService<MainViewModel>();
            Log.Information("Main window initialized successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize main window");
            MessageBox.Show($"Failed to initialize main window: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }
}