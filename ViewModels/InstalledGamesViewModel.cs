using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DxvkVersionManager.Models;
using DxvkVersionManager.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace DxvkVersionManager.ViewModels;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public partial class InstalledGamesViewModel : ViewModelBase
{
    private readonly ISteamService _steamService;
    private readonly IDxvkManagerService _dxvkManagerService;
    
    [ObservableProperty]
    private ObservableCollection<SteamGame> _games = new();
    
    [ObservableProperty]
    private SteamGame? _selectedGame;
    
    [ObservableProperty]
    private bool _isRefreshing;
    
    [ObservableProperty]
    private string _statusMessage = string.Empty;
    
    // Renamed from _showDxvkSelectionDialog to avoid conflict with the command
    [ObservableProperty]
    private bool _isDxvkSelectionDialogOpen;
    
    [ObservableProperty]
    private DxvkSelectionDialogViewModel? _dxvkSelectionViewModel;
    
    [ObservableProperty]
    private OperationResultDialogViewModel? _operationResultViewModel;
    
    [ObservableProperty]
    private bool _showResultDialog;
    
    [ObservableProperty]
    private OperationResult? _operationResult;
    
    public InstalledGamesViewModel(ISteamService steamService, IDxvkManagerService dxvkManagerService)
    {
        _steamService = steamService;
        _dxvkManagerService = dxvkManagerService;
        
        _ = LoadGamesAsync();
    }
    
    [RelayCommand]
    private async Task LoadGamesAsync()
    {
        IsLoading = true;
        IsRefreshing = true;
        StatusMessage = "Loading installed Steam games...";
        
        try
        {
            var games = await _steamService.GetInstalledGamesAsync();
            
            Games.Clear();
            foreach (var game in games)
            {
                Games.Add(game);
            }
            
            StatusMessage = $"Found {games.Count} Steam games";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading Steam games: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            IsRefreshing = false;
        }
    }
    
    [RelayCommand]
    private void SelectGame(SteamGame game)
    {
        SelectedGame = game;
    }
    
    [RelayCommand]
    private void ShowDxvkSelectionDialog(SteamGame game)
    {
        SelectedGame = game;
        
        // Create a new DxvkSelectionDialogViewModel
        DxvkSelectionViewModel = new DxvkSelectionDialogViewModel(
            game,
            App.Services.GetRequiredService<IDxvkVersionService>(),
            _dxvkManagerService
        );
        
        // Subscribe to events
        DxvkSelectionViewModel.CloseRequested += OnDxvkSelectionDialogCloseRequested;
        DxvkSelectionViewModel.OperationCompleted += OnDxvkOperationCompleted;
        
        IsDxvkSelectionDialogOpen = true;
    }
    
    private void OnDxvkSelectionDialogCloseRequested()
    {
        IsDxvkSelectionDialogOpen = false;
        
        // Unsubscribe from events
        if (DxvkSelectionViewModel != null)
        {
            DxvkSelectionViewModel.CloseRequested -= OnDxvkSelectionDialogCloseRequested;
            DxvkSelectionViewModel.OperationCompleted -= OnDxvkOperationCompleted;
            DxvkSelectionViewModel = null;
        }
    }
    
    private void OnDxvkOperationCompleted(OperationResult result)
    {
        OperationResult = result;
        OperationResultViewModel = new OperationResultDialogViewModel(result);
        OperationResultViewModel.CloseRequested += OnOperationResultDialogClosed;
        ShowResultDialog = true;
        
        // Refresh the game list to update the status
        _ = LoadGamesAsync();
    }
    
    private void OnOperationResultDialogClosed()
    {
        ShowResultDialog = false;
        if (OperationResultViewModel != null)
        {
            OperationResultViewModel.CloseRequested -= OnOperationResultDialogClosed;
            OperationResultViewModel = null;
        }
    }
    
    [RelayCommand]
    private void ApplyDxvk(SteamGame game)
    {
        if (game == null) return;
        
        // Show the DXVK selection dialog
        ShowDxvkSelectionDialog(game);
    }
    
    [RelayCommand]
    private async Task SaveCustomGameMetadataAsync(Dictionary<string, object> metadataUpdates)
    {
        if (SelectedGame == null) return;
        
        IsLoading = true;
        StatusMessage = $"Saving custom metadata for {SelectedGame.Name}...";
        
        try
        {
            var success = await _steamService.SaveCustomGameMetadataAsync(SelectedGame.AppId, metadataUpdates);
            
            if (success)
            {
                StatusMessage = $"Successfully saved custom metadata for {SelectedGame.Name}";
                // Refresh the game to get updated metadata
                await LoadGamesAsync();
            }
            else
            {
                StatusMessage = $"Failed to save custom metadata for {SelectedGame.Name}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving custom metadata: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    [RelayCommand]
    private void CloseResultDialog()
    {
        OnOperationResultDialogClosed();
    }
    
    [RelayCommand]
    private async Task UpdateDirectXVersionAsync(ComboBoxItem selectedItem)
    {
        if (selectedItem == null || selectedItem.Tag == null) return;
        
        var appId = selectedItem.Tag.ToString();
        if (string.IsNullOrEmpty(appId)) return;
        
        var game = Games.FirstOrDefault(g => g.AppId == appId);
        if (game == null || game.Metadata == null) return;
        
        var value = selectedItem.Content?.ToString();
        if (string.IsNullOrEmpty(value)) return;
        
        // Skip the "Choose Direct3D version" option
        if (value.Contains("Choose")) return;
        
        // Update in memory immediately
        game.Metadata.Direct3dVersions = value;
        
        // Create a dictionary for the server update
        var metadataUpdates = new Dictionary<string, object>
        {
            ["direct3dVersions"] = value
        };
        
        // Save the metadata
        await SaveMetadataByAppIdAsync(appId, metadataUpdates);
    }
    
    [RelayCommand]
    private async Task UpdateArchitectureAsync(ComboBoxItem selectedItem)
    {
        if (selectedItem == null || selectedItem.Tag == null) return;
        
        var appId = selectedItem.Tag.ToString();
        if (string.IsNullOrEmpty(appId)) return;
        
        var game = Games.FirstOrDefault(g => g.AppId == appId);
        if (game == null || game.Metadata == null) return;
        
        var value = selectedItem.Content?.ToString();
        if (string.IsNullOrEmpty(value)) return;
        
        // Skip the "Choose architecture" option
        if (value.Contains("Choose")) return;
        
        // Update in memory immediately
        if (value == "32-bit")
        {
            game.Metadata.Executable32bit = true;
            game.Metadata.Executable64bit = false;
        }
        else if (value == "64-bit")
        {
            game.Metadata.Executable32bit = false;
            game.Metadata.Executable64bit = true;
        }
        
        // Create a dictionary for the server update
        var metadataUpdates = new Dictionary<string, object>();
        
        if (value == "32-bit")
        {
            metadataUpdates["executable32bit"] = true;
            metadataUpdates["executable64bit"] = false;
        }
        else if (value == "64-bit")
        {
            metadataUpdates["executable32bit"] = false;
            metadataUpdates["executable64bit"] = true;
        }
        
        // Save the metadata
        await SaveMetadataByAppIdAsync(appId, metadataUpdates);
    }
    
    // Helper method to save metadata using a specific appId
    private async Task SaveMetadataByAppIdAsync(string appId, Dictionary<string, object> metadataUpdates)
    {
        var game = Games.FirstOrDefault(g => g.AppId == appId);
        if (game == null) return;
        
        try
        {
            StatusMessage = $"Saving metadata for {game.Name}...";
            var success = await _steamService.SaveCustomGameMetadataAsync(appId, metadataUpdates);
            
            if (success)
            {
                StatusMessage = $"Successfully saved metadata for {game.Name}";
            }
            else
            {
                StatusMessage = $"Failed to save metadata for {game.Name}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving metadata: {ex.Message}";
        }
    }
    
    [RelayCommand]
    private void CloseDxvkSelectionDialog()
    {
        OnDxvkSelectionDialogCloseRequested();
    }
    
    [RelayCommand]
    private async Task DiagnoseDxvkAsync(SteamGame game)
    {
        if (game == null) return;
        
        IsLoading = true;
        StatusMessage = $"Running DXVK diagnostics for {game.Name}...";
        
        try
        {
            var result = await _dxvkManagerService.DiagnoseAndLogDxvkEnvironmentAsync(game);
            
            // Create operation result view model with detailed diagnostic info
            OperationResult = result;
            OperationResultViewModel = new OperationResultDialogViewModel(result);
            OperationResultViewModel.CloseRequested += OnOperationResultDialogClosed;
            ShowResultDialog = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error running DXVK diagnostics: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    [RelayCommand]
    private async Task RevertDxvkChangesAsync(SteamGame game)
    {
        if (game == null) return;
        
        IsLoading = true;
        StatusMessage = $"Reverting DXVK changes for {game.Name}...";
        
        try
        {
            var result = await _dxvkManagerService.RevertDxvkChangesAsync(game);
            OperationResult = result;
            OperationResultViewModel = new OperationResultDialogViewModel(result);
            OperationResultViewModel.CloseRequested += OnOperationResultDialogClosed;
            ShowResultDialog = true;
            
            // Refresh the game list to update the status
            await LoadGamesAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error reverting DXVK changes: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}