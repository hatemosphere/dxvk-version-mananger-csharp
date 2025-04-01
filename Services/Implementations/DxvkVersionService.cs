using DxvkVersionManager.Models;
using DxvkVersionManager.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpCompress.Archives;
using SharpCompress.Archives.Tar;
using SharpCompress.Common;
using SharpCompress.Readers;
using System.Net.Http;
using System.IO.Compression;

namespace DxvkVersionManager.Services.Implementations;

public class DxvkVersionService : IDxvkVersionService
{
    private readonly HttpClient _httpClient;
    private readonly string _dataPath;
    private readonly string _dxvkCachePath;
    private readonly string _dxvkGplasyncCachePath;
    private readonly LoggingService _logger;
    
    public DxvkVersionService(string dataPath)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "DxvkVersionManager/1.0");
        _logger = LoggingService.Instance;
        
        _dataPath = dataPath; // Store the base data path
        _dxvkCachePath = Path.Combine(_dataPath, "dxvk-cache");
        _dxvkGplasyncCachePath = Path.Combine(_dataPath, "dxvk-gplasync-cache");
        
        _logger.LogInformation($"DXVK cache path set to: {_dxvkCachePath}");
        _logger.LogInformation($"DXVK GPLAsync cache path set to: {_dxvkGplasyncCachePath}");
        
        // Ensure cache directories exist (moved from InitializeCacheDirectoriesAsync for earlier check)
        try
        {
            Directory.CreateDirectory(_dxvkCachePath);
            Directory.CreateDirectory(_dxvkGplasyncCachePath);
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, "Failed to create cache directories on initialization.");
        }
    }
    
    public Task InitializeCacheDirectoriesAsync()
    {
        // Directories are now created in the constructor
        return Task.CompletedTask;
    }
    
    public async Task<List<DxvkRelease>> FetchDxvkReleasesAsync()
    {
        try
        {
            _logger.LogInformation("Fetching DXVK releases...");
            var response = await _httpClient.GetStringAsync("https://api.github.com/repos/doitsujin/dxvk/releases");
            var releases = JsonConvert.DeserializeObject<List<DxvkRelease>>(response) ?? new List<DxvkRelease>();
            
            // Process each release to set download URL
            foreach (var release in releases)
            {
                var tarAsset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".tar.gz"));
                if (tarAsset != null)
                {
                    release.DownloadUrl = tarAsset.BrowserDownloadUrl;
                }
            }
            
            // Filter out releases without download URLs
            releases = releases.Where(r => !string.IsNullOrEmpty(r.DownloadUrl)).ToList();
            
            _logger.LogInformation($"Found {releases.Count} DXVK releases");
            return releases;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch DXVK releases");
            throw;
        }
    }
    
    public async Task<List<DxvkRelease>> FetchDxvkGplasyncReleasesAsync()
    {
        try
        {
            _logger.LogInformation("Fetching DXVK-gplasync releases from GitLab...");
            
            // URL encode the project path
            var encodedPath = Uri.EscapeDataString("Ph42oN/dxvk-gplasync");
            var projectUrl = $"https://gitlab.com/api/v4/projects/{encodedPath}";
            
            // First get the project ID
            var projectResponse = await _httpClient.GetStringAsync(projectUrl);
            var project = JsonConvert.DeserializeObject<GitLabProject>(projectResponse);
            
            if (project == null)
            {
                throw new Exception("Failed to get project information from GitLab");
            }
            
            // Now fetch releases using the project ID
            var releasesUrl = $"https://gitlab.com/api/v4/projects/{project.Id}/releases";
            var response = await _httpClient.GetStringAsync(releasesUrl);
            var releases = JsonConvert.DeserializeObject<List<GitLabRelease>>(response) ?? new List<GitLabRelease>();
            
            // Convert GitLab releases to our model
            var dxvkReleases = new List<DxvkRelease>();
            foreach (var release in releases)
            {
                // For GitLab, we need to construct the URL to the raw file
                // Format: https://gitlab.com/Ph42oN/dxvk-gplasync/-/raw/main/releases/dxvk-gplasync-{tag}-1.tar.gz?ref_type=heads
                var tagName = release.TagName;
                if (!string.IsNullOrEmpty(tagName))
                {
                    // Remove the extra "-1" suffix, as tagName already contains a "-1" suffix in most cases
                    var downloadUrl = $"https://gitlab.com/Ph42oN/dxvk-gplasync/-/raw/main/releases/dxvk-gplasync-{tagName}.tar.gz?ref_type=heads";
                    
                    dxvkReleases.Add(new DxvkRelease
                    {
                        Version = tagName,
                        Date = release.ReleasedAt,
                        DownloadUrl = downloadUrl
                    });
                    
                    _logger.LogDebug($"Added DXVK-gplasync release {tagName} with URL: {downloadUrl}");
                }
            }
            
            _logger.LogInformation($"Found {dxvkReleases.Count} DXVK-gplasync releases");
            return dxvkReleases;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch DXVK-gplasync releases from GitLab");
            throw;
        }
    }
    
    public async Task<bool> DownloadDxvkVersionAsync(string version, string downloadUrl, IProgress<(int progress, string message)>? progress = null)
    {
        return await DownloadVersionAsync(version, downloadUrl, _dxvkCachePath, "DXVK", progress);
    }
    
    public async Task<bool> DownloadDxvkGplasyncVersionAsync(string version, string downloadUrl, IProgress<(int progress, string message)>? progress = null)
    {
        try 
        {
            _logger.LogInformation($"Attempting to download DXVK-gplasync version {version} from: {downloadUrl}");
            return await DownloadVersionAsync(version, downloadUrl, _dxvkGplasyncCachePath, "DXVK-gplasync", progress);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("404"))
        {
            // If we get a 404, we might have constructed the URL incorrectly
            _logger.LogWarning($"404 Not Found for URL: {downloadUrl}. Attempting fallback URL...");
            
            // Try a fallback URL format (with "-1" suffix) that might be used for some versions
            string fallbackUrl = downloadUrl.Replace(".tar.gz", "-1.tar.gz");
            _logger.LogInformation($"Trying fallback URL: {fallbackUrl}");
            
            if (progress != null)
                progress.Report((0, $"Original URL not found, trying alternative download location..."));
                
            return await DownloadVersionAsync(version, fallbackUrl, _dxvkGplasyncCachePath, "DXVK-gplasync", progress);
        }
    }
    
    private async Task<bool> DownloadVersionAsync(string version, string downloadUrl, string cachePath, string type, IProgress<(int progress, string message)>? progress = null)
    {
        try
        {
            _logger.LogInformation($"Downloading {type} version {version}...");
            progress?.Report((0, $"Preparing to download {type} version {version}..."));
            
            // Clean up the version string to use in folder names
            string safeFolderVersion = version;
            if (version.StartsWith("v"))
            {
                safeFolderVersion = version; // Keep the 'v' prefix for consistency
            }
            
            // Check if version is already downloaded
            var versionPath = Path.Combine(cachePath, safeFolderVersion);
            if (Directory.Exists(versionPath))
            {
                // Don't just check if directory exists, but if it contains files
                var files = Directory.GetFiles(versionPath, "*.dll", SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    _logger.LogInformation($"{type} version {version} already exists in cache with {files.Length} DLL files");
                    
                    // We are explicitly re-downloading, delete the existing directory first
                    _logger.LogInformation($"Re-downloading {type} version {version}...");
                    progress?.Report((10, $"Removing existing {type} version {version}..."));
                    
                    try
                    {
                        // Delete existing directory and its contents
                        Directory.Delete(versionPath, true);
                        _logger.LogInformation($"Deleted existing directory: {versionPath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error deleting existing directory: {versionPath}");
                        // Continue with download even if delete fails
                    }
                }
                else
                {
                    _logger.LogWarning($"{type} version {version} directory exists but is empty or missing DLLs, re-downloading...");
                    // Continue to download since the directory is empty or doesn't contain DLLs
                }
            }
            
            // Download the file
            var tempFilePath = Path.Combine(Path.GetTempPath(), $"{type}-{safeFolderVersion}.tar.gz");
            progress?.Report((15, $"Downloading {type} version {version}..."));
            
            using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                
                // Get content length for progress reporting if available
                var contentLength = response.Content.Headers.ContentLength;
                using (var contentStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = File.Create(tempFilePath))
                {
                    // If we have content length, report download progress
                    if (contentLength.HasValue && contentLength.Value > 0)
                    {
                        var buffer = new byte[8192];
                        var totalBytesRead = 0L;
                        var bytesRead = 0;
                        
                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            
                            totalBytesRead += bytesRead;
                            var progressPercent = (int)((totalBytesRead * 35) / contentLength.Value) + 15;
                            progress?.Report((progressPercent, $"Downloading {type} version {version}: {progressPercent}%"));
                        }
                    }
                    else
                    {
                        // No content length available, just copy stream
                        await contentStream.CopyToAsync(fileStream);
                    }
                }
            }
            
            _logger.LogInformation($"Downloaded {type} version {version} to {tempFilePath}");
            progress?.Report((50, $"Download complete. Extracting {type} version {version}..."));
            
            // Create version directory
            Directory.CreateDirectory(versionPath);
            
            // Extract the archive
            _logger.LogInformation($"Extracting archive: {tempFilePath} to {versionPath}");
            progress?.Report((55, $"Extracting archive..."));
            
            using (var archive = ArchiveFactory.Open(tempFilePath))
            {
                var reader = archive.ExtractAllEntries();
                bool entriesExtracted = false;
                int totalEntriesProcessed = 0;
                bool hasRootDir = false;
                string rootDir = "";
                int totalEntries = archive.Entries.Count();
                
                // First pass - check if there's a root directory structure
                foreach (var entry in archive.Entries)
                {
                    if (entry == null) continue;
                    
                    var entryPath = entry.Key;
                    if (string.IsNullOrEmpty(entryPath)) continue;
                    
                    // Check if this is a directory entry at the root level (e.g., "dxvk-2.6/")
                    if (entry.IsDirectory && entryPath.Count(c => c == '/') <= 1)
                    {
                        hasRootDir = true;
                        rootDir = entryPath.TrimEnd('/');
                        _logger.LogDebug($"Found root directory in archive: {rootDir}");
                        break;
                    }
                }
                
                // Now extract all entries
                int extractedCount = 0;
                while (reader.MoveToNextEntry())
                {
                    // Comprehensive null checks
                    if (reader.Entry == null) continue;

                    // Skip if Entry or Key is null or empty
                    var entryPath = reader.Entry.Key;
                    if (string.IsNullOrEmpty(entryPath)) continue;

                    totalEntriesProcessed++;
                    extractedCount++;
                    
                    if (totalEntries > 0)
                    {
                        var extractProgress = (int)((extractedCount * 45) / totalEntries) + 55;
                        progress?.Report((extractProgress, $"Extracting files: {extractProgress}%"));
                    }
                    
                    _logger.LogDebug($"Processing entry: {entryPath}, IsDirectory: {reader.Entry.IsDirectory}");

                    // Skip directories
                    if (reader.Entry.IsDirectory) continue;

                    string targetPath;
                    
                    // If we have a root directory, strip it from paths when extracting
                    if (hasRootDir && entryPath.StartsWith(rootDir, StringComparison.OrdinalIgnoreCase))
                    {
                        // Get path relative to root dir
                        string relativePath = entryPath.Substring(rootDir.Length).TrimStart('/');
                        targetPath = Path.Combine(versionPath, relativePath);
                    }
                    else
                    {
                        targetPath = Path.Combine(versionPath, entryPath);
                    }

                    // Create target directory if it doesn't exist
                    string? targetDir = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    _logger.LogDebug($"Extracting file to: {targetPath}");
                    
                    try
                    {
                        reader.WriteEntryToFile(targetPath, new ExtractionOptions
                        {
                            ExtractFullPath = false,
                            Overwrite = true
                        });
                        entriesExtracted = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Error extracting entry {entryPath}: {ex.Message}");
                        // Continue with next entry
                    }
                }

                // Debug logging
                _logger.LogInformation($"Total entries processed: {totalEntriesProcessed}, Files extracted: {entriesExtracted}");

                // Ensure some entries were extracted
                if (!entriesExtracted)
                {
                    _logger.LogWarning($"No files extracted with SharpCompress. Attempting fallback extraction for {type} {version}...");
                    progress?.Report((80, $"Trying alternative extraction method..."));
                    
                    // Fallback to manual extraction for problematic archives
                    try
                    {
                        // For tar.gz files, try using GZipStream + manually handling tar format
                        if (tempFilePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                        {
                            // First extract the .gz to get the tar
                            string tarPath = Path.Combine(Path.GetTempPath(), $"{type}-{version}.tar");
                            using (var gzipFile = File.OpenRead(tempFilePath))
                            using (var gzipStream = new GZipStream(gzipFile, CompressionMode.Decompress))
                            using (var tarFile = File.Create(tarPath))
                            {
                                _logger.LogDebug($"Extracting .gz to tar: {tarPath}");
                                gzipStream.CopyTo(tarFile);
                            }
                            
                            // Now use SharpCompress on the raw tar file
                            using (var tarArchive = ArchiveFactory.Open(tarPath))
                            {
                                var tarReader = tarArchive.ExtractAllEntries();
                                while (tarReader.MoveToNextEntry())
                                {
                                    if (tarReader.Entry == null || tarReader.Entry.IsDirectory) continue;
                                    
                                    var entryPath = tarReader.Entry.Key;
                                    if (string.IsNullOrEmpty(entryPath)) continue;
                                    
                                    var targetPath = Path.Combine(versionPath, entryPath);
                                    string? targetDir = Path.GetDirectoryName(targetPath);
                                    if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                                    {
                                        Directory.CreateDirectory(targetDir);
                                    }
                                    
                                    _logger.LogDebug($"Fallback extracting: {entryPath}");
                                    tarReader.WriteEntryToFile(targetPath, new ExtractionOptions
                                    {
                                        ExtractFullPath = false,
                                        Overwrite = true
                                    });
                                    entriesExtracted = true;
                                }
                            }
                            
                            // Clean up temp tar file
                            if (File.Exists(tarPath))
                            {
                                File.Delete(tarPath);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Fallback extraction method failed");
                    }
                    
                    // If we still couldn't extract any files
                    if (!entriesExtracted)
                    {
                        throw new InvalidOperationException($"No files were extracted from the archive for {type} {version} after multiple attempts");
                    }
                }
            }
            
            // Delete the temp file
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
                _logger.LogDebug($"Deleted temporary file: {tempFilePath}");
            }
            
            // Check if the extracted files contain the expected DLLs
            var expectedDlls = new[] { "d3d9.dll", "d3d10.dll", "d3d10_1.dll", "d3d11.dll", "dxgi.dll" };
            var x64Path = Path.Combine(versionPath, "x64");
            var x32Path = Path.Combine(versionPath, "x32");

            // Only require ANY of the expected DLLs, not ALL of them
            bool hasX64Dlls = Directory.Exists(x64Path) && expectedDlls.Any(dll => File.Exists(Path.Combine(x64Path, dll)));
            bool hasX32Dlls = Directory.Exists(x32Path) && expectedDlls.Any(dll => File.Exists(Path.Combine(x32Path, dll)));

            if (!hasX64Dlls && !hasX32Dlls)
            {
                // Look for DLLs directly in the version directory as fallback
                bool hasDllsInRoot = expectedDlls.Any(dll => File.Exists(Path.Combine(versionPath, dll)));
                
                // Also check for nested dxvk-[version] directory structure
                string nestedDirPattern = $"dxvk*{version.TrimStart('v')}*";
                var nestedDirs = Directory.GetDirectories(versionPath, nestedDirPattern);
                bool hasDllsInNestedDir = false;
                
                foreach (var nestedDir in nestedDirs)
                {
                    var nestedX64Path = Path.Combine(nestedDir, "x64");
                    var nestedX32Path = Path.Combine(nestedDir, "x32");
                    
                    if (Directory.Exists(nestedX64Path) && 
                        expectedDlls.Any(dll => File.Exists(Path.Combine(nestedX64Path, dll))))
                    {
                        hasDllsInNestedDir = true;
                        _logger.LogInformation($"Found DLLs in nested x64 directory: {nestedX64Path}");
                        break;
                    }
                    
                    if (Directory.Exists(nestedX32Path) && 
                        expectedDlls.Any(dll => File.Exists(Path.Combine(nestedX32Path, dll))))
                    {
                        hasDllsInNestedDir = true;
                        _logger.LogInformation($"Found DLLs in nested x32 directory: {nestedX32Path}");
                        break;
                    }
                }
                
                // Recursive check for any DirectX DLLs in the version directory
                bool hasDllsAnywhere = ContainsDirectXDlls(versionPath);
                
                if (!hasDllsInRoot && !hasDllsInNestedDir && !hasDllsAnywhere)
                {
                    // None of the expected DLLs were found
                    _logger.LogWarning($"No expected DLLs found in extracted archive for {type} {version}");
                    progress?.Report((100, $"Download completed but no required DLLs found"));
                    
                    // Don't delete the directory, leave it for inspection
                    return false;
                }
            }
            
            progress?.Report((100, $"Successfully downloaded and extracted {type} version {version}"));
            _logger.LogInformation($"Successfully downloaded and extracted {type} version {version}");
            return true;
        }
        catch (Exception ex)
        {
            // Log the download error with additional context
            string safeFolderVersion = version;
            if (version.StartsWith("v"))
            {
                safeFolderVersion = version; // Keep the 'v' prefix for consistency
            }
            
            // Create more detailed error message with contextual information
            var errorMessage = $"Error downloading {type} version {version}. " +
                              $"URL: {downloadUrl}, " + 
                              $"Cache Path: {cachePath}, " +
                              $"Temp File: {Path.Combine(Path.GetTempPath(), $"{type}-{safeFolderVersion}.tar.gz")}";
            
            _logger.LogError(ex, errorMessage);
            progress?.Report((0, $"Error: {ex.Message}"));
            return false;
        }
    }
    
    public Task<InstalledVersions> GetInstalledVersionsAsync()
    {
        var result = new InstalledVersions();
        
        try
        {
            // Get DXVK versions
            if (Directory.Exists(_dxvkCachePath))
            {
                result.Dxvk = Directory.GetDirectories(_dxvkCachePath)
                    .Select(Path.GetFileName)
                    .Where(dir => !string.IsNullOrEmpty(dir))
                    .Select(dir => dir!)
                    .ToList();
                _logger.LogDebug($"Found {result.Dxvk.Count} installed DXVK versions");
            }
            
            // Get DXVK-gplasync versions
            if (Directory.Exists(_dxvkGplasyncCachePath))
            {
                result.DxvkGplasync = Directory.GetDirectories(_dxvkGplasyncCachePath)
                    .Select(Path.GetFileName)
                    .Where(dir => !string.IsNullOrEmpty(dir))
                    .Select(dir => dir!)
                    .ToList();
                _logger.LogDebug($"Found {result.DxvkGplasync.Count} installed DXVK-gplasync versions");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting installed DXVK versions");
        }
        
        return Task.FromResult(result);
    }

    public bool IsVersionDownloaded(string version, bool isGplasync)
    {
        try
        {
            // Clean up the version string to use in folder names
            string safeFolderVersion = version;
            if (version.StartsWith("v"))
            {
                safeFolderVersion = version; // Keep the 'v' prefix for consistency
            }
            
            string cachePath = isGplasync ? _dxvkGplasyncCachePath : _dxvkCachePath;
            var versionPath = Path.Combine(cachePath, safeFolderVersion);
            
            if (!Directory.Exists(versionPath))
            {
                return false;
            }
            
            // Check if directory contains DLL files - now also looking in nested dxvk-version directories
            var files = Directory.GetFiles(versionPath, "*.dll", SearchOption.AllDirectories);
            if (files.Length > 0)
            {
                return true;
            }
            
            // Look for nested dxvk/gplasync directory structure
            string nestedDirPattern = $"dxvk*{version.TrimStart('v')}*";
            var nestedDirs = Directory.GetDirectories(versionPath, nestedDirPattern);
            
            foreach (var nestedDir in nestedDirs)
            {
                var nestedFiles = Directory.GetFiles(nestedDir, "*.dll", SearchOption.AllDirectories);
                if (nestedFiles.Length > 0)
                {
                    _logger.LogDebug($"Found DLLs in nested directory structure: {nestedDir}");
                    return true;
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error checking if version {version} is downloaded");
            return false;
        }
    }

    // Helper method to recursively search for any DirectX DLLs in a directory
    private bool ContainsDirectXDlls(string directory)
    {
        if (!Directory.Exists(directory))
            return false;
            
        // List of common DirectX DLL files to check for
        var expectedDlls = new[] { "d3d9.dll", "d3d10.dll", "d3d10_1.dll", "d3d11.dll", "dxgi.dll" };
        
        // First check if any DLLs exist directly in this directory
        foreach (var dll in expectedDlls)
        {
            if (File.Exists(Path.Combine(directory, dll)))
            {
                _logger.LogDebug($"Found DirectX DLL {dll} in {directory}");
                return true;
            }
        }
        
        // Check all subdirectories recursively
        foreach (var subDir in Directory.GetDirectories(directory))
        {
            if (ContainsDirectXDlls(subDir))
                return true;
        }
        
        return false;
    }

    private class GitLabProject
    {
        [JsonProperty("id")]
        public int Id { get; set; }
    }

    private class GitLabRelease
    {
        [JsonProperty("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonProperty("released_at")]
        public DateTime ReleasedAt { get; set; }

        [JsonProperty("assets")]
        public GitLabAssets? Assets { get; set; }
    }

    private class GitLabAssets
    {
        [JsonProperty("sources")]
        public List<GitLabSource>? Sources { get; set; }
    }

    private class GitLabSource
    {
        [JsonProperty("url")]
        public string Url { get; set; } = string.Empty;

        [JsonProperty("format")]
        public string Format { get; set; } = string.Empty;
    }
}