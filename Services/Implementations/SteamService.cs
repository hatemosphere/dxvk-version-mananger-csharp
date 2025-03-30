using System.Text.RegularExpressions;
using DxvkVersionManager.Models;
using DxvkVersionManager.Services.Interfaces;
using Microsoft.Win32;
using System.Text.Json;
using System.Text;

namespace DxvkVersionManager.Services.Implementations;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class SteamService : ISteamService
{
    private readonly string _userDataPath;
    private readonly string _metadataCachePath;
    private List<SteamGame>? _cachedGames;
    private readonly LoggingService _logger;
    
    public SteamService(string userDataPath)
    {
        ArgumentNullException.ThrowIfNull(userDataPath);
        _userDataPath = userDataPath;
        _metadataCachePath = Path.Combine(_userDataPath, "game-metadata-cache");
        _logger = LoggingService.Instance;
        
        // Ensure cache directory exists
        if (!Directory.Exists(_metadataCachePath))
        {
            Directory.CreateDirectory(_metadataCachePath);
            _logger.LogInformation($"Created game metadata cache directory: {_metadataCachePath}");
        }
    }
    
    public async Task<List<SteamGame>> GetInstalledGamesAsync()
    {
        try
        {
            _logger.LogInformation("Fetching installed Steam games...");
            
            // Get Steam installation path from registry
            string steamPath = GetSteamInstallPath();
            
            if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
            {
                _logger.LogError($"Steam installation directory not found");
                return new List<SteamGame>();
            }
            
            string steamAppsPath = Path.Combine(steamPath, "steamapps");
            
            // Read libraryfolders.vdf
            var libraryFoldersPath = Path.Combine(steamAppsPath, "libraryfolders.vdf");
            if (!File.Exists(libraryFoldersPath))
            {
                _logger.LogError($"Steam library folders file not found: {libraryFoldersPath}");
                return new List<SteamGame>();
            }
            
            // Get all library folders
            var libraryFolders = new List<string> { steamAppsPath };
            libraryFolders.AddRange(GetSteamLibraryFolders(libraryFoldersPath));
            
            // Remove duplicate library folders (important to avoid duplicate game entries)
            libraryFolders = libraryFolders.Distinct().ToList();
            
            _logger.LogDebug($"Found {libraryFolders.Count} Steam library folders");
            
            // Debug logging - List all library folders found
            for (int i = 0; i < libraryFolders.Count; i++)
            {
                _logger.LogDebug($"Library {i+1}: {libraryFolders[i]}");
            }
            
            // Get all installed games
            var games = new List<SteamGame>();
            
            // Process all libraries
            foreach (var library in libraryFolders)
            {
                var manifestFiles = Directory.GetFiles(library, "appmanifest_*.acf");
                
                foreach (var manifestFile in manifestFiles)
                {
                    try
                    {
                        var content = await File.ReadAllTextAsync(manifestFile);
                        var appId = Path.GetFileNameWithoutExtension(manifestFile).Replace("appmanifest_", "");
                        
                        // Add detailed logging for debugging
                        _logger.LogDebug($"Processing manifest: {manifestFile} for AppID: {appId}");
                        
                        // Check if the game is actually installed by looking at StateFlags
                        // Steam StateFlags: 4 = installed, 1026 = update required, etc.
                        var stateFlagsMatch = Regex.Match(content, "\"StateFlags\"\\s+\"(\\d+)\"");
                        var stateFlags = 0;
                        if (stateFlagsMatch.Success)
                        {
                            int.TryParse(stateFlagsMatch.Groups[1].Value, out stateFlags);
                        }
                        
                        // Check installation status - StateFlags 4 or 1026 typically indicate an installed game
                        bool isInstalled = (stateFlags & 4) != 0; // Check if bit 2 is set (value 4)
                        
                        if (!isInstalled)
                        {
                            _logger.LogDebug($"Skipping game with AppID {appId} as it doesn't appear to be installed (StateFlags: {stateFlags})");
                            continue;
                        }
                        
                        // Extract game name and install directory
                        var nameMatch = Regex.Match(content, "\"name\"\\s+\"(.+?)\"");
                        var installDirMatch = Regex.Match(content, "\"installdir\"\\s+\"(.+?)\"");
                        
                        if (nameMatch.Success && installDirMatch.Success)
                        {
                            var gameName = nameMatch.Groups[1].Value;
                            // Use the library root path and properly construct path to steamapps/common
                            var libraryRoot = Path.GetDirectoryName(library); // Gets the parent of the steamapps folder
                            if (libraryRoot != null)
                            {
                                // Explicitly construct path with steamapps/common included
                                var installDir = Path.Combine(libraryRoot, "steamapps", "common", installDirMatch.Groups[1].Value);
                                
                                // Log detailed game and path information
                                _logger.LogDebug($"Found game: [{appId}] {gameName} at path: {installDir}");
                                
                                // Basic verification that the directory exists
                                if (Directory.Exists(installDir))
                                {
                                    // Get game metadata
                                    var metadata = await GetGameMetadataAsync(appId, gameName, installDir);
                                    
                                    // Get DXVK status
                                    var dxvkStatus = await GetGameDxvkStatusAsync(appId);
                                    
                                    // Always ensure DxvkStatus is initialized
                                    if (dxvkStatus == null)
                                    {
                                        dxvkStatus = new DxvkStatus();
                                        _logger.LogWarning($"Created default DXVK status for {gameName} because it was null");
                                    }
                                    
                                    games.Add(new SteamGame
                                    {
                                        AppId = appId,
                                        Name = gameName,
                                        InstallDir = installDir,
                                        Metadata = metadata,
                                        DxvkStatus = dxvkStatus
                                    });
                                    
                                    _logger.LogDebug($"Added game to list: [{appId}] {gameName} at {installDir}");
                                }
                                else
                                {
                                    _logger.LogWarning($"Skipping game with non-existent directory: [{appId}] {gameName} at {installDir}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error processing manifest file: {manifestFile}");
                    }
                }
            }
            
            // Filter out the Steamworks Common Redistributables
            games = games.Where(g => g.Name != null && !g.Name.Contains("Steamworks Common Redistributables")).ToList();
            
            _logger.LogInformation($"Found {games.Count} installed Steam games (after filtering)");
            
            // Debug logging - List all games found with their AppIDs
            foreach (var game in games)
            {
                _logger.LogDebug($"Final game list item: [{game.AppId}] {game.Name} at {game.InstallDir}");
            }
            
            _cachedGames = games;
            return games;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching installed Steam games");
            return new List<SteamGame>();
        }
    }
    
    public async Task<GameMetadata> GetGameMetadataAsync(string appId, string gameName, string installDir)
    {
        var metadataFilePath = GetMetadataFilePath(appId);
        
        // Check if we have saved metadata
        if (File.Exists(metadataFilePath))
        {
            try
            {
                var jsonContent = await File.ReadAllTextAsync(metadataFilePath);
                var metadata = JsonSerializer.Deserialize<GameMetadata>(jsonContent);
                
                if (metadata != null)
                {
                    // Make sure we have the latest paths
                    metadata.InstallDir = installDir;
                    
                    // Set cover URL if it's empty or not working
                    if (string.IsNullOrEmpty(metadata.CoverUrl))
                    {
                        metadata.CoverUrl = GetSteamGameCoverUrl(appId);
                    }
                    
                    return metadata;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error loading metadata for {gameName} (AppID: {appId})");
            }
        }
        
        // Create new metadata with auto-detection
        var newMetadata = new GameMetadata
        {
            AppId = appId,
            Name = gameName,
            InstallDir = installDir,
            Direct3dVersions = "Unknown", // Will be auto-detected
            Executable32bit = false,      // Will be auto-detected
            Executable64bit = false,      // Will be auto-detected
            SupportsVulkan = false,       // Default
            CoverUrl = GetSteamGameCoverUrl(appId) // Set game cover URL
        };

        // Auto-detect game properties
        await DetectGamePropertiesAsync(newMetadata);
        
        // Save the metadata
        await SaveGameMetadataAsync(appId, newMetadata);
        
        return newMetadata;
    }
    
    private async Task DetectGamePropertiesAsync(GameMetadata metadata)
    {
        try
        {
            _logger.LogInformation($"Auto-detecting properties for {metadata.Name} at {metadata.InstallDir}");
            
            if (!Directory.Exists(metadata.InstallDir))
            {
                _logger.LogWarning($"Game directory not found: {metadata.InstallDir}");
                return;
            }

            // Get all executables in the game directory
            var exeFiles = Directory.GetFiles(metadata.InstallDir, "*.exe", SearchOption.AllDirectories);
            
            if (exeFiles.Length == 0)
            {
                _logger.LogWarning($"No executable files found in {metadata.InstallDir}");
                return;
            }

            // Try to find the main executable for architecture detection
            string? mainExecutable = null;
            
            // First try: Look for an executable with the same name as the installation folder
            var installDirName = Path.GetFileName(metadata.InstallDir);
            var matchingExe = exeFiles.FirstOrDefault(f => 
                Path.GetFileNameWithoutExtension(f).Equals(installDirName, StringComparison.OrdinalIgnoreCase));
            
            if (matchingExe != null)
            {
                mainExecutable = matchingExe;
                _logger.LogDebug($"Found matching executable {Path.GetFileName(mainExecutable)} for {metadata.Name}");
            }
            
            // Second try: Look in common locations
            if (mainExecutable == null)
            {
                // Look for exe files in the root
                var rootExeFiles = Directory.GetFiles(metadata.InstallDir, "*.exe", SearchOption.TopDirectoryOnly);
                if (rootExeFiles.Length > 0)
                {
                    // Exclude common non-game executables
                    var nonGameExePatterns = new[] { "unins", "setup", "config", "launcher", "crash", "redist" };
                    var gameExes = rootExeFiles.Where(f => 
                        !nonGameExePatterns.Any(p => 
                            Path.GetFileNameWithoutExtension(f).ToLower().Contains(p))).ToList();
                    
                    if (gameExes.Count > 0)
                    {
                        mainExecutable = gameExes[0];
                        _logger.LogDebug($"Found probable game executable in root: {Path.GetFileName(mainExecutable)}");
                    }
                    else if (rootExeFiles.Length > 0)
                    {
                        mainExecutable = rootExeFiles[0];
                        _logger.LogDebug($"No clear game executable found, using first exe: {Path.GetFileName(mainExecutable)}");
                    }
                }
            }
            
            // Third try: Look in bin directories
            if (mainExecutable == null)
            {
                string[] binDirs = { "bin", "Bin", "binaries", "Binaries" };
                foreach (var binDir in binDirs)
                {
                    var binPath = Path.Combine(metadata.InstallDir, binDir);
                    if (Directory.Exists(binPath))
                    {
                        var binExes = Directory.GetFiles(binPath, "*.exe", SearchOption.TopDirectoryOnly);
                        if (binExes.Length > 0)
                        {
                            mainExecutable = binExes[0];
                            _logger.LogDebug($"Found executable in bin directory: {Path.GetFileName(mainExecutable)}");
                            break;
                        }
                    }
                }
            }
            
            // Final fallback: Use the first exe we find
            if (mainExecutable == null && exeFiles.Length > 0)
            {
                mainExecutable = exeFiles[0];
                _logger.LogDebug($"Using fallback executable: {Path.GetFileName(mainExecutable)}");
            }
            
            // If we found an executable, detect its architecture
            if (mainExecutable != null)
            {
                // Detect architecture
                DetectExecutableArchitecture(mainExecutable, metadata);
            }
            
            // For DirectX detection, examine multiple executables to improve accuracy
            await DetectDirectXVersionFromMultipleExesAsync(exeFiles, metadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error during game property detection for {metadata.Name}");
        }
    }
    
    private void DetectExecutableArchitecture(string exePath, GameMetadata metadata)
    {
        try
        {
            using var stream = new FileStream(exePath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(stream);
            
            // Read DOS header
            stream.Seek(0x3C, SeekOrigin.Begin);
            int peOffset = reader.ReadInt32();
            
            // Read PE signature to confirm this is a valid PE file
            stream.Seek(peOffset, SeekOrigin.Begin);
            uint peSignature = reader.ReadUInt32();
            if (peSignature != 0x00004550) // "PE\0\0"
            {
                _logger.LogWarning($"Invalid PE signature in {exePath}");
                return;
            }
            
            // Read the FILE_HEADER Machine field
            stream.Seek(peOffset + 4, SeekOrigin.Begin);
            ushort machine = reader.ReadUInt16();
            
            switch (machine)
            {
                case 0x014c: // IMAGE_FILE_MACHINE_I386
                    metadata.Executable32bit = true;
                    metadata.Executable64bit = false;
                    metadata.ArchitectureAutoDetected = true;
                    _logger.LogInformation($"Detected 32-bit executable: {Path.GetFileName(exePath)}");
                    break;
                case 0x8664: // IMAGE_FILE_MACHINE_AMD64
                    metadata.Executable32bit = false;
                    metadata.Executable64bit = true;
                    metadata.ArchitectureAutoDetected = true;
                    _logger.LogInformation($"Detected 64-bit executable: {Path.GetFileName(exePath)}");
                    break;
                default:
                    _logger.LogWarning($"Unknown architecture (0x{machine:X4}) for {exePath}");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error detecting architecture for {exePath}");
        }
    }
    
    private async Task DetectDirectXVersionFromMultipleExesAsync(string[] exeFiles, GameMetadata metadata)
    {
        try
        {
            _logger.LogInformation($"Scanning {exeFiles.Length} executables for DirectX dependencies in {metadata.Name}");
            
            // Dictionary to track which DLLs were found in which executables
            var detectedDlls = new Dictionary<string, List<string>>(); // DLL -> List of executable names
            
            // Priority order for DirectX versions (higher version takes precedence)
            var dxVersions = new[] { "d3d12.dll", "d3d11.dll", "d3d10.dll", "d3d9.dll", "d3d8.dll", "dxgi.dll" };
            
            // Number of executables to check (limit to avoid excessive checks on large games)
            const int MAX_EXES_TO_CHECK = 10;
            
            // Prioritize executables in the root directory and common bin folders
            var prioritizedExes = new List<string>();
            
            // Add root executables first
            prioritizedExes.AddRange(Directory.GetFiles(metadata.InstallDir, "*.exe", SearchOption.TopDirectoryOnly));
            
            // Add executables from common bin folders
            string[] binDirs = { "bin", "Bin", "binaries", "Binaries" };
            foreach (var binDir in binDirs)
            {
                var binPath = Path.Combine(metadata.InstallDir, binDir);
                if (Directory.Exists(binPath))
                {
                    prioritizedExes.AddRange(Directory.GetFiles(binPath, "*.exe", SearchOption.TopDirectoryOnly));
                }
            }
            
            // Add any remaining executables
            var remainingExes = exeFiles.Where(exe => !prioritizedExes.Contains(exe)).ToList();
            prioritizedExes.AddRange(remainingExes);
            
            // Ensure we don't have duplicates
            prioritizedExes = prioritizedExes.Distinct().ToList();
            
            // Check for executables with game-like names first
            var nonGameExePatterns = new[] { "unins", "setup", "config", "launcher", "crash", "redist" };
            prioritizedExes.Sort((a, b) => {
                string nameA = Path.GetFileNameWithoutExtension(a).ToLowerInvariant();
                string nameB = Path.GetFileNameWithoutExtension(b).ToLowerInvariant();
                
                bool isNonGameA = nonGameExePatterns.Any(p => nameA.Contains(p));
                bool isNonGameB = nonGameExePatterns.Any(p => nameB.Contains(p));
                
                if (isNonGameA && !isNonGameB) return 1;  // A is non-game, B might be game
                if (!isNonGameA && isNonGameB) return -1; // A might be game, B is non-game
                return 0;
            });
            
            // Limit the number of executables to check
            var exesToCheck = prioritizedExes.Take(MAX_EXES_TO_CHECK).ToArray();
            
            // Also check for DLL files in the game directory
            var dllsInGameDir = new Dictionary<string, bool>();
            foreach (var dll in dxVersions)
            {
                dllsInGameDir[dll] = File.Exists(Path.Combine(metadata.InstallDir, dll));
                if (dllsInGameDir[dll])
                {
                    if (!detectedDlls.ContainsKey(dll))
                        detectedDlls[dll] = new List<string>();
                        
                    detectedDlls[dll].Add("[Found in game directory]");
                }
            }
            
            // Check each executable for DirectX dependencies
            foreach (var exePath in exesToCheck)
            {
                var exeName = Path.GetFileName(exePath);
                _logger.LogDebug($"Checking executable {exeName} for DirectX dependencies");
                
                foreach (var dll in dxVersions)
                {
                    bool hasDependency = await CheckForDllDependencyAsync(exePath, dll);
                    if (hasDependency)
                    {
                        if (!detectedDlls.ContainsKey(dll))
                            detectedDlls[dll] = new List<string>();
                            
                        detectedDlls[dll].Add(exeName);
                        _logger.LogDebug($"Found dependency on {dll} in {exeName}");
                    }
                }
            }
            
            // Determine the DirectX version based on detected dependencies
            bool detected = false;
            string detectionMethod = string.Empty;
            
            if (detectedDlls.ContainsKey("d3d12.dll"))
            {
                metadata.Direct3dVersions = "Direct3D 12";
                detectionMethod = $"Found d3d12.dll dependency in: {string.Join(", ", detectedDlls["d3d12.dll"])}";
                _logger.LogInformation($"Detected DirectX 12 usage for {metadata.Name}");
                detected = true;
            }
            else if (detectedDlls.ContainsKey("d3d11.dll"))
            {
                metadata.Direct3dVersions = "Direct3D 11";
                detectionMethod = $"Found d3d11.dll dependency in: {string.Join(", ", detectedDlls["d3d11.dll"])}";
                _logger.LogInformation($"Detected DirectX 11 usage for {metadata.Name}");
                detected = true;
            }
            else if (detectedDlls.ContainsKey("d3d10.dll"))
            {
                metadata.Direct3dVersions = "Direct3D 10";
                detectionMethod = $"Found d3d10.dll dependency in: {string.Join(", ", detectedDlls["d3d10.dll"])}";
                _logger.LogInformation($"Detected DirectX 10 usage for {metadata.Name}");
                detected = true;
            }
            else if (detectedDlls.ContainsKey("d3d9.dll"))
            {
                metadata.Direct3dVersions = "Direct3D 9";
                detectionMethod = $"Found d3d9.dll dependency in: {string.Join(", ", detectedDlls["d3d9.dll"])}";
                _logger.LogInformation($"Detected DirectX 9 usage for {metadata.Name}");
                detected = true;
            }
            else if (detectedDlls.ContainsKey("d3d8.dll"))
            {
                metadata.Direct3dVersions = "Direct3D 8";
                detectionMethod = $"Found d3d8.dll dependency in: {string.Join(", ", detectedDlls["d3d8.dll"])}";
                _logger.LogInformation($"Detected DirectX 8 usage for {metadata.Name}");
                detected = true;
            }
            else if (detectedDlls.ContainsKey("dxgi.dll"))
            {
                // DXGI is used by DX10/11/12, default to 11 if no specific version is found
                metadata.Direct3dVersions = "Direct3D 11";
                detectionMethod = $"Found dxgi.dll dependency in: {string.Join(", ", detectedDlls["dxgi.dll"])}";
                _logger.LogInformation($"Detected DXGI (defaulting to DX11) usage for {metadata.Name}");
                detected = true;
            }
            else
            {
                // Could not determine directly, use heuristics based on game release date or other factors
                _logger.LogWarning($"Could not detect DirectX version for {metadata.Name}, leaving as Unknown");
                detected = false;
                detectionMethod = "Failed to detect DirectX version in any executable";
            }
            
            // Create a summary of all detected DirectX DLLs
            var detectedDllSummary = new StringBuilder();
            foreach (var entry in detectedDlls)
            {
                var uniqueExes = entry.Value.Distinct().ToList();
                detectedDllSummary.AppendLine($"{entry.Key}: {string.Join(", ", uniqueExes)}");
            }
            
            if (detectedDllSummary.Length > 0)
            {
                // Additional detailed detection info
                metadata.DetailedDetectionInfo = detectedDllSummary.ToString().Trim();
            }
            
            // Set the auto-detected flag if we successfully identified the DirectX version
            metadata.D3dVersionAutoDetected = detected;
            
            // Store the detection method in the logs and metadata
            _logger.LogDebug($"DirectX detection method for {metadata.Name}: {detectionMethod}");
            metadata.DetectionMethod = detectionMethod;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error detecting DirectX version for {metadata.Name}");
        }
    }
    
    private async Task<bool> CheckForDllDependencyAsync(string exePath, string dllName)
    {
        try
        {
            // Read the file in chunks to check for the DLL name in the import section
            // This is a simplified approach that looks for the DLL name in the binary
            using var fileStream = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            
            // Read in chunks of 4KB
            var buffer = new byte[4096];
            var stringBuffer = new StringBuilder();
            int bytesRead;
            
            // Read the file in chunks and look for the DLL name
            while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                // Convert the chunk to a string for easy searching
                // Only add ASCII characters to avoid encoding issues
                for (int i = 0; i < bytesRead; i++)
                {
                    // Only include ASCII printable chars
                    if (buffer[i] >= 32 && buffer[i] <= 126)
                    {
                        stringBuffer.Append((char)buffer[i]);
                    }
                    else
                    {
                        // Add a space for non-printable chars to separate strings
                        stringBuffer.Append(' ');
                    }
                }
                
                // Check if the DLL name appears in this chunk
                if (stringBuffer.ToString().IndexOf(dllName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
                
                // Clear the buffer for the next chunk, but keep the last 20 chars
                // in case the DLL name is split across chunks
                if (stringBuffer.Length > 20)
                {
                    stringBuffer.Remove(0, stringBuffer.Length - 20);
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error checking for DLL dependency {dllName} in {exePath}");
            return false;
        }
    }
    
    public async Task<bool> SaveCustomGameMetadataAsync(string appId, Dictionary<string, object> metadataUpdates)
    {
        try
        {
            var metadataPath = Path.Combine(_metadataCachePath, $"{appId}.json");
            GameMetadata metadata;
            
            // Load existing metadata if available
            if (File.Exists(metadataPath))
            {
                var json = await File.ReadAllTextAsync(metadataPath);
                metadata = JsonSerializer.Deserialize<GameMetadata>(json) ?? new GameMetadata();
            }
            else
            {
                metadata = new GameMetadata
                {
                    PageName = "",
                    CoverUrl = "",
                    Direct3dVersions = "Unknown",
                    Executable32bit = false,
                    Executable64bit = false,
                    VulkanVersions = null,
                    InstallDir = string.Empty,
                    CustomD3d = false,
                    CustomExec = false
                };
            }
            
            // Update properties based on the metadataUpdates dictionary
            foreach (var update in metadataUpdates)
            {
                switch (update.Key)
                {
                    case "direct3dVersions":
                        metadata.Direct3dVersions = update.Value?.ToString() ?? "Unknown";
                        break;
                    case "executable32bit":
                        metadata.Executable32bit = update.Value?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;
                        break;
                    case "executable64bit":
                        metadata.Executable64bit = update.Value?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;
                        break;
                    case "custom_d3d":
                        metadata.CustomD3d = update.Value?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;
                        break;
                    case "custom_exec":
                        metadata.CustomExec = update.Value?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;
                        break;
                }
            }
            
            // Save updated metadata
            var serializedMetadata = JsonSerializer.Serialize(metadata, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            await File.WriteAllTextAsync(metadataPath, serializedMetadata);
            _logger.LogInformation($"Updated metadata for game {appId}");
            
            // If we have cached games, update the metadata there too
            if (_cachedGames != null)
            {
                var game = _cachedGames.FirstOrDefault(g => g.AppId == appId);
                if (game != null)
                {
                    game.Metadata = metadata;
                }
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error saving metadata for game {appId}");
            return false;
        }
    }
    
    public async Task<DxvkStatus> GetGameDxvkStatusAsync(string appId)
    {
        try
        {
            var statusPath = Path.Combine(_metadataCachePath, $"{appId}_dxvk.json");
            if (File.Exists(statusPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(statusPath);
                    var status = JsonSerializer.Deserialize<DxvkStatus>(json);
                    
                    // Always return a new DxvkStatus if deserialization returns null
                    if (status == null)
                    {
                        _logger.LogWarning($"Deserialized DxvkStatus was null for game {appId}, creating new instance");
                        return new DxvkStatus();
                    }
                    
                    return status;
                }
                catch (Exception deserializeEx)
                {
                    _logger.LogError(deserializeEx, $"Error deserializing DXVK status for game {appId}, creating new instance");
                    return new DxvkStatus();
                }
            }
            
            _logger.LogInformation($"No DXVK status file found for game {appId}, creating new instance");
            return new DxvkStatus();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting DXVK status for game {appId}, creating new instance");
            return new DxvkStatus();
        }
    }
    
    public async Task<bool> UpdateGameDxvkStatusAsync(string appId, DxvkStatus status)
    {
        try
        {
            var statusPath = Path.Combine(_metadataCachePath, $"{appId}_dxvk.json");
            var json = JsonSerializer.Serialize(status, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            await File.WriteAllTextAsync(statusPath, json);
            _logger.LogInformation($"Updated DXVK status for game {appId}");
            
            // If we have cached games, update the status there too
            if (_cachedGames != null)
            {
                var game = _cachedGames.FirstOrDefault(g => g.AppId == appId);
                if (game != null)
                {
                    game.DxvkStatus = status;
                }
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error updating DXVK status for game {appId}");
            return false;
        }
    }
    
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private string GetSteamInstallPath()
    {
        try
        {
            // Try to get Steam install path from registry (64-bit)
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Wow6432Node\Valve\Steam"))
            {
                if (key != null)
                {
                    var installPath = key.GetValue("InstallPath") as string;
                    if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                    {
                        _logger.LogInformation($"Found Steam installation in registry (64-bit): {installPath}");
                        return installPath;
                    }
                }
            }
            
            // Try to get Steam install path from registry (32-bit)
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam"))
            {
                if (key != null)
                {
                    var installPath = key.GetValue("InstallPath") as string;
                    if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                    {
                        _logger.LogInformation($"Found Steam installation in registry (32-bit): {installPath}");
                        return installPath;
                    }
                }
            }
            
            // Try common install locations
            var commonPaths = new[] 
            {
                @"C:\Program Files (x86)\Steam",
                @"C:\Program Files\Steam",
                @"C:\Steam"
            };
            
            foreach (var path in commonPaths)
            {
                if (Directory.Exists(path))
                {
                    _logger.LogInformation($"Found Steam installation in common location: {path}");
                    return path;
                }
            }
            
            _logger.LogError("Could not find Steam installation path");
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding Steam installation path");
            return string.Empty;
        }
    }
    
    private List<string> GetSteamLibraryFolders(string libraryFoldersPath)
    {
        var libraryFolders = new List<string>();
        try
        {
            var vdfContent = File.ReadAllText(libraryFoldersPath);
            
            // VDF parsing for newer Steam format (v2)
            var pathMatches = Regex.Matches(vdfContent, "\"path\"\\s+\"(.+?)\"", RegexOptions.Singleline);
            foreach (Match match in pathMatches)
            {
                if (match.Success)
                {
                    var path = match.Groups[1].Value.Replace("\\\\", "\\");
                    var steamAppsPath = Path.Combine(path, "steamapps");
                    if (Directory.Exists(steamAppsPath))
                    {
                        libraryFolders.Add(steamAppsPath);
                        _logger.LogDebug($"Found Steam library folder: {steamAppsPath}");
                    }
                }
            }
            
            // VDF parsing for older Steam format (legacy)
            if (libraryFolders.Count == 0)
            {
                var legacyMatches = Regex.Matches(vdfContent, "\"\\d+\"\\s+\"(.+?)\"", RegexOptions.Singleline);
                foreach (Match match in legacyMatches)
                {
                    if (match.Success)
                    {
                        var path = match.Groups[1].Value.Replace("\\\\", "\\");
                        var steamAppsPath = Path.Combine(path, "steamapps");
                        if (Directory.Exists(steamAppsPath))
                        {
                            libraryFolders.Add(steamAppsPath);
                            _logger.LogDebug($"Found Steam library folder (legacy format): {steamAppsPath}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error parsing Steam library folders file: {libraryFoldersPath}");
        }
        
        return libraryFolders;
    }
    
    // Helper method to analyze a game's installation structure
    private GameInstallationStatus AnalyzeGameInstallation(string appId, string gameName, string installDir, string manifestFile, int stateFlags)
    {
        var status = new GameInstallationStatus
        {
            AppId = appId,
            Name = gameName,
            InstallDir = installDir,
            ManifestPath = manifestFile,
            StateFlags = stateFlags,
            DirectoryExists = Directory.Exists(installDir),
            IsProperlyInstalled = false
        };
        
        if (!status.DirectoryExists)
        {
            status.AnalysisDetails = "Installation directory does not exist";
            return status;
        }
        
        try
        {
            // Count executables for informational purposes
            status.ExecutablesFound = Directory.GetFiles(installDir, "*.exe", SearchOption.AllDirectories).Length;
            
            // Check for total file count
            status.TotalFileCount = Directory.GetFiles(installDir, "*.*", SearchOption.AllDirectories).Length;
            
            // Check for directory structure complexity
            status.SubdirectoryCount = Directory.GetDirectories(installDir, "*", SearchOption.AllDirectories).Length;
            
            // Check if there's a Steam cached AppID file (steam_appid.txt)
            status.HasSteamAppIdFile = File.Exists(Path.Combine(installDir, "steam_appid.txt"));
            
            // Additional specific Steam game files
            status.HasSteamworksCommonFiles = Directory.Exists(Path.Combine(installDir, "steamworks_common_redistributables"));
            
            // Determine if this looks like a properly installed game (basic check)
            status.IsProperlyInstalled = 
                (status.StateFlags & 4) != 0 && // StateFlags indicates installed
                status.TotalFileCount > 10;     // Has a reasonable number of files
            
            // Construct analysis details
            status.AnalysisDetails = $"Installation appears {(status.IsProperlyInstalled ? "valid" : "incomplete")}. " +
                $"Found {status.ExecutablesFound} executables, {status.TotalFileCount} total files, " +
                $"{status.SubdirectoryCount} subdirectories. " +
                $"StateFlags: {status.StateFlags}";
        }
        catch (Exception ex)
        {
            status.AnalysisDetails = $"Error analyzing installation: {ex.Message}";
            _logger.LogError(ex, $"Error analyzing game installation for {gameName} at {installDir}");
        }
        
        return status;
    }
    
    // Public method to analyze a specific game's installation in all libraries
    public async Task<List<GameInstallationStatus>> AnalyzeGameByAppIdAsync(string appId)
    {
        try
        {
            var results = new List<GameInstallationStatus>();
            
            // Get Steam installation path from registry
            string steamPath = GetSteamInstallPath();
            
            if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
            {
                _logger.LogError($"Steam installation directory not found");
                return results;
            }
            
            string steamAppsPath = Path.Combine(steamPath, "steamapps");
            
            // Read libraryfolders.vdf
            var libraryFoldersPath = Path.Combine(steamAppsPath, "libraryfolders.vdf");
            if (!File.Exists(libraryFoldersPath))
            {
                _logger.LogError($"Steam library folders file not found: {libraryFoldersPath}");
                return results;
            }
            
            // Get all library folders
            var libraryFolders = new List<string> { steamAppsPath };
            libraryFolders.AddRange(GetSteamLibraryFolders(libraryFoldersPath));
            
            // Remove duplicate library folders
            libraryFolders = libraryFolders.Distinct().ToList();
            
            _logger.LogDebug($"Scanning {libraryFolders.Count} Steam library folders for AppID {appId}");
            
            // Process all libraries
            foreach (var library in libraryFolders)
            {
                // Look for a manifest file for this specific AppID
                var manifestFile = Path.Combine(library, $"appmanifest_{appId}.acf");
                
                if (File.Exists(manifestFile))
                {
                    try
                    {
                        var content = await File.ReadAllTextAsync(manifestFile);
                        
                        // Extract game information
                        var nameMatch = Regex.Match(content, "\"name\"\\s+\"(.+?)\"");
                        var installDirMatch = Regex.Match(content, "\"installdir\"\\s+\"(.+?)\"");
                        var stateFlagsMatch = Regex.Match(content, "\"StateFlags\"\\s+\"(\\d+)\"");
                        
                        if (nameMatch.Success && installDirMatch.Success)
                        {
                            var gameName = nameMatch.Groups[1].Value;
                            var libraryRoot = Path.GetDirectoryName(library);
                            var stateFlags = 0;
                            
                            if (stateFlagsMatch.Success)
                            {
                                int.TryParse(stateFlagsMatch.Groups[1].Value, out stateFlags);
                            }
                            
                            if (libraryRoot != null)
                            {
                                var installDir = Path.Combine(libraryRoot, "steamapps", "common", installDirMatch.Groups[1].Value);
                                
                                // Analyze the installation
                                var analysis = AnalyzeGameInstallation(appId, gameName, installDir, manifestFile, stateFlags);
                                results.Add(analysis);
                                
                                _logger.LogInformation($"Found {appId} ({gameName}) at {library}: {analysis.AnalysisDetails}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error processing manifest file: {manifestFile}");
                    }
                }
            }
            
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error analyzing game with AppID {appId}");
            return new List<GameInstallationStatus>();
        }
    }

    // Add method to get metadata file path
    private string GetMetadataFilePath(string appId)
    {
        return Path.Combine(_metadataCachePath, $"{appId}.json");
    }

    // Add method to save game metadata
    private async Task<bool> SaveGameMetadataAsync(string appId, GameMetadata metadata)
    {
        try
        {
            var metadataFilePath = GetMetadataFilePath(appId);
            var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            
            await File.WriteAllTextAsync(metadataFilePath, json);
            _logger.LogInformation($"Saved metadata for game with AppID {appId}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error saving metadata for game with AppID {appId}");
            return false;
        }
    }

    private string GetSteamGameCoverUrl(string appId)
    {
        // Use Steam's CDN for game header images
        return $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/header.jpg";
    }
}