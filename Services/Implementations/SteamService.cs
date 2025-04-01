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
    private readonly string _dataPath; // Use the application's data path
    private readonly string _metadataCachePath; // = <dataPath>/game-metadata-cache
    private List<SteamGame>? _cachedGames;
    private readonly LoggingService _logger;
    private readonly IPCGamingWikiService _pcGamingWikiService;
    private string _steamInstallPath = string.Empty;
    
    public SteamService(IPCGamingWikiService pcGamingWikiService, string dataPath)
    {
        _logger = LoggingService.Instance;
        _pcGamingWikiService = pcGamingWikiService;
        _dataPath = dataPath;
        _metadataCachePath = Path.Combine(_dataPath, "game-metadata-cache");
        
        try
        {
            // Ensure cache directory exists
            if (!Directory.Exists(_metadataCachePath))
            {
                Directory.CreateDirectory(_metadataCachePath);
                _logger.LogInformation($"Created game metadata cache directory: {_metadataCachePath}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing SteamService paths");
        }
    }
    
    public async Task<List<SteamGame>> GetInstalledGamesAsync(bool forceRefresh = true)
    {
        try
        {
            _logger.LogInformation("Fetching installed Steam games...");
            
            // Only use cached games if not forcing refresh
            if (_cachedGames != null && !forceRefresh)
            {
                return _cachedGames;
            }
            
            // Get Steam installation path from registry
            _steamInstallPath = GetSteamInstallPath();
            
            if (string.IsNullOrEmpty(_steamInstallPath) || !Directory.Exists(_steamInstallPath))
            {
                _logger.LogError($"Steam installation directory not found");
                return new List<SteamGame>();
            }
            
            string steamAppsPath = Path.Combine(_steamInstallPath, "steamapps");
            
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
                                    
                                    // Explicitly fetch PCGamingWiki data for each game
                                    if (metadata != null)
                                    {
                                        _logger.LogWarning($"Explicitly fetching PCGamingWiki data for {gameName} (AppID: {appId})");
                                        try
                                        {
                                            await FetchPCGamingWikiInfoAsync(metadata);
                                        }
                                        catch (Exception wikiEx)
                                        {
                                            _logger.LogError(wikiEx, $"Error fetching PCGamingWiki data: {wikiEx.Message}");
                                        }
                                    }
                                    
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
                    
                    // Ensure lists are sorted even when loaded from cache
                    if (metadata.AvailableDirect3dVersions != null)
                    {
                        SortDirectXVersions(metadata.AvailableDirect3dVersions);
                    }
                    if (metadata.OfficialDirectXVersions != null)
                    {
                        SortDirectXVersions(metadata.OfficialDirectXVersions);
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
            
            // Fetch PCGamingWiki information
            await FetchPCGamingWikiInfoAsync(metadata);
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
            List<string> allDetectedVersions = new List<string>();
            string? primaryExePath = null; // Path of the exe associated with the highest detected DX version
            
            // Helper function to track the highest priority executable found so far
            void UpdatePrimaryExe(string dll)
            {
                // Only update if we haven't found a primary exe yet, or if this DLL is higher priority
                if (primaryExePath == null || Array.IndexOf(dxVersions, dll) < Array.IndexOf(dxVersions, GetDllForVersion(allDetectedVersions.FirstOrDefault()))) 
                {
                    // Find the executable associated with this DLL
                    if (detectedDlls.TryGetValue(dll, out var exeList) && exeList.Count > 0)
                    {
                        // Find the full path for the first exe in the list (prioritize real exes over '[Found in game directory]')
                        var targetExeName = exeList.FirstOrDefault(e => e != "[Found in game directory]") ?? exeList.First();
                        if(targetExeName != "[Found in game directory]")
                        {
                             // Find the full path among the checked exes
                            primaryExePath = exesToCheck.FirstOrDefault(fullPath => Path.GetFileName(fullPath).Equals(targetExeName, StringComparison.OrdinalIgnoreCase));
                        }
                    }
                }
            }
            
            // Helper function to get the primary DLL for a given version string (e.g., "Direct3D 12" -> "d3d12.dll")
            string? GetDllForVersion(string? version)
            {
                if (version == null) return null;
                if (version.Contains("12")) return "d3d12.dll";
                if (version.Contains("11")) return "d3d11.dll";
                if (version.Contains("10")) return "d3d10.dll";
                if (version.Contains("9")) return "d3d9.dll";
                if (version.Contains("8")) return "d3d8.dll";
                return null;
            }
            
            if (detectedDlls.ContainsKey("d3d12.dll"))
            {
                allDetectedVersions.Add("Direct3D 12");
                UpdatePrimaryExe("d3d12.dll");
                _logger.LogInformation($"Detected DirectX 12 usage for {metadata.Name}");
                detected = true;
            }
            
            if (detectedDlls.ContainsKey("d3d11.dll"))
            {
                allDetectedVersions.Add("Direct3D 11");
                UpdatePrimaryExe("d3d11.dll");
                _logger.LogInformation($"Detected DirectX 11 usage for {metadata.Name}");
                detected = true;
            }
            
            if (detectedDlls.ContainsKey("d3d10.dll"))
            {
                allDetectedVersions.Add("Direct3D 10");
                UpdatePrimaryExe("d3d10.dll");
                _logger.LogInformation($"Detected DirectX 10 usage for {metadata.Name}");
                detected = true;
            }
            
            if (detectedDlls.ContainsKey("d3d9.dll"))
            {
                allDetectedVersions.Add("Direct3D 9");
                UpdatePrimaryExe("d3d9.dll");
                _logger.LogInformation($"Detected DirectX 9 usage for {metadata.Name}");
                detected = true;
            }
            
            if (detectedDlls.ContainsKey("d3d8.dll"))
            {
                allDetectedVersions.Add("Direct3D 8");
                UpdatePrimaryExe("d3d8.dll");
                _logger.LogInformation($"Detected DirectX 8 usage for {metadata.Name}");
                detected = true;
            }
            
            if (!detected && detectedDlls.ContainsKey("dxgi.dll"))
            {
                // DXGI is used by DX10/11/12, default to 11 if no specific version is found
                allDetectedVersions.Add("Direct3D 11");
                UpdatePrimaryExe("dxgi.dll");
                _logger.LogInformation($"Detected DXGI (defaulting to DX11) usage for {metadata.Name}");
                detected = true;
            }
            
            // Sort versions from oldest to newest
            SortDirectXVersions(allDetectedVersions);
            
            // Set detected versions in metadata
            metadata.AvailableDirect3dVersions = allDetectedVersions.Count > 0 ? allDetectedVersions : null;
            metadata.SupportsMultipleDirect3dVersions = allDetectedVersions.Count > 1;

            // Store the path of the primary executable
            metadata.TargetExecutablePath = primaryExePath;
            _logger.LogInformation($"Target executable for DXVK installation set to: {primaryExePath ?? "Not Found"}");
            
            // Set the primary version (highest available) if detected
            if (allDetectedVersions.Count > 0)
            {
                // Get the newest version (last in sorted list) for primary
                metadata.Direct3dVersions = allDetectedVersions[allDetectedVersions.Count - 1]; 
                detectionMethod = $"Found dependencies for multiple DirectX versions: {string.Join(", ", allDetectedVersions)}";
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
            var metadataPath = GetMetadataFilePath(appId);
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
                    case "d3dVersionAutoDetected":
                        metadata.D3dVersionAutoDetected = update.Value?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;
                        break;
                    case "detectionMethod":
                        metadata.DetectionMethod = update.Value?.ToString() ?? string.Empty;
                        break;
                    case "availableDirect3dVersions":
                        if (update.Value is List<string> versionsList)
                        {
                            metadata.AvailableDirect3dVersions = versionsList;
                        }
                        break;
                    case "supportsMultipleDirect3dVersions":
                        metadata.SupportsMultipleDirect3dVersions = update.Value?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;
                        break;
                    case "targetExecutablePath":
                        metadata.TargetExecutablePath = update.Value?.ToString();
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
    
    public async Task<DxvkStatus?> GetGameDxvkStatusAsync(string appId)
    {
        try
        {
            // Construct the path to the status file in the central status directory
            var statusDir = Path.Combine(_dataPath, "dxvk-status");
            var statusFilePath = Path.Combine(statusDir, $"{appId}.json");

            // Check if the status file exists
            if (File.Exists(statusFilePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(statusFilePath);
                    var status = JsonSerializer.Deserialize<DxvkStatus>(json);
                    
                    if (status == null)
                    {
                        _logger.LogWarning($"Deserialized DxvkStatus was null for game {appId}. Status file might be corrupt. Path: {statusFilePath}");
                        return new DxvkStatus { Patched = false }; // Treat corrupt file as not patched
                    }
                    
                    _logger.LogDebug($"Loaded DXVK status for game {appId}: Patched={status.Patched}, Version={status.DxvkVersion}");
                    return status;
                }
                catch (JsonException jsonEx)
                {
                     _logger.LogError(jsonEx, $"Error deserializing DXVK status JSON for game {appId}. Path: {statusFilePath}");
                     return new DxvkStatus { Patched = false }; // Treat corrupt file as not patched
                }
                catch (Exception deserializeEx)
                {
                    _logger.LogError(deserializeEx, $"Error reading/deserializing DXVK status file for game {appId}. Path: {statusFilePath}");
                    return new DxvkStatus { Patched = false }; // Treat error as not patched
                }
            }
            
            // If status file doesn't exist, the game is considered not patched
            _logger.LogDebug($"No DXVK status file found for game {appId} at {statusFilePath}. Assuming not patched.");
            return new DxvkStatus { Patched = false };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"General error getting DXVK status for game {appId}");
            return new DxvkStatus { Patched = false }; // Treat error as not patched
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
            _steamInstallPath = GetSteamInstallPath();
            
            if (string.IsNullOrEmpty(_steamInstallPath) || !Directory.Exists(_steamInstallPath))
            {
                _logger.LogError($"Steam installation directory not found");
                return results;
            }
            
            string steamAppsPath = Path.Combine(_steamInstallPath, "steamapps");
            
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

    // Method to get the specific metadata file path
    private string GetMetadataFilePath(string appId)
    {
        return Path.Combine(_metadataCachePath, $"{appId}.json");
    }

    // Add method to save game metadata
    private async Task SaveGameMetadataAsync(string appId, GameMetadata metadata)
    {
        try
        {            
            // Ensure lists are sorted before saving
            if (metadata.AvailableDirect3dVersions != null)
            {
                SortDirectXVersions(metadata.AvailableDirect3dVersions);
            }
            if (metadata.OfficialDirectXVersions != null)
            {
                SortDirectXVersions(metadata.OfficialDirectXVersions);
            }

            _logger.LogInformation($"Saving complete metadata for game with AppID {appId}");
            
            var metadataDict = new Dictionary<string, object>
            {
                ["name"] = metadata.Name,
                ["installDir"] = metadata.InstallDir,
                ["pageName"] = metadata.PageName,
                ["executable32bit"] = metadata.Executable32bit,
                ["executable64bit"] = metadata.Executable64bit,
                ["direct3dVersions"] = metadata.Direct3dVersions,
                ["d3dVersionAutoDetected"] = metadata.D3dVersionAutoDetected,
                ["architectureAutoDetected"] = metadata.ArchitectureAutoDetected,
                ["detectionMethod"] = metadata.DetectionMethod,
                ["detailedDetectionInfo"] = metadata.DetailedDetectionInfo,
                ["targetExecutablePath"] = metadata.TargetExecutablePath ?? string.Empty
            };
            
            // Add PCGamingWiki information
            metadataDict["hasWikiInformation"] = metadata.HasWikiInformation;
            
            if (metadata.OfficialDirectXVersions != null && metadata.OfficialDirectXVersions.Count > 0)
            {
                metadataDict["officialDirectXVersions"] = metadata.OfficialDirectXVersions;
            }
            
            // Add multi-DX support if available
            if (metadata.SupportsMultipleDirect3dVersions)
            {
                metadataDict["supportsMultipleDirect3dVersions"] = metadata.SupportsMultipleDirect3dVersions;
            }
            
            if (metadata.AvailableDirect3dVersions != null && metadata.AvailableDirect3dVersions.Count > 0)
            {
                metadataDict["availableDirect3dVersions"] = metadata.AvailableDirect3dVersions;
            }
            
            await SaveCustomGameMetadataAsync(appId, metadataDict);
            _logger.LogInformation($"Saved complete metadata for game with AppID {appId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error saving complete metadata for game with AppID {appId}");
        }
    }

    private string GetSteamGameCoverUrl(string appId)
    {
        // Use Steam's CDN for game header images
        return $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/header.jpg";
    }

    // Fetch PCGamingWiki information for a game
    private async Task FetchPCGamingWikiInfoAsync(GameMetadata metadata)
    {
        // DEBUG: Log received metadata
        _logger.LogDebug($"FetchPCGamingWikiInfoAsync received: AppId='{metadata.AppId ?? "NULL"}', Name='{metadata.Name ?? "NULL"}'");
        
        try
        {
            if (string.IsNullOrEmpty(metadata.AppId))
            {
                _logger.LogWarning("Cannot fetch PCGamingWiki info: AppID is missing");
                return;
            }
            
            _logger.LogInformation($"Fetching PCGamingWiki information for {metadata.Name} (AppID: {metadata.AppId})");
            
            // Initialize the lists if they're null
            if (metadata.OfficialDirectXVersions == null)
            {
                metadata.OfficialDirectXVersions = new List<string>();
            }
            
            // Get officially supported DirectX versions using AppID
            var officialDxVersions = await _pcGamingWikiService.GetSupportedDirectXVersionsAsync(metadata.AppId);
            _logger.LogDebug($"Received {officialDxVersions.Count} official DirectX versions from PCGamingWiki for {metadata.Name}");
            
            // Clear existing versions and add new ones
            metadata.OfficialDirectXVersions.Clear();
            if (officialDxVersions.Count > 0)
            {
                foreach (var version in officialDxVersions)
                {
                    metadata.OfficialDirectXVersions.Add(version);
                    _logger.LogDebug($"Added version: {version}");
                }
            }
            
            // Set the wiki information flag - based on whether we found DirectX versions
            metadata.HasWikiInformation = officialDxVersions.Count > 0;
            
            _logger.LogInformation($"Found {officialDxVersions.Count} official DirectX versions for {metadata.Name} (AppID: {metadata.AppId})");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error fetching PCGamingWiki information for {metadata.Name} (AppID: {metadata.AppId})");
        }
    }

    public async Task<List<SteamGame>> LoadGamesAsync()
    {
        try
        {
            _logger.LogWarning("LOAD GAMES DEBUG: Starting to load games");
            var games = new List<SteamGame>();
            var steamPaths = await FindSteamPathsAsync();
            if (steamPaths.Count == 0)
            {
                _logger.LogWarning("No Steam installation found");
                return games;
            }
            
            var steamAppsPath = FindSteamAppsPath(steamPaths[0]);
            if (string.IsNullOrEmpty(steamAppsPath))
            {
                _logger.LogWarning("SteamApps directory not found");
                return games;
            }
            
            _logger.LogInformation("Fetching installed Steam games...");
            
            // Get all Steam library folders
            var libraryFolders = await GetLibraryFoldersAsync(steamAppsPath);
            _logger.LogDebug($"Found {libraryFolders.Count} Steam library folders");
            
            // Debug log each library folder
            int libraryIndex = 1;
            foreach (var folder in libraryFolders)
            {
                _logger.LogDebug($"Library {libraryIndex++}: {folder}");
            }
            
            // For each library, process all manifest files
            var processedManifests = new HashSet<string>(); // Track processed manifests to avoid duplicates
            
            foreach (var libraryFolder in libraryFolders)
            {
                var manifestFiles = Directory.GetFiles(libraryFolder, "appmanifest_*.acf");
                
                foreach (var manifestFile in manifestFiles)
                {
                    try
                    {
                        // Skip already processed manifests
                        if (processedManifests.Contains(Path.GetFileName(manifestFile)))
                        {
                            continue;
                        }
                        processedManifests.Add(Path.GetFileName(manifestFile));
                        
                        _logger.LogDebug($"Processing manifest: {manifestFile} for AppID: {Path.GetFileNameWithoutExtension(manifestFile).Replace("appmanifest_", "")}");
                        
                        // Parse the manifest file
                        var game = await ParseManifestFileAsync(manifestFile, libraryFolder);
                        
                        if (game == null) continue;
                        
                        // Load or create metadata
                        _logger.LogWarning($"LOAD GAMES DEBUG: Getting metadata for {game.Name} (AppID: {game.AppId})");
                        var metadata = await GetGameMetadataAsync(game.AppId, game.Name, game.InstallDir);
                        if (metadata != null)
                        {
                            game.Metadata = metadata;
                            
                            // If installation directory is not set, update it
                            if (string.IsNullOrEmpty(metadata.InstallDir))
                            {
                                metadata.InstallDir = game.InstallDir;
                                await SaveCustomGameMetadataAsync(game.AppId, new Dictionary<string, object> { ["installDir"] = game.InstallDir });
                            }
                            
                            // Always fetch PCGamingWiki data for each game
                            _logger.LogWarning($"LOAD GAMES DEBUG: About to fetch PCGamingWiki data for {game.Name} (AppID: {game.AppId})");
                            bool wikiSuccess = false;
                            try
                            {
                                await FetchPCGamingWikiInfoAsync(game.Metadata);
                                wikiSuccess = true;
                                _logger.LogWarning($"LOAD GAMES DEBUG: PCGamingWiki fetch COMPLETED for {game.Name}");
                            }
                            catch (Exception wikiEx)
                            {
                                _logger.LogError(wikiEx, $"LOAD GAMES DEBUG: Critical error fetching PCGamingWiki data: {wikiEx.Message}");
                            }
                            _logger.LogWarning($"LOAD GAMES DEBUG: PCGamingWiki fetch result: {(wikiSuccess ? "Success" : "Failed")}");
                        }
                        else
                        {
                            // Auto-detect properties for this game
                            _logger.LogInformation($"Auto-detecting properties for {game.Name} at {game.InstallDir}");
                            var newMetadata = new GameMetadata
                            {
                                AppId = game.AppId,
                                Name = game.Name,
                                InstallDir = game.InstallDir
                            };
                            await DetectGamePropertiesAsync(newMetadata);
                            game.Metadata = newMetadata;
                            
                            // Also get PCGamingWiki information for newly detected games
                            _logger.LogWarning($"LOAD GAMES DEBUG: About to fetch PCGamingWiki data for new game {game.Name}");
                            bool wikiSuccess = false;
                            try
                            {
                                await FetchPCGamingWikiInfoAsync(game.Metadata);
                                wikiSuccess = true;
                                _logger.LogWarning($"LOAD GAMES DEBUG: PCGamingWiki fetch COMPLETED for new game {game.Name}");
                            }
                            catch (Exception wikiEx)
                            {
                                _logger.LogError(wikiEx, $"LOAD GAMES DEBUG: Critical error fetching PCGamingWiki data: {wikiEx.Message}");
                            }
                            _logger.LogWarning($"LOAD GAMES DEBUG: PCGamingWiki fetch result: {(wikiSuccess ? "Success" : "Failed")}");
                            
                            // Save the detected metadata
                            if (game.Metadata != null)
                            {
                                await SaveGameMetadataAsync(game.AppId, game.Metadata);
                            }
                        }
                        
                        // Get DXVK status (handle potential null)
                        game.DxvkStatus = await GetGameDxvkStatusAsync(game.AppId);
                        if (game.DxvkStatus == null)
                        {
                            _logger.LogWarning($"GetGameDxvkStatusAsync returned null for {game.Name}. Assigning default status.");
                            game.DxvkStatus = new DxvkStatus { Patched = false };
                        }
                        
                        games.Add(game);
                        _logger.LogDebug($"Added game to list: [{game.AppId}] {game.Name} at {game.InstallDir}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error processing manifest file: {manifestFile}");
                    }
                }
            }
            
            // Filter out Steamworks Common Redistributables and similar utility "games"
            var filteredGames = games
                .Where(g => !g.Name.Contains("Steamworks Common Redistributables"))
                .ToList();
            
            _logger.LogInformation($"Found {filteredGames.Count} installed Steam games (after filtering)");
            
            // Debug log the final game list
            foreach (var game in filteredGames)
            {
                _logger.LogDebug($"Final game list item: [{game.AppId}] {game.Name} at {game.InstallDir}");
            }
            
            _logger.LogWarning("LOAD GAMES DEBUG: Completed loading games");
            return filteredGames;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading installed Steam games");
            return new List<SteamGame>();
        }
    }

    // Get Steam library folders asynchronously
    private async Task<List<string>> GetLibraryFoldersAsync(string steamAppsPath)
    {
        try
        {
            var libraryFolders = new List<string> { steamAppsPath };
            
            // Read libraryfolders.vdf
            var libraryFoldersPath = Path.Combine(steamAppsPath, "libraryfolders.vdf");
            if (!File.Exists(libraryFoldersPath))
            {
                _logger.LogError($"Steam library folders file not found: {libraryFoldersPath}");
                return libraryFolders;
            }
            
            // Read file content asynchronously
            var vdfContent = await File.ReadAllTextAsync(libraryFoldersPath);
            
            // VDF parsing for newer Steam format (v2)
            var pathMatches = Regex.Matches(vdfContent, "\"path\"\\s+\"(.+?)\"", RegexOptions.Singleline);
            foreach (Match match in pathMatches)
            {
                if (match.Success)
                {
                    var path = match.Groups[1].Value.Replace("\\\\", "\\");
                    var libSteamAppsPath = Path.Combine(path, "steamapps");
                    if (Directory.Exists(libSteamAppsPath))
                    {
                        libraryFolders.Add(libSteamAppsPath);
                        _logger.LogDebug($"Found Steam library folder: {libSteamAppsPath}");
                    }
                }
            }
            
            // VDF parsing for older Steam format (legacy)
            if (libraryFolders.Count == 1) // Only the main Steam folder
            {
                var legacyMatches = Regex.Matches(vdfContent, "\"\\d+\"\\s+\"(.+?)\"", RegexOptions.Singleline);
                foreach (Match match in legacyMatches)
                {
                    if (match.Success)
                    {
                        var path = match.Groups[1].Value.Replace("\\\\", "\\");
                        var libSteamAppsPath = Path.Combine(path, "steamapps");
                        if (Directory.Exists(libSteamAppsPath))
                        {
                            libraryFolders.Add(libSteamAppsPath);
                            _logger.LogDebug($"Found Steam library folder (legacy format): {libSteamAppsPath}");
                        }
                    }
                }
            }
            
            // Remove duplicate library folders
            return libraryFolders.Distinct().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error parsing Steam library folders");
            return new List<string> { steamAppsPath };
        }
    }
    
    // Parse manifest file and return a SteamGame object
    private async Task<SteamGame?> ParseManifestFileAsync(string manifestFile, string libraryFolder)
    {
        try
        {
            var content = await File.ReadAllTextAsync(manifestFile);
            var appId = Path.GetFileNameWithoutExtension(manifestFile).Replace("appmanifest_", "");
            
            // Check if the game is actually installed by looking at StateFlags
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
                return null;
            }
            
            // Extract game name and install directory
            var nameMatch = Regex.Match(content, "\"name\"\\s+\"(.+?)\"");
            var installDirMatch = Regex.Match(content, "\"installdir\"\\s+\"(.+?)\"");
            
            if (nameMatch.Success && installDirMatch.Success)
            {
                var gameName = nameMatch.Groups[1].Value;
                // Use the library root path and properly construct path to steamapps/common
                var libraryRoot = Path.GetDirectoryName(libraryFolder); // Gets the parent of the steamapps folder
                if (libraryRoot != null)
                {
                    // Explicitly construct path with steamapps/common included
                    var installDir = Path.Combine(libraryRoot, "steamapps", "common", installDirMatch.Groups[1].Value);
                    
                    // Basic verification that the directory exists
                    if (Directory.Exists(installDir))
                    {
                        return new SteamGame
                        {
                            AppId = appId,
                            Name = gameName,
                            InstallDir = installDir
                        };
                    }
                    else
                    {
                        _logger.LogWarning($"Skipping game with non-existent directory: [{appId}] {gameName} at {installDir}");
                    }
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing manifest file: {manifestFile}");
            return null;
        }
    }

    private async Task<List<string>> FindSteamPathsAsync()
    {
        var paths = new List<string>();
        
        // Try to get Steam installation path from registry
        string regPath = GetSteamInstallPath();
        if (!string.IsNullOrEmpty(regPath) && Directory.Exists(regPath))
        {
            _logger.LogInformation($"Found Steam installation in registry (64-bit): {regPath}");
            paths.Add(regPath);
        }
        
        // Other common Steam installation paths
        var commonPaths = new[]
        {
            @"C:\Program Files (x86)\Steam",
            @"C:\Program Files\Steam",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam")
        };
        
        foreach (var path in commonPaths)
        {
            if (!paths.Contains(path) && Directory.Exists(path))
            {
                _logger.LogInformation($"Found Steam installation in common path: {path}");
                paths.Add(path);
            }
        }
        
        return await Task.FromResult(paths);
    }
    
    private string FindSteamAppsPath(string steamInstallPath)
    {
        _steamInstallPath = steamInstallPath; // Store for future use
        string steamAppsPath = Path.Combine(steamInstallPath, "steamapps");
        
        if (Directory.Exists(steamAppsPath))
        {
            return steamAppsPath;
        }
        
        // Try alternate case (SteamApps vs steamapps)
        steamAppsPath = Path.Combine(steamInstallPath, "SteamApps");
        if (Directory.Exists(steamAppsPath))
        {
            return steamAppsPath;
        }
        
        _logger.LogWarning($"SteamApps directory not found in {steamInstallPath}");
        return string.Empty;
    }

    private void SortDirectXVersions(List<string> versions)
    {
        versions.Sort((a, b) =>
        {
            // Helper function to extract version number
            int GetVersionNumber(string version)
            {
                if (version.Contains("8")) return 8;
                if (version.Contains("9")) return 9;
                if (version.Contains("10")) return 10;
                if (version.Contains("11")) return 11;
                if (version.Contains("12")) return 12;
                return 0;
            }
            
            // Sort by version number (ascending - oldest first)
            return GetVersionNumber(a).CompareTo(GetVersionNumber(b));
        });
    }
}