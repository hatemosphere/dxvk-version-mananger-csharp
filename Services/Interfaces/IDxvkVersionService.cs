using DxvkVersionManager.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DxvkVersionManager.Services.Interfaces;

public interface IDxvkVersionService
{
    Task<List<DxvkRelease>> FetchDxvkReleasesAsync();
    Task<List<DxvkRelease>> FetchDxvkGplasyncReleasesAsync();
    Task<bool> DownloadDxvkVersionAsync(string version, string downloadUrl, IProgress<(int progress, string message)>? progress = null);
    Task<bool> DownloadDxvkGplasyncVersionAsync(string version, string downloadUrl, IProgress<(int progress, string message)>? progress = null);
    Task<InstalledVersions> GetInstalledVersionsAsync();
    Task InitializeCacheDirectoriesAsync();
    bool IsVersionDownloaded(string version, bool isGplasync);
}