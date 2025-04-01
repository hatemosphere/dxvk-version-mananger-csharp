using DxvkVersionManager.Models;
using DxvkVersionManager.Services.Interfaces;
using System.Text;
using System.Text.RegularExpressions;

namespace DxvkVersionManager.Services.Implementations;

public class DxvkManagerService : IDxvkManagerService
{
    private readonly ISteamService _steamService;
    private readonly string _userDataPath;
    private readonly LoggingService _logger;
    
    public DxvkManagerService(ISteamService steamService, string userDataPath)
    {
        ArgumentNullException.ThrowIfNull(steamService);
        ArgumentNullException.ThrowIfNull(userDataPath);
        _steamService = steamService;
        _userDataPath = userDataPath;
        _logger = LoggingService.Instance;
    }
    
    public async Task<OperationResult> ApplyDxvkToGameAsync(SteamGame game, string dxvkType, string version)
    {
        try
        {
            _logger.LogInformation($"Applying {dxvkType} version {version} to {game.Name}...");
            
            if (game.Metadata == null)
            {
                return OperationResult.Failed($"Game metadata not available for {game.Name}. Please select DirectX version and architecture first.");
            }
            
            // Verify DirectX and architecture settings
            if (string.IsNullOrEmpty(game.Metadata.Direct3dVersions) || game.Metadata.Direct3dVersions == "Unknown")
            {
                return OperationResult.Failed($"DirectX version not specified for {game.Name}. Please select a DirectX version in the game settings.");
            }
            
            if (!game.Metadata.Executable32bit && !game.Metadata.Executable64bit)
            {
                return OperationResult.Failed($"Executable architecture not specified for {game.Name}. Please select either 32-bit or 64-bit architecture in the game settings.");
            }
            
            // Get the game installation directory
            var gameDir = game.Metadata.InstallDir;
            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
            {
                return OperationResult.Failed($"Game installation directory not found: {gameDir}. The game may have been moved or uninstalled.");
            }
            
            // Get the architecture subfolder
            var archSubfolder = GetArchSubfolder(game.Metadata);
            _logger.LogInformation($"Using {archSubfolder} architecture for {game.Name}");
            
            // Determine required DLLs based on Direct3D version
            var requiredDlls = GetRequiredDlls(game.Metadata.Direct3dVersions);
            if (requiredDlls.Count == 0)
            {
                return OperationResult.Failed($"Could not determine required DLLs for {game.Name}. Please select a valid DirectX version.");
            }
            
            _logger.LogInformation($"Game uses {game.Metadata.Direct3dVersions}, requires DLLs: {string.Join(", ", requiredDlls)}");
            
            // Add version information to log for analysis
            if (Version.TryParse(version, out Version? parsedVersion) && parsedVersion >= new Version(2, 0))
            {
                _logger.LogInformation($"Using DXVK version {version} which is 2.0 or newer - may have consolidated DLLs");
            }
            
            // Check for existing DLLs
            var existingDlls = CheckExistingDlls(gameDir, requiredDlls);
            
            // Determine the DXVK source directory
            var dxvkTypeDir = dxvkType == "dxvk-gplasync" ? "dxvk-gplasync-cache" : "dxvk-cache";
            var sourceDxvkDir = Path.Combine(_userDataPath, dxvkTypeDir, version);
            
            if (!Directory.Exists(sourceDxvkDir))
            {
                return OperationResult.Failed($"DXVK version {version} not found in cache. Please download this version first.");
            }
            
            // Backup existing DLLs if any
            if (existingDlls.Count > 0)
            {
                try
                {
                    var backupSuccess = await BackupExistingDllsAsync(gameDir, existingDlls);
                    if (!backupSuccess)
                    {
                        return OperationResult.Failed($"Failed to backup existing DLLs for {game.Name}. Please check if you have write permissions to the game directory.");
                    }
                }
                catch (Exception ex)
                {
                    return OperationResult.Failed($"Error backing up DLLs: {ex.Message}");
                }
            }
            
            // Copy DXVK DLLs
            try
            {
                var (success, skippedD3D10) = await CopyDxvkDllsAsync(
                    sourceDxvkDir,
                    archSubfolder,
                    gameDir,
                    requiredDlls,
                    dxvkType,
                    version);
                    
                // Update DXVK status after successful installation
                await UpdateGameDxvkStatusAsync(game.AppId, new DxvkStatus 
                { 
                    Patched = success, 
                    DxvkType = dxvkType,
                    DxvkVersion = version,
                    Backuped = existingDlls.Count > 0,
                    DxvkTimestamp = DateTime.Now
                });
                
                var result = OperationResult.Successful($"Successfully applied {dxvkType} version {version} to {game.Name}");
                
                if (existingDlls.Count == 0)
                {
                    result.Warning = "No original DirectX DLLs were found and backed up. This is unusual but not necessarily a problem.";
                }
                
                if (skippedD3D10)
                {
                    result.Warning = "Some D3D10 DLLs were skipped because they were not found in the DXVK package. This is normal in newer DXVK versions.";
                }
                
                return result;
            }
            catch (DirectoryNotFoundException ex)
            {
                return OperationResult.Failed(ex.Message);
            }
            catch (FileNotFoundException ex)
            {
                return OperationResult.Failed(ex.Message);
            }
            catch (IOException ex)
            {
                return OperationResult.Failed(ex.Message);
            }
            catch (Exception ex)
            {
                return OperationResult.Failed($"Error applying DXVK: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error applying DXVK to {game.Name}");
            return OperationResult.Failed($"Unexpected error applying DXVK: {ex.Message}");
        }
    }
    
    public async Task<OperationResult> RemoveDxvkFromGameAsync(SteamGame game)
    {
        try
        {
            _logger.LogInformation($"Removing DXVK DLL files for {game.Name}...");
            
            if (game.Metadata == null)
            {
                return OperationResult.Failed($"Game metadata not available for {game.Name}");
            }
            
            // Get the game installation directory
            var gameDir = game.Metadata.InstallDir;
            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
            {
                return OperationResult.Failed($"Game installation directory not found: {gameDir}");
            }
            
            // Determine required DLLs based on Direct3D version
            var requiredDlls = GetRequiredDlls(game.Metadata.Direct3dVersions);
            _logger.LogInformation($"Game uses {game.Metadata.Direct3dVersions}, requires DLLs: {string.Join(", ", requiredDlls)}");
            
            // Check if there are backup files
            bool hasBackups = HasBackupFiles(gameDir);
            
            if (hasBackups)
            {
                _logger.LogInformation($"Found backup files for {game.Name}, restoring original DLLs");
                
                // Track success for each DLL
                var restoredDlls = new List<string>();
                var failedDlls = new List<string>();
                
                // For each required DLL, restore from backup if exists
                foreach (var dll in requiredDlls)
                {
                    var backupPath = Path.Combine(gameDir, $"{dll}.bkp");
                    var targetPath = Path.Combine(gameDir, dll);
                    
                    if (File.Exists(backupPath))
                    {
                        try
                        {
                            // If target exists, delete it first
                            if (File.Exists(targetPath))
                            {
                                File.Delete(targetPath);
                            }
                            
                            // Restore from backup
                            File.Copy(backupPath, targetPath);
                            
                            // Delete the backup file after successful restoration
                            File.Delete(backupPath);
                            
                            restoredDlls.Add(dll);
                            _logger.LogInformation($"Restored {dll} for {game.Name}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Failed to restore {dll} for {game.Name}");
                            failedDlls.Add(dll);
                        }
                    }
                    else if (File.Exists(targetPath))
                    {
                        // If there's no backup but the DLL exists, it might be a DXVK DLL
                        // Check if it's likely a DXVK DLL and delete it if so
                        try
                        {
                            bool isDxvk = await IsDxvkDllAsync(targetPath);
                            if (isDxvk)
                            {
                                File.Delete(targetPath);
                                _logger.LogInformation($"Deleted DXVK {dll} from {game.Name} (no backup found)");
                                restoredDlls.Add(dll);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Failed to check/delete {dll} for {game.Name}");
                            failedDlls.Add(dll);
                        }
                    }
                }
                
                // Update DXVK status
                await UpdateGameDxvkStatusAsync(game.AppId, new DxvkStatus
                {
                    Patched = false,
                    Backuped = false
                });
                
                if (restoredDlls.Count > 0)
                {
                    var message = $"Successfully reverted {restoredDlls.Count} DLL(s) for {game.Name}";
                    if (failedDlls.Count > 0)
                    {
                        message += $". Failed to revert {failedDlls.Count} DLL(s): {string.Join(", ", failedDlls)}";
                    }
                    return OperationResult.Successful(message);
                }
                else
                {
                    return OperationResult.Failed($"Failed to revert any DLLs for {game.Name}");
                }
            }
            else
            {
                _logger.LogInformation($"No backup files found for {game.Name}, removing DXVK DLLs if present");
                
                // Track deleted DLLs
                var deletedDlls = new List<string>();
                
                // Cache the patched status for use in logic
                bool isKnownToBePatched = (game.DxvkStatus?.Patched == true);
                
                // For each required DLL, check if it exists and if it's likely a DXVK DLL
                foreach (var dll in requiredDlls)
                {
                    var dllPath = Path.Combine(gameDir, dll);
                    if (File.Exists(dllPath))
                    {
                        try
                        {
                            // If game is known to be patched, force DLL removal
                            bool shouldRemove = false;
                            
                            if (isKnownToBePatched)
                            {
                                shouldRemove = true;
                                _logger.LogInformation($"Forcing deletion of {dll} since game is known to have DXVK active");
                            }
                            else
                            {
                                // Otherwise check if it's a DXVK DLL
                                shouldRemove = await IsDxvkDllAsync(dllPath);
                            }
                            
                            if (shouldRemove)
                            {
                                File.Delete(dllPath);
                                deletedDlls.Add(dll);
                                _logger.LogInformation($"Deleted DXVK {dll} from {game.Name}");
                            }
                            else
                            {
                                _logger.LogInformation($"Found {dll} but it doesn't appear to be a DXVK DLL, skipping");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Failed to check/delete {dll} from {game.Name}");
                        }
                    }
                }
                
                // Update DXVK status
                await UpdateGameDxvkStatusAsync(game.AppId, new DxvkStatus
                {
                    Patched = false,
                    Backuped = false
                });
                
                if (deletedDlls.Count > 0)
                {
                    return OperationResult.Successful($"Successfully removed {deletedDlls.Count} DXVK DLL(s) from {game.Name}");
                }
                else
                {
                    // Check if the game's status shows it's patched - if so, we should update the status
                    // even if we didn't find files to delete
                    if (game.DxvkStatus?.Patched == true)
                    {
                        _logger.LogWarning($"Game status shows DXVK is active, but no DXVK DLLs were found. Status has been updated.");
                        return OperationResult.Successful($"Update complete - DXVK has been removed from {game.Name}");
                    }
                    else
                    {
                        return OperationResult.Successful($"No DXVK DLLs found to remove from {game.Name}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error reverting DXVK changes for {game.Name}");
            return OperationResult.Failed($"Error reverting DXVK changes: {ex.Message}");
        }
    }
    
    private bool HasBackupFiles(string gameDir)
    {
        try
        {
            var files = Directory.GetFiles(gameDir);
            return files.Any(f => f.EndsWith(".bkp"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error checking backup files in {gameDir}");
            return false;
        }
    }
    
    public Task<bool> CheckBackupExistsAsync(string gameDir)
    {
        return Task.FromResult(HasBackupFiles(gameDir));
    }
    
    public string GetArchSubfolder(GameMetadata metadata)
    {
        if (metadata.Executable64bit)
        {
            return "x64";
        }
        else if (metadata.Executable32bit)
        {
            return "x32";
        }
        else
        {
            // Default to x64 if no architecture is specified
            return "x64";
        }
    }
    
    public List<string> GetRequiredDlls(string directXVersion)
    {
        if (string.IsNullOrEmpty(directXVersion) || directXVersion == "Unknown")
        {
            // Default case if no DirectX version is provided
            return new List<string> { "d3d9.dll", "dxgi.dll", "d3d11.dll" };
        }
        
        // Create a normalized lowercase version for comparison
        var normalizedVersion = directXVersion.ToLowerInvariant();
        
        // Extract the primary version number (e.g., 9.0c -> 9, 11.4 -> 11)
        // Match any number at the start of a "Direct3D X" or "DX" or "D3D X" pattern
        int? primaryVersion = null;
        
        // Try to extract version from various formats
        var versionPatterns = new[]
        {
            new Regex(@"direct3d\s*(\d+)(?:\.\d+)?", RegexOptions.IgnoreCase),
            new Regex(@"d3d\s*(\d+)(?:\.\d+)?", RegexOptions.IgnoreCase),
            new Regex(@"dx\s*(\d+)(?:\.\d+)?", RegexOptions.IgnoreCase),
            new Regex(@"(\d+)(?:\.\d+)?")
        };
        
        foreach (var pattern in versionPatterns)
        {
            var match = pattern.Match(normalizedVersion);
            if (match.Success && match.Groups.Count > 1)
            {
                primaryVersion = int.Parse(match.Groups[1].Value);
                break;
            }
        }
        
        _logger.LogDebug($"Normalized DirectX version: \"{normalizedVersion}\" -> Primary version: {primaryVersion}");
        
        // Check for each possible Direct3D version based on the primary version number
        if (primaryVersion == 8 || normalizedVersion.Contains("direct3d 8") || normalizedVersion.Contains("d3d8"))
        {
            return new List<string> { "d3d8.dll" };
        }
        else if (primaryVersion == 9 || normalizedVersion.Contains("direct3d 9") || normalizedVersion.Contains("d3d9"))
        {
            return new List<string> { "d3d9.dll", "dxgi.dll" };
        }
        else if (primaryVersion == 10 || normalizedVersion.Contains("direct3d 10") || normalizedVersion.Contains("d3d10"))
        {
            return new List<string> { "d3d10.dll", "d3d10_1.dll", "dxgi.dll" };
        }
        else if (primaryVersion == 11 || normalizedVersion.Contains("direct3d 11") || normalizedVersion.Contains("d3d11"))
        {
            return new List<string> { "d3d11.dll", "d3d10.dll", "d3d10_1.dll", "dxgi.dll" };
        }
        else if (primaryVersion == 12 || normalizedVersion.Contains("direct3d 12") || normalizedVersion.Contains("d3d12"))
        {
            return new List<string> { "d3d12.dll", "d3d11.dll", "d3d10.dll", "d3d10_1.dll", "dxgi.dll" };
        }
        else
        {
            // Default case if no specific version is matched
            return new List<string> { "d3d9.dll", "dxgi.dll", "d3d11.dll" };
        }
    }
    
    private List<string> CheckExistingDlls(string gameDir, List<string> requiredDlls)
    {
        var existingDlls = new List<string>();
        
        foreach (var dll in requiredDlls)
        {
            var dllPath = Path.Combine(gameDir, dll);
            if (File.Exists(dllPath))
            {
                existingDlls.Add(dll);
            }
        }
        
        return existingDlls;
    }
    
    private Task<bool> BackupExistingDllsAsync(string gameDir, List<string> existingDlls)
    {
        try
        {
            foreach (var dll in existingDlls)
            {
                var sourcePath = Path.Combine(gameDir, dll);
                var backupPath = $"{sourcePath}.bkp";
                
                // Check if backup already exists
                if (File.Exists(backupPath))
                {
                    _logger.LogInformation($"Backup already exists for {dll}, skipping");
                    continue;
                }
                
                // Create a backup copy
                File.Copy(sourcePath, backupPath);
                _logger.LogInformation($"Backed up {dll} to {backupPath}");
            }
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error backing up DLLs in {gameDir}");
            return Task.FromResult(false);
        }
    }
    
    private async Task<(bool Success, bool SkippedD3D10)> CopyDxvkDllsAsync(
        string sourceDxvkDir,
        string archSubfolder,
        string gameDir,
        List<string> requiredDlls,
        string dxvkType,
        string version)
    {
        try
        {
            // Add Task.Yield to make this truly async
            await Task.Yield();
            
            _logger.LogDebug($"Starting DXVK copy operation for {dxvkType} {version}");
            _logger.LogDebug($"Source: {sourceDxvkDir}/{archSubfolder}, Target: {gameDir}");
            
            // Flag to track if we're intentionally skipping D3D10 DLLs
            bool skippingD3D10Dlls = false;
            
            // Check source directory exists
            var sourceArchDir = Path.Combine(sourceDxvkDir, archSubfolder ?? string.Empty);
            var versionNameFolder = $"dxvk-{version}";
            var alternativeSourceArchDir = Path.Combine(sourceDxvkDir, versionNameFolder, archSubfolder ?? string.Empty);
            
            _logger.LogDebug($"Checking for architecture directory at: {sourceArchDir}");
            _logger.LogDebug($"Checking for alternative architecture directory at: {alternativeSourceArchDir}");
            
            // Check first in the direct path, then in the nested path
            if (!Directory.Exists(sourceArchDir))
            {
                _logger.LogDebug($"Directory not found at {sourceArchDir}, trying alternative path");
                
                if (Directory.Exists(alternativeSourceArchDir))
                {
                    _logger.LogDebug($"Found architecture directory at alternative path: {alternativeSourceArchDir}");
                    sourceArchDir = alternativeSourceArchDir;
                }
                else
                {
                    // Try to find any subdirectory that might contain the architecture folders
                    var possibleSubdirs = Directory.GetDirectories(sourceDxvkDir);
                    foreach (var subdir in possibleSubdirs)
                    {
                        var archPath = Path.Combine(subdir, archSubfolder ?? string.Empty);
                        if (Directory.Exists(archPath))
                        {
                            _logger.LogDebug($"Found architecture directory in subdirectory: {archPath}");
                            sourceArchDir = archPath;
                            break;
                        }
                    }
                    
                    // If we still haven't found it, throw an error
                    if (!Directory.Exists(sourceArchDir))
                    {
                        _logger.LogError($"Architecture directory not found: {sourceArchDir}");
                        _logger.LogError($"Also checked alternative path: {alternativeSourceArchDir}");
                        _logger.LogError($"Directory structure at {sourceDxvkDir}: {string.Join(", ", Directory.GetDirectories(sourceDxvkDir))}");
                        
                        throw new DirectoryNotFoundException($"DXVK directory for {archSubfolder} architecture not found. Make sure you have downloaded this DXVK version.");
                    }
                }
            }
            
            // Check which DLLs are available in the source directory
            var availableDlls = Directory.GetFiles(sourceArchDir, "*.dll")
                .Select(Path.GetFileName)
                .ToList();
                
            // Check which required DLLs are missing from source
            var missingSourceDlls = requiredDlls
                .Where(dll => !availableDlls.Contains(dll))
                .ToList();
                
            if (missingSourceDlls.Count > 0)
            {
                _logger.LogWarning($"Missing DXVK DLLs in source: {string.Join(", ", missingSourceDlls)}");
                
                // Special handling for D3D10 related DLLs which may not exist in newer DXVK versions
                bool onlyD3D10Missing = missingSourceDlls.All(dll => 
                    dll == "d3d10.dll" || dll == "d3d10_1.dll");
                    
                // If we're missing D3D10 DLLs but have D3D11 (which handles D3D10 in newer DXVK),
                // we can continue with just the available DLLs
                if (onlyD3D10Missing && availableDlls.Contains("d3d11.dll"))
                {
                    _logger.LogInformation("Missing D3D10 DLLs but D3D11 present - this is normal in newer DXVK versions");
                    
                    // Filter required DLLs to only those that actually exist
                    requiredDlls = requiredDlls
                        .Where(dll => availableDlls.Contains(dll))
                        .ToList();
                        
                    _logger.LogInformation($"Proceeding with available DLLs: {string.Join(", ", requiredDlls)}");
                    
                    // Set a flag that we're skipping D3D10 DLLs intentionally
                    skippingD3D10Dlls = true;
                }
                else
                {
                    throw new FileNotFoundException($"Required DLLs not found in DXVK package: {string.Join(", ", missingSourceDlls)}. Try downloading DXVK again.");
                }
            }
            
            // Copy each required DLL
            foreach (var dll in requiredDlls)
            {
                var sourcePath = Path.Combine(sourceArchDir, dll);
                var targetPath = Path.Combine(gameDir, dll);
                
                // If target exists, try to delete it
                if (File.Exists(targetPath))
                {
                    try
                    {
                        using (var fs = new FileStream(targetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        {
                            // Just testing if file is locked - if we can open it for writing, it's not locked
                        }
                        
                        // If we get here, the file isn't locked, so delete it
                        File.Delete(targetPath);
                        _logger.LogDebug($"Deleted existing {dll}");
                    }
                    catch (IOException)
                    {
                        _logger.LogError($"Cannot replace {dll} because it's in use. Please close the game or any related processes and try again.");
                        throw new IOException($"The file {dll} is locked and cannot be replaced. Please close the game and any related launchers before trying again.");
                    }
                }
                
                // Copy the DLL
                File.Copy(sourcePath, targetPath);
                _logger.LogInformation($"Copied {dll} to {gameDir}");
            }
            
            return (true, skippingD3D10Dlls);
        }
        catch (Exception ex) when (!(ex is DirectoryNotFoundException || ex is FileNotFoundException || ex is IOException))
        {
            // Only log general exception, but let specific ones propagate with their custom messages
            _logger.LogError(ex, $"Error copying DXVK DLLs");
            throw;
        }
    }

    // Helper method to check for running processes that might be from the game
    private (bool HasRunningProcess, string ProcessName) CheckForRunningGameProcess(string gameDir)
    {
        try
        {
            // Get all running processes
            var processes = System.Diagnostics.Process.GetProcesses();
            
            // Get all executable files in the game directory
            var gameExes = Directory.GetFiles(gameDir, "*.exe", SearchOption.AllDirectories)
                .Select(path => Path.GetFileNameWithoutExtension(path).ToLowerInvariant())
                .ToList();
                
            // Check if any running process matches game executables
            foreach (var process in processes)
            {
                try
                {
                    var processName = process.ProcessName.ToLowerInvariant();
                    if (gameExes.Contains(processName))
                    {
                        _logger.LogWarning($"Found a potentially running game process: {process.ProcessName}");
                        return (true, process.ProcessName);
                    }
                }
                catch
                {
                    // Skip processes we can't access
                    continue;
                }
            }
            
            return (false, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for running game processes");
            return (false, string.Empty);
        }
    }

    // Add a diagnostic tool that users can run to check their DXVK environment
    public async Task<OperationResult> DiagnoseAndLogDxvkEnvironmentAsync(SteamGame game, string dxvkType = "dxvk")
    {
        try
        {
            _logger.LogInformation("====== Starting DXVK Environment Diagnostics ======");
            _logger.LogInformation($"Game: {game.Name} (AppID: {game.AppId})");
            
            var results = new List<string>();
            results.Add($"DXVK Environment Diagnostics for {game.Name} (AppID: {game.AppId})");
            results.Add($"Timestamp: {DateTime.Now}");
            results.Add("----------------------------------------");
            
            // Check game metadata
            if (game.Metadata == null)
            {
                _logger.LogError("Game metadata is not available");
                results.Add("ERROR: Game metadata is not available");
                return OperationResult.Failed("Game metadata not available");
            }
            
            // Game info
            var installDir = game.Metadata.InstallDir;
            results.Add($"Installation directory: {installDir}");
            
            if (string.IsNullOrEmpty(installDir) || !Directory.Exists(installDir))
            {
                _logger.LogError($"Game installation directory not found: {installDir}");
                results.Add("ERROR: Game installation directory not found");
                return OperationResult.Failed("Game installation directory not found");
            }
            
            // Configuration info
            var archStr = game.Metadata.Executable64bit ? "64-bit" : "32-bit";
            var dxVersions = game.Metadata.Direct3dVersions;
            results.Add($"Architecture: {archStr}");
            results.Add($"Direct3D Versions: {dxVersions}");
            
            // Add detection method information
            if (game.Metadata.D3dVersionAutoDetected)
            {
                results.Add($"DirectX Detection: Auto-detected from executable imports");
                if (!string.IsNullOrEmpty(game.Metadata.DetectionMethod))
                {
                    results.Add($"Detection Method: {game.Metadata.DetectionMethod}");
                }
                
                // Add detailed detection info if available
                if (!string.IsNullOrEmpty(game.Metadata.DetailedDetectionInfo))
                {
                    results.Add("Detailed DirectX Detection Results:");
                    foreach (var line in game.Metadata.DetailedDetectionInfo.Split(Environment.NewLine))
                    {
                        results.Add($"  {line}");
                    }
                }
            }
            else
            {
                results.Add($"DirectX Detection: Manually selected by user");
            }
            
            if (game.Metadata.ArchitectureAutoDetected)
            {
                results.Add($"Architecture Detection: Auto-detected from executable PE header");
            }
            else
            {
                results.Add($"Architecture Detection: Manually selected by user");
            }
            
            results.Add("----------------------------------------");
            
            // Calculate arch subfolder
            var archSubfolder = GetArchSubfolder(game.Metadata);
            results.Add($"DXVK Architecture Subfolder: {archSubfolder}");
            
            // Get required DLLs
            var requiredDlls = GetRequiredDlls(dxVersions);
            results.Add($"Required DLLs: {string.Join(", ", requiredDlls)}");
            results.Add("----------------------------------------");
            
            // DXVK cache info
            var dxvkTypeDir = dxvkType == "dxvk-gplasync" ? "dxvk-gplasync-cache" : "dxvk-cache";
            var dxvkCacheDir = Path.Combine(_userDataPath, dxvkTypeDir);
            
            results.Add($"DXVK Cache Directory: {dxvkCacheDir}");
            
            if (!Directory.Exists(dxvkCacheDir))
            {
                _logger.LogWarning($"DXVK cache directory not found: {dxvkCacheDir}");
                results.Add("WARNING: DXVK cache directory not found");
            }
            else
            {
                // List available versions
                var versions = Directory.GetDirectories(dxvkCacheDir)
                    .Select(Path.GetFileName)
                    .Where(d => !string.IsNullOrEmpty(d))
                    .ToList();
                
                results.Add($"Available {dxvkType} versions: {string.Join(", ", versions)}");
                
                // For each version, check if it has the required DLLs for this architecture
                foreach (var version in versions)
                {
                    // Check multiple possible locations for architecture directories
                    results.Add($"Examining version: {version}");
                    
                    // First, check direct path (version/arch)
                    var versionDir = Path.Combine(dxvkCacheDir, version ?? string.Empty);
                    var standardVersionArchDir = Path.Combine(versionDir, archSubfolder ?? string.Empty);
                    var hasStandardStructure = Directory.Exists(standardVersionArchDir);
                    results.Add($"  Standard structure ({version}/{archSubfolder}): {(hasStandardStructure ? "exists" : "missing")}");
                    
                    // Next, check for subfolders that might contain arch directories
                    var subDirs = Directory.GetDirectories(versionDir);
                    results.Add($"  Subfolders under {version}: {(subDirs.Length > 0 ? string.Join(", ", subDirs.Select(Path.GetFileName)) : "none")}");
                    
                    // Check specifically for dxvk-<version> subfolder
                    var versionNameFolder = $"dxvk-{version}";
                    var nestedVersionArchDir = Path.Combine(versionDir, versionNameFolder, archSubfolder ?? string.Empty);
                    var hasNestedStructure = Directory.Exists(nestedVersionArchDir);
                    results.Add($"  Nested structure ({version}/{versionNameFolder}/{archSubfolder}): {(hasNestedStructure ? "exists" : "missing")}");
                    
                    // Check DLLs in the correct location (if any)
                    string? effectiveArchDir = null;
                    if (hasStandardStructure)
                    {
                        effectiveArchDir = standardVersionArchDir;
                    }
                    else if (hasNestedStructure)
                    {
                        effectiveArchDir = nestedVersionArchDir;
                    }
                    else
                    {
                        // Look for arch folder in any subfolder
                        foreach (var subDir in subDirs)
                        {
                            if (subDir != null)
                            {
                                var possibleArchDir = Path.Combine(subDir, archSubfolder ?? string.Empty);
                                if (Directory.Exists(possibleArchDir))
                                {
                                    effectiveArchDir = possibleArchDir;
                                    results.Add($"  Found architecture folder in alternate location: {Path.GetFileName(subDir ?? string.Empty)}/{archSubfolder ?? string.Empty}");
                                    break;
                                }
                            }
                        }
                    }
                    
                    if (effectiveArchDir != null)
                    {
                        var availableDlls = Directory.GetFiles(effectiveArchDir, "*.dll")
                            .Select(Path.GetFileName)
                            .ToList();
                            
                        results.Add($"  Available DLLs: {string.Join(", ", availableDlls)}");
                        
                        var missingDlls = requiredDlls
                            .Where(dll => !availableDlls.Contains(dll))
                            .ToList();
                            
                        if (missingDlls.Count > 0)
                        {
                            results.Add($"  Missing DLLs: {string.Join(", ", missingDlls)}");
                        }
                        else
                        {
                            results.Add($"  All required DLLs available");
                        }
                    }
                    else
                    {
                        results.Add($"  ERROR: Could not find architecture directory for {archSubfolder}");
                    }
                }
            }
            
            results.Add("----------------------------------------");
            
            // Game installation diagnostics
            results.Add("Game Installation Details:");
            
            // Check for executables in game directory
            var exeFiles = Directory.GetFiles(installDir, "*.exe", SearchOption.AllDirectories);
            results.Add($"Total executables: {exeFiles.Length}");
            
            if (exeFiles.Length > 0)
            {
                results.Add("Executable files:");
                foreach (var exe in exeFiles.Take(10)) // Limit to 10 to avoid log spam
                {
                    var relPath = exe.Substring(installDir.Length).TrimStart('\\', '/');
                    results.Add($"  {relPath}");
                }
                
                if (exeFiles.Length > 10)
                {
                    results.Add($"  ... and {exeFiles.Length - 10} more");
                }
            }
            
            // Check for existing d3d/dxgi DLLs in game directory
            results.Add("DirectX related DLLs in game directory:");
            var directXDlls = Directory.GetFiles(installDir, "*.dll", SearchOption.TopDirectoryOnly)
                .Where(f => Path.GetFileName(f).StartsWith("d3d") || Path.GetFileName(f).StartsWith("dxgi"))
                .ToList();
                
            if (directXDlls.Count > 0)
            {
                foreach (var dll in directXDlls)
                {
                    var fileName = Path.GetFileName(dll);
                    var fileInfo = new FileInfo(dll);
                    var fileVersion = GetFileVersion(dll);
                    var isDxvk = await IsDxvkDllAsync(dll);
                    
                    results.Add($"  {fileName}: Size={fileInfo.Length}, Modified={fileInfo.LastWriteTime}, Version={fileVersion}, DXVK={isDxvk}");
                }
            }
            else
            {
                results.Add("  No DirectX related DLLs found in game directory");
            }
            
            // Check for backup files
            var backupFiles = Directory.GetFiles(installDir, "*.bkp", SearchOption.TopDirectoryOnly);
            results.Add($"Backup files: {backupFiles.Length}");
            
            if (backupFiles.Length > 0)
            {
                results.Add("Backup files:");
                foreach (var bkp in backupFiles)
                {
                    var fileName = Path.GetFileName(bkp);
                    var fileInfo = new FileInfo(bkp);
                    results.Add($"  {fileName}: Size={fileInfo.Length}, Modified={fileInfo.LastWriteTime}");
                }
            }
            
            // Check for running processes
            var gameProcessCheck = CheckForRunningGameProcess(installDir);
            if (gameProcessCheck.HasRunningProcess)
            {
                results.Add($"WARNING: Game process detected: {gameProcessCheck.ProcessName}");
            }
            else
            {
                results.Add("No running game processes detected");
            }
            
            // Write access test
            try
            {
                var testFilePath = Path.Combine(installDir, $"dxvk_write_test_{Guid.NewGuid()}.tmp");
                File.WriteAllText(testFilePath, "DXVK Manager write test");
                File.Delete(testFilePath);
                results.Add("Write permissions: OK");
            }
            catch (Exception ex)
            {
                results.Add($"Write permissions: FAILED - {ex.Message}");
            }
            
            results.Add("----------------------------------------");
            results.Add("Diagnostics completed");
            
            // Return diagnostic results
            var resultStr = string.Join(Environment.NewLine, results);
            _logger.LogInformation("====== DXVK Environment Diagnostics Completed ======");
            
            return OperationResult.Successful("Diagnostics completed successfully", resultStr);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during DXVK environment diagnostics");
            return OperationResult.Failed($"Error during diagnostics: {ex.Message}");
        }
    }
    
    // Helper method to check if a DLL is a DXVK DLL
    private async Task<bool> IsDxvkDllAsync(string dllPath)
    {
        try
        {
            // Read first few KB of the file to check for DXVK signatures
            using (var fileStream = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (fileStream.Length < 100)
                    return false;
                
                var buffer = new byte[4096]; // Read first 4KB
                var bytesToRead = Math.Min(buffer.Length, (int)fileStream.Length);
                await fileStream.ReadAsync(buffer.AsMemory(0, bytesToRead));
                
                // Convert to string for easier searching
                var content = System.Text.Encoding.ASCII.GetString(buffer);
                
                // Check for common DXVK strings - expanded to catch more variants
                return content.Contains("DXVK") || 
                       content.Contains("VK_LAYER") ||
                       content.Contains("Vulkan") ||
                       content.Contains("doitsujin") ||
                       content.Contains("vkCreateInstance") ||
                       content.Contains("vkGetDeviceProcAddr") ||
                       content.Contains("VkResult") ||
                       content.Contains("D3D9 to Vulkan") ||
                       content.Contains("d3d9-on-12") ||
                       content.Contains("D3D11 to Vulkan");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error checking if DLL is DXVK: {dllPath}");
            return false;
        }
    }
    
    // Helper method to get file version information
    private string GetFileVersion(string filePath)
    {
        try
        {
            var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(filePath);
            if (versionInfo.FileVersion != null)
                return versionInfo.FileVersion;
            return "Unknown";
        }
        catch
        {
            return "Error reading version";
        }
    }

    private async Task<bool> UpdateGameDxvkStatusAsync(string appId, DxvkStatus status)
    {
        try
        {
            _logger.LogInformation($"Updating DXVK status for game with AppID {appId}");
            
            // Create the dxvk-status directory if it doesn't exist
            var dxvkStatusDir = Path.Combine(_userDataPath, "dxvk-status");
            if (!Directory.Exists(dxvkStatusDir))
            {
                Directory.CreateDirectory(dxvkStatusDir);
            }
            
            // Save status to JSON file
            var statusFilePath = Path.Combine(dxvkStatusDir, $"{appId}.json");
            var json = System.Text.Json.JsonSerializer.Serialize(status, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            await File.WriteAllTextAsync(statusFilePath, json);
            
            // Update Steam service status too
            await _steamService.UpdateGameDxvkStatusAsync(appId, status);
            
            _logger.LogInformation($"DXVK status updated for game with AppID {appId}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error updating DXVK status for game with AppID {appId}");
            return false;
        }
    }

    public async Task<OperationResult> RevertDxvkChangesAsync(SteamGame game)
    {
        // Replace with call to the fixed method
        return await RemoveDxvkFromGameAsync(game);
    }
}