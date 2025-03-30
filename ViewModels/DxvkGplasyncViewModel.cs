using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DxvkVersionManager.Models;
using DxvkVersionManager.Services.Interfaces;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Windows.Threading;

namespace DxvkVersionManager.ViewModels;

public partial class DxvkGplasyncViewModel : ViewModelBase
{
    private readonly IDxvkVersionService? _dxvkVersionService;
    
    [ObservableProperty]
    private ObservableCollection<DxvkRelease> _releases = new();
    
    [ObservableProperty]
    private string _statusMessage = "Loading DXVK-gplasync releases...";
    
    [ObservableProperty]
    private bool _isRefreshing;
    
    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private int _downloadProgress;
    
    public DxvkGplasyncViewModel(IDxvkVersionService dxvkVersionService)
    {
        _dxvkVersionService = dxvkVersionService;
        StatusMessage = "Loading DXVK-gplasync releases...";
        
        // Load data automatically
        _ = RefreshAsync();
    }
    
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_dxvkVersionService == null) return;
        
        IsRefreshing = true;
        Releases.Clear();
        StatusMessage = "Loading DXVK-gplasync releases...";
        
        try
        {
            if (_dxvkVersionService == null)
            {
                StatusMessage = "Service unavailable";
                return;
            }
            
            var gplasyncReleases = await _dxvkVersionService.FetchDxvkGplasyncReleasesAsync();
            
            // Set type and check if already downloaded
            foreach (var release in gplasyncReleases)
            {
                release.Type = "DXVK-gplasync";
                
                // Check if this version is already downloaded
                release.IsDownloaded = _dxvkVersionService.IsVersionDownloaded(release.Version, true);
            }
            
            // Sort by date (newest first)
            gplasyncReleases = gplasyncReleases
                .OrderByDescending(r => r.Date)
                .ToList();
            
            foreach (var release in gplasyncReleases)
            {
                Releases.Add(release);
            }
            
            StatusMessage = $"Found {gplasyncReleases.Count} DXVK-gplasync releases";
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = $"Error connecting to release servers: {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private async Task DownloadAsync(DxvkRelease release)
    {
        if (_dxvkVersionService == null || release == null) 
        {
            StatusMessage = "Cannot download: invalid release or service unavailable";
            return;
        }
        
        // Validate the release
        if (string.IsNullOrEmpty(release.Version) || string.IsNullOrEmpty(release.DownloadUrl))
        {
            StatusMessage = "Cannot download error releases";
            return;
        }
        
        try
        {
            IsDownloading = true;
            StatusMessage = $"Preparing to download {release.Version}...";
            DownloadProgress = 0;
            
            // Create progress reporter that updates both progress bar and status message
            var progress = new Progress<(int progress, string message)>(update => 
            {
                DownloadProgress = update.progress;
                StatusMessage = update.message;
            });
            
            bool success = await _dxvkVersionService.DownloadDxvkGplasyncVersionAsync(
                release.Version, 
                release.DownloadUrl,
                progress);
            
            // Ensure progress shows 100% at the end
            DownloadProgress = 100;
            
            if (success)
            {
                StatusMessage = $"Successfully downloaded {release.Version}";
                // Update IsDownloaded status after successful download
                release.IsDownloaded = true;
            }
            else
            {
                // Check if the status message indicates missing DLLs
                if (StatusMessage.Contains("DLLs not found") || StatusMessage.Contains("no required DLLs"))
                {
                    StatusMessage = $"Downloaded {release.Version} but no required DLLs found";
                }
                else
                {
                    StatusMessage = $"Failed to download {release.Version}";
                }
                
                // Don't mark as downloaded since it wasn't successful
                release.IsDownloaded = false;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error downloading {release.Version}: {ex.Message}";
        }
        finally
        {
            // Small delay before hiding the download overlay
            await Task.Delay(1000);
            IsDownloading = false;
        }
    }
}