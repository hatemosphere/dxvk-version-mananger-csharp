using DxvkVersionManager.Models;
using DxvkVersionManager.Services.Interfaces;
using System.Text;
using System.Text.RegularExpressions;

namespace DxvkVersionManager.Services.Implementations;

public class DxvkManagerService : IDxvkManagerService
{
    private readonly ISteamService _steamService;
    private readonly IDxvkVersionService _dxvkVersionService;
    private readonly string _dataPath;
    private readonly LoggingService _logger;
    private const string _dxvkStatusFile = "dxvk-status.json";
    
    public DxvkManagerService(ISteamService steamService, IDxvkVersionService dxvkVersionService, string dataPath)
    {
        ArgumentNullException.ThrowIfNull(steamService);
        ArgumentNullException.ThrowIfNull(dxvkVersionService);
        ArgumentNullException.ThrowIfNull(dataPath);
        _steamService = steamService;
        _dxvkVersionService = dxvkVersionService;
        _dataPath = dataPath;
        _logger = LoggingService.Instance;
        _logger.LogInformation($"DxvkManagerService initialized with dataPath: {_dataPath}");
    }
    
    public async Task<OperationResult> ApplyDxvkToGameAsync(SteamGame game, string dxvkType, string version)
    {
        try
        {
            _logger.LogInformation($"Starting DXVK application process for {game.Name}: Version={version}, Type={dxvkType}");
            
            if (game.Metadata == null)
            {
                return OperationResult.Failed($"Game metadata not available for {game.Name}. Cannot apply DXVK.");
            }

            // 1. Check current status and remove if already patched
            var currentStatus = await _steamService.GetGameDxvkStatusAsync(game.AppId);
            if (currentStatus != null && currentStatus.Patched)
            {
                _logger.LogInformation($"Game {game.Name} is already patched with DXVK ({currentStatus.DxvkType} {currentStatus.DxvkVersion}). Performing removal first.");
                var removalResult = await RemoveDxvkFromGameAsync(game);
                if (!removalResult.Success)
                {
                    return OperationResult.Failed($"Failed to remove existing DXVK installation before applying new version. Error: {removalResult.Message}");
                }
                _logger.LogInformation("Existing DXVK installation removed successfully.");
            }
            else
            {
                 _logger.LogInformation($"Game {game.Name} is not currently patched or status is unknown. Proceeding with new installation.");
            }

            // 2. Proceed with the standard application process (Backup original DLLs if present, Copy new DXVK DLLs)
            
            // Verify architecture settings
            if (!game.Metadata.Executable32bit && !game.Metadata.Executable64bit)
            {
                return OperationResult.Failed($"Executable architecture not specified for {game.Name}. Please select either 32-bit or 64-bit architecture in the game settings.");
            }
            
            // Get the target directory (directory of the primary executable)
            string? targetDir = null;
            if (!string.IsNullOrEmpty(game.Metadata.TargetExecutablePath))
            {
                targetDir = Path.GetDirectoryName(game.Metadata.TargetExecutablePath);
            }
            
            // Fallback to install directory if target executable path is not set
            if (string.IsNullOrEmpty(targetDir))
            {
                targetDir = game.Metadata.InstallDir;
                _logger.LogWarning($"Target executable path not found for {game.Name}. Falling back to game installation directory: {targetDir}");
            }
            
            if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
            {
                return OperationResult.Failed($"Target directory not found or invalid: {targetDir}. The game may have been moved or uninstalled.");
            }
            
            _logger.LogInformation($"Target directory for DXVK DLLs: {targetDir}");
            
            // Get the architecture subfolder
            var archSubfolder = GetArchSubfolder(game.Metadata);
            _logger.LogInformation($"Using {archSubfolder} architecture for {game.Name}");
            
            // Use ALL possible DirectX DLLs
            var allDxDlls = new List<string> {
                "d3d8.dll", "d3d9.dll", "d3d10.dll", "d3d10core.dll", "d3d11.dll", "d3d12.dll", "dxgi.dll"
            };
            
            _logger.LogInformation($"Will copy all available DirectX DLLs for {game.Name}");
            
            // Add version information to log for analysis
            if (Version.TryParse(version, out Version? parsedVersion) && parsedVersion >= new Version(2, 0))
            {
                _logger.LogInformation($"Using DXVK version {version} which is 2.0 or newer - may have consolidated DLLs");
            }
            
            // Check for existing DLLs
            var existingDlls = CheckExistingDlls(targetDir, allDxDlls);
            
            // Determine the DXVK source directory
            var dxvkTypeDir = dxvkType == "dxvk-gplasync" ? "dxvk-gplasync-cache" : "dxvk-cache";
            var sourceDxvkDir = Path.Combine(_dataPath, dxvkTypeDir, version);
            
            if (!Directory.Exists(sourceDxvkDir))
            {
                return OperationResult.Failed($"DXVK version {version} not found in cache. Please download this version first.");
            }
            
            // Backup existing DLLs if necessary
            if (existingDlls.Count > 0)
            {
                try
                {
                    var backupSuccess = BackupExistingDlls(targetDir, existingDlls);
                    if (!backupSuccess)
                    {
                        // Decide how to handle backup failure - maybe log and continue?
                        _logger.LogWarning($"Failed to back up existing DLLs for {game.Name}. Proceeding with installation...");
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
                var (success, skippedDlls) = await CopyAllDxvkDllsAsync(
                    sourceDxvkDir,
                    archSubfolder,
                    targetDir,
                    allDxDlls,
                    dxvkType,
                    version);
                
                if (!success)
                {
                    return OperationResult.Failed($"Failed to copy DXVK DLLs to {targetDir}");
                }
                
                // Update DXVK status after successful installation
                await UpdateGameDxvkStatusAsync(game.AppId, new DxvkStatus 
                { 
                    Patched = true, 
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
                
                if (skippedDlls.Count > 0)
                {
                    result.Warning = $"Some DLLs were skipped because they were not found in the DXVK package: {string.Join(", ", skippedDlls)}. This is normal in some DXVK versions.";
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
            
            // Get the target directory (directory of the primary executable)
            string? targetDir = null;
            if (!string.IsNullOrEmpty(game.Metadata.TargetExecutablePath))
            {
                targetDir = Path.GetDirectoryName(game.Metadata.TargetExecutablePath);
            }
            
            // Fallback to install directory if target executable path is not set
            if (string.IsNullOrEmpty(targetDir))
            {
                targetDir = game.Metadata.InstallDir;
                _logger.LogWarning($"Target executable path not found for {game.Name}. Falling back to game installation directory: {targetDir}");
            }
            
            if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
            {
                return OperationResult.Failed($"Target directory not found or invalid: {targetDir}");
            }
            
            _logger.LogInformation($"Target directory for DXVK removal: {targetDir}");
            
            // Use ALL possible DirectX DLLs for restoration/removal checks
            var allDxDlls = new List<string> {
                "d3d8.dll", "d3d9.dll", "d3d10.dll", "d3d10core.dll", "d3d11.dll", "d3d12.dll", "dxgi.dll"
            };
            
            _logger.LogInformation($"Will restore/remove all DirectX DLLs for {game.Name}");
            
            // Check if there are backup files
            bool hasBackups = HasBackupFiles(targetDir);
            
            if (hasBackups)
            {
                _logger.LogInformation($"Found backup files for {game.Name}, restoring original DLLs");
                
                // Track success for each DLL
                var restoredDlls = new List<string>();
                var failedDlls = new List<string>();
                
                // For each required DLL, restore from backup if exists
                foreach (var dll in allDxDlls)
                {
                    var backupPath = Path.Combine(targetDir, $"{dll}.bkp");
                    var targetPath = Path.Combine(targetDir, dll);
                    
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
                
                // For each DLL, check if it exists and if it's likely a DXVK DLL
                foreach (var dll in allDxDlls)
                {
                    var dllPath = Path.Combine(targetDir, dll);
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
    
    private bool HasBackupFiles(string targetDir)
    {
        // Use ALL possible DirectX DLLs for backup check
        var allDxDlls = new List<string> {
            "d3d8.dll", "d3d9.dll", "d3d10.dll", "d3d10core.dll", "d3d11.dll", "d3d12.dll", "dxgi.dll"
        };
        
        foreach (var dll in allDxDlls)
        {
            if (File.Exists(Path.Combine(targetDir, $"{dll}.bkp")))
            {
                return true;
            }
        }
        
        return false;
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
    
    private bool BackupExistingDlls(string targetDir, List<string> existingDlls)
    {
        var backupDir = Path.Combine(targetDir, "dxvk-backup");
        try
        {
            // Ensure backup directory exists
            Directory.CreateDirectory(backupDir);
            
            foreach (var dll in existingDlls)
            {
                var sourcePath = Path.Combine(targetDir, dll);
                var backupPath = Path.Combine(backupDir, dll);
                
                // Check if backup already exists (don't overwrite)
                if (!File.Exists(backupPath))
                {
                    File.Move(sourcePath, backupPath, true); // Use Move to ensure atomicity
                    _logger.LogInformation($"Backed up {dll} to {backupPath}");
                }
                else
                {
                    _logger.LogWarning($"Backup for {dll} already exists at {backupPath}. Removing current DLL without backup.");
                    // If backup exists, just delete the source file since we can't back it up again
                    File.Delete(sourcePath);
                    _logger.LogInformation($"Removed existing DLL: {sourcePath}");
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error backing up DLLs in {targetDir}");
            return false;
        }
    }
    
    private async Task<(bool Success, List<string> SkippedDlls)> CopyAllDxvkDllsAsync(
        string sourceDxvkDir,
        string archSubfolder,
        string targetGameDir,
        List<string> requiredDlls,
        string dxvkType,
        string version)
    {
        // Prepare a list to track skipped DLLs
        var skippedDlls = new List<string>();
        
        // Check if source directory exists
        var sourceDllDir = Path.Combine(sourceDxvkDir, archSubfolder);
        if (!Directory.Exists(sourceDllDir))
        {
            // Also check for nested directory structure
            var potentialNestedDirs = Directory.GetDirectories(sourceDxvkDir, "dxvk*");
            bool foundNestedDllDir = false;
            
            foreach (var nestedDir in potentialNestedDirs)
            {
                var nestedDllDir = Path.Combine(nestedDir, archSubfolder);
                if (Directory.Exists(nestedDllDir))
                {
                    sourceDllDir = nestedDllDir;
                    foundNestedDllDir = true;
                    _logger.LogInformation($"Found DLLs in nested directory structure: {nestedDllDir}");
                    break;
                }
            }
            
            // If we still don't have a valid directory, check the root of the DXVK directory
            if (!foundNestedDllDir)
            {
                if (Directory.GetFiles(sourceDxvkDir, "*.dll").Length > 0)
                {
                    sourceDllDir = sourceDxvkDir;
                    _logger.LogInformation($"Using DLLs directly from root directory: {sourceDllDir}");
                }
                else
                {
                    throw new DirectoryNotFoundException($"Cannot find DXVK {version} DLLs for {archSubfolder} architecture.");
                }
            }
        }
        
        // Copy each required DLL
        foreach (var dll in requiredDlls)
        {
            var sourceDllPath = Path.Combine(sourceDllDir, dll);
            var targetDllPath = Path.Combine(targetGameDir, dll);
            
            if (File.Exists(sourceDllPath))
            {
                // Ensure target file is not read-only
                if (File.Exists(targetDllPath))
                {
                    var fileInfo = new FileInfo(targetDllPath);
                    if (fileInfo.IsReadOnly)
                    {
                        fileInfo.IsReadOnly = false;
                    }
                }
                
                // Copy the DLL
                File.Copy(sourceDllPath, targetDllPath, true);
                _logger.LogInformation($"Copied {dll} to game directory");
            }
            else
            {
                _logger.LogWarning($"Could not find {dll} in DXVK version {version}, skipping");
                skippedDlls.Add(dll);
            }
        }
        
        return (true, skippedDlls);
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
            var allDxDlls = new List<string> {
                "d3d8.dll", "d3d9.dll", "d3d10.dll", "d3d10core.dll", "d3d11.dll", "d3d12.dll", "dxgi.dll"
            };
            results.Add($"Required DLLs: {string.Join(", ", allDxDlls)}");
            results.Add("----------------------------------------");
            
            // DXVK cache info
            var dxvkTypeDir = dxvkType == "dxvk-gplasync" ? "dxvk-gplasync-cache" : "dxvk-cache";
            var dxvkCacheDir = Path.Combine(_dataPath, dxvkTypeDir);
            
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
                        
                        var missingDlls = allDxDlls
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
                
                // Read the magic number (first 2 bytes)
                var buffer = new byte[2];
                await fileStream.ReadExactlyAsync(buffer, 0, 2);
                
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

            // Define the path for the status file within the app's data directory
            var statusDir = Path.Combine(_dataPath, "dxvk-status");
            Directory.CreateDirectory(statusDir); // Ensure the directory exists
            var statusFilePath = Path.Combine(statusDir, $"{appId}.json");

            // Save status to JSON file in the central status directory
            var json = System.Text.Json.JsonSerializer.Serialize(status, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            await File.WriteAllTextAsync(statusFilePath, json);
            
            _logger.LogInformation($"DXVK status updated for game with AppID {appId} at {statusFilePath}");
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

    // Determine the source directory for DXVK DLLs based on architecture and potential nested structures
    private string? FindSourceDxvkArchDir(string sourceDxvkBaseDir, string archSubfolder)
    {
        // Standard: <dataPath>/<cacheType>/<version>/<archSubfolder>
        var standardPath = Path.Combine(sourceDxvkBaseDir, archSubfolder);
        if (Directory.Exists(standardPath))
        {
            return standardPath;
        }

        // Check for nested structure
        var nestedDirs = Directory.GetDirectories(sourceDxvkBaseDir, "dxvk*");
        foreach (var nestedDir in nestedDirs)
        {
            var nestedArchDir = Path.Combine(nestedDir, archSubfolder);
            if (Directory.Exists(nestedArchDir))
            {
                return nestedArchDir;
            }
        }

        // Check for root level dxvk directories
        if (Directory.GetFiles(sourceDxvkBaseDir, "*.dll").Length > 0)
        {
            return sourceDxvkBaseDir;
        }

        return null;
    }
}