using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DxvkVersionManager.Services.Interfaces;
using DxvkVersionManager.Views;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Input;

namespace DxvkVersionManager.ViewModels;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public partial class MainViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly LoggingService _logger;
    
    [ObservableProperty]
    private ViewModelBase? _currentViewModel;
    
    [ObservableProperty]
    private bool _isLoading;
    
    [ObservableProperty]
    private string _statusMessage = "Initializing...";
    
    private DxvkVersionsViewModel? _dxvkVersionsViewModel;
    private DxvkGplasyncViewModel? _dxvkGplasyncViewModel;
    private InstalledGamesViewModel? _installedGamesViewModel;
    
    public MainViewModel(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = LoggingService.Instance;
        _logger.LogDebug("Initializing MainViewModel");
        
        // Initialize with InstalledGamesViewModel
        NavigateToInstalledGames();
    }
    
    [RelayCommand]
    private void NavigateToInstalledGames()
    {
        try
        {
            _logger.LogDebug("Navigating to Installed Games");
            CurrentViewModel = _installedGamesViewModel ??= new InstalledGamesViewModel(
                _serviceProvider.GetRequiredService<ISteamService>(),
                _serviceProvider.GetRequiredService<IDxvkManagerService>());
            StatusMessage = _installedGamesViewModel.StatusMessage;
            
            // Subscribe to status message changes
            if (_installedGamesViewModel != null)
            {
                _installedGamesViewModel.PropertyChanged -= ViewModel_PropertyChanged;
                _installedGamesViewModel.PropertyChanged += ViewModel_PropertyChanged;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to Installed Games view");
            StatusMessage = $"Error: {ex.Message}";
        }
    }
    
    [RelayCommand]
    private void NavigateToDxvkVersions()
    {
        try
        {
            _logger.LogDebug("Navigating to DXVK Versions view");
            CurrentViewModel = _dxvkVersionsViewModel ??= new DxvkVersionsViewModel(_serviceProvider.GetRequiredService<IDxvkVersionService>());
            StatusMessage = _dxvkVersionsViewModel.StatusMessage;
            
            // Subscribe to status message changes
            if (_dxvkVersionsViewModel != null)
            {
                _dxvkVersionsViewModel.PropertyChanged -= ViewModel_PropertyChanged;
                _dxvkVersionsViewModel.PropertyChanged += ViewModel_PropertyChanged;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to DXVK Versions view");
            StatusMessage = $"Error: {ex.Message}";
        }
    }
    
    [RelayCommand]
    private void NavigateToDxvkGplasync()
    {
        try
        {
            _logger.LogDebug("Navigating to DXVK-gplasync view");
            CurrentViewModel = _dxvkGplasyncViewModel ??= new DxvkGplasyncViewModel(_serviceProvider.GetRequiredService<IDxvkVersionService>());
            StatusMessage = _dxvkGplasyncViewModel.StatusMessage;
            
            // Subscribe to status message changes
            if (_dxvkGplasyncViewModel != null)
            {
                _dxvkGplasyncViewModel.PropertyChanged -= ViewModel_PropertyChanged;
                _dxvkGplasyncViewModel.PropertyChanged += ViewModel_PropertyChanged;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to DXVK-gplasync view");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    // Helper method to update status from view models
    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DxvkVersionsViewModel.StatusMessage) || 
            e.PropertyName == nameof(DxvkGplasyncViewModel.StatusMessage) ||
            e.PropertyName == nameof(InstalledGamesViewModel.StatusMessage))
        {
            if (sender is DxvkVersionsViewModel dxvkVM && CurrentViewModel == dxvkVM)
            {
                StatusMessage = dxvkVM.StatusMessage;
            }
            else if (sender is DxvkGplasyncViewModel gplasyncVM && CurrentViewModel == gplasyncVM)
            {
                StatusMessage = gplasyncVM.StatusMessage;
            }
            else if (sender is InstalledGamesViewModel gamesVM && CurrentViewModel == gamesVM)
            {
                StatusMessage = gamesVM.StatusMessage;
            }
        }
    }
}