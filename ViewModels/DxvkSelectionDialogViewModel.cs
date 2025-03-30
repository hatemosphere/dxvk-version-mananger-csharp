using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DxvkVersionManager.Models;
using DxvkVersionManager.Services.Interfaces;
using System.Collections.ObjectModel;

namespace DxvkVersionManager.ViewModels;

public partial class DxvkSelectionDialogViewModel : ViewModelBase
{
    private readonly IDxvkVersionService _dxvkVersionService;
    private readonly IDxvkManagerService _dxvkManagerService;
    
    [ObservableProperty]
    private SteamGame _game;
    
    [ObservableProperty]
    private ObservableCollection<string> _dxvkVersions = new();
    
    [ObservableProperty]
    private ObservableCollection<string> _dxvkGplasyncVersions = new();
    
    private string? _selectedDxvkVersion;
    public string? SelectedDxvkVersion
    {
        get => _selectedDxvkVersion;
        set
        {
            if (SetProperty(ref _selectedDxvkVersion, value))
            {
                OnPropertyChanged(nameof(CanApply));
                // If selecting a DXVK version, clear the gplasync selection
                if (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(SelectedDxvkGplasyncVersion))
                {
                    SelectedDxvkGplasyncVersion = null;
                }
            }
        }
    }
    
    private string? _selectedDxvkGplasyncVersion;
    public string? SelectedDxvkGplasyncVersion
    {
        get => _selectedDxvkGplasyncVersion;
        set
        {
            if (SetProperty(ref _selectedDxvkGplasyncVersion, value))
            {
                OnPropertyChanged(nameof(CanApply));
                // If selecting a DXVK-gplasync version, clear the regular DXVK selection
                if (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(SelectedDxvkVersion))
                {
                    SelectedDxvkVersion = null;
                }
            }
        }
    }
    
    [ObservableProperty]
    private string _dialogTitle;
    
    [ObservableProperty]
    private bool _hasVersions;
    
    public bool CanApply => !string.IsNullOrEmpty(SelectedDxvkVersion) || !string.IsNullOrEmpty(SelectedDxvkGplasyncVersion);
    
    // Event to close the dialog
    public event Action? CloseRequested;
    
    // Event when operation completes
    public event Action<OperationResult>? OperationCompleted;
    
    public DxvkSelectionDialogViewModel(
        SteamGame game,
        IDxvkVersionService dxvkVersionService,
        IDxvkManagerService dxvkManagerService)
    {
        _game = game;
        _dxvkVersionService = dxvkVersionService;
        _dxvkManagerService = dxvkManagerService;
        
        // Set dialog title based on current state
        bool isPatched = game.DxvkStatus?.Patched ?? false;
        _dialogTitle = isPatched ? $"Update DXVK for {game.Name}" : $"Apply DXVK to {game.Name}";
        
        _ = LoadVersionsAsync();
    }
    
    private async Task LoadVersionsAsync()
    {
        IsLoading = true;
        
        try
        {
            var installedVersions = await _dxvkVersionService.GetInstalledVersionsAsync();
            
            // Add versions to collections
            DxvkVersions.Clear();
            foreach (var version in installedVersions.Dxvk)
            {
                DxvkVersions.Add(version);
            }
            
            DxvkGplasyncVersions.Clear();
            foreach (var version in installedVersions.DxvkGplasync)
            {
                DxvkGplasyncVersions.Add(version);
            }
            
            // Check if we have any versions
            HasVersions = DxvkVersions.Count > 0 || DxvkGplasyncVersions.Count > 0;
            
            // Pre-select current version if applicable
            if (Game.DxvkStatus?.Patched ?? false)
            {
                if (Game.DxvkStatus.DxvkType == "dxvk" && !string.IsNullOrEmpty(Game.DxvkStatus.DxvkVersion))
                {
                    SelectedDxvkVersion = DxvkVersions.Contains(Game.DxvkStatus.DxvkVersion) 
                        ? Game.DxvkStatus.DxvkVersion 
                        : null;
                }
                else if (Game.DxvkStatus.DxvkType == "dxvk-gplasync" && !string.IsNullOrEmpty(Game.DxvkStatus.DxvkVersion))
                {
                    SelectedDxvkGplasyncVersion = DxvkGplasyncVersions.Contains(Game.DxvkStatus.DxvkVersion) 
                        ? Game.DxvkStatus.DxvkVersion 
                        : null;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading DXVK versions: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
        
        // Notify property change for CanApply
        OnPropertyChanged(nameof(CanApply));
    }
    
    [RelayCommand]
    private void CloseDialog()
    {
        CloseRequested?.Invoke();
    }
    
    [RelayCommand]
    private async Task ApplyDxvk()
    {
        IsLoading = true;
        
        try
        {
            OperationResult result;
            
            // Determine which version to apply
            if (!string.IsNullOrEmpty(SelectedDxvkVersion))
            {
                // Apply DXVK
                result = await _dxvkManagerService.ApplyDxvkToGameAsync(Game, "dxvk", SelectedDxvkVersion);
            }
            else if (!string.IsNullOrEmpty(SelectedDxvkGplasyncVersion))
            {
                // Apply DXVK-gplasync
                result = await _dxvkManagerService.ApplyDxvkToGameAsync(Game, "dxvk-gplasync", SelectedDxvkGplasyncVersion);
            }
            else
            {
                result = OperationResult.Failed("No DXVK version selected");
            }
            
            // Notify operation completed
            OperationCompleted?.Invoke(result);
            
            // Close dialog
            CloseRequested?.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error applying DXVK: {ex.Message}");
            OperationCompleted?.Invoke(OperationResult.Failed($"Error applying DXVK: {ex.Message}"));
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    [RelayCommand]
    private async Task RevertToDirectX()
    {
        IsLoading = true;
        
        try
        {
            // Use the new unified revert method
            var result = await _dxvkManagerService.RevertDxvkChangesAsync(Game);
            
            // Notify operation completed
            OperationCompleted?.Invoke(result);
            
            // Close dialog
            CloseRequested?.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reverting to DirectX: {ex.Message}");
            OperationCompleted?.Invoke(OperationResult.Failed($"Error reverting to DirectX: {ex.Message}"));
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    [RelayCommand]
    private async Task DiagnoseDxvk()
    {
        IsLoading = true;
        
        try
        {
            var result = await _dxvkManagerService.DiagnoseAndLogDxvkEnvironmentAsync(Game);
            
            // Notify operation completed
            OperationCompleted?.Invoke(result);
            
            // Don't close dialog after diagnostics
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during DXVK diagnostics: {ex.Message}");
            OperationCompleted?.Invoke(OperationResult.Failed($"Error during DXVK diagnostics: {ex.Message}"));
        }
        finally
        {
            IsLoading = false;
        }
    }
}