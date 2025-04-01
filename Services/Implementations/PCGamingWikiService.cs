namespace DxvkVersionManager.Services.Implementations;

using DxvkVersionManager.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using System.Linq;

public class PCGamingWikiService : IPCGamingWikiService
{
    private readonly HttpClient _httpClient;
    private readonly LoggingService _logger;
    private readonly Dictionary<string, List<string>> _dxVersionCache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    
    // PCGamingWiki API base URL
    private const string PCGamingWikiApiUrl = "https://www.pcgamingwiki.com/w/api.php";

    public PCGamingWikiService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "DxvkVersionManager/1.0");
        _httpClient.Timeout = TimeSpan.FromSeconds(10); // Add reasonable timeout to avoid hanging
        _logger = LoggingService.Instance;
        
        // Log service initialization for debugging
        _logger.LogInformation("PCGamingWikiService initialized");
    }

    /// <inheritdoc/>
    public async Task<List<string>> GetSupportedDirectXVersionsAsync(string appIdOrName)
    {
        try
        {
            _logger.LogInformation($"Fetching DirectX versions from PCGamingWiki for: {appIdOrName}");
            
            // Check cache first
            if (_dxVersionCache.TryGetValue(appIdOrName, out var cachedVersions))
            {
                _logger.LogInformation($"Using cached DirectX versions for {appIdOrName}: {string.Join(", ", cachedVersions)}");
                return cachedVersions;
            }

            // Get the Steam AppID for the game
            var appId = await GetSteamAppIdByNameAsync(appIdOrName);
            if (string.IsNullOrEmpty(appId))
            {
                _logger.LogWarning($"Could not determine Steam AppID for {appIdOrName}, cannot fetch PCGamingWiki data");
                return new List<string>();
            }

            _logger.LogInformation($"Querying PCGamingWiki API for game with AppID: {appId}");
            
            // Proper cargo query to get DirectX versions
            var apiUrl = $"{PCGamingWikiApiUrl}?action=cargoquery&tables=Infobox_game,API&fields=API.Direct3D_versions&join_on=Infobox_game._pageID=API._pageID&where=Infobox_game.Steam_AppID%20HOLDS%20%22{appId}%22&format=json";
            _logger.LogDebug($"PCGamingWiki API URL: {apiUrl}");
            
            var response = await _httpClient.GetStringAsync(apiUrl);
            _logger.LogDebug($"PCGamingWiki API response received: {response}");
            
            // Parse the response
            var directXVersions = new List<string>();
            var apiInfo = JsonDocument.Parse(response);
            
            if (apiInfo.RootElement.TryGetProperty("cargoquery", out var cargoQuery))
            {
                _logger.LogDebug($"Found cargoquery with {cargoQuery.GetArrayLength()} results");
                foreach (var result in cargoQuery.EnumerateArray())
                {
                    if (result.TryGetProperty("title", out var title) && 
                        title.TryGetProperty("Direct3D versions", out var directXVersionsElement))
                    {
                        var versionString = directXVersionsElement.GetString();
                        _logger.LogDebug($"Raw PCGamingWiki DirectX version: {versionString}");
                        
                        if (!string.IsNullOrEmpty(versionString))
                        {
                            // Wiki data may have multiple versions separated by comma or other characters
                            var versions = SplitAndNormalizeDirectXVersions(versionString);
                            _logger.LogDebug($"Split into {versions.Count} versions: {string.Join(", ", versions)}");
                            directXVersions.AddRange(versions);
                        }
                    }
                }
            }
            else
            {
                _logger.LogWarning($"No cargoquery data found in PCGamingWiki response for {appIdOrName}");
            }

            // Normalize the DirectX versions to match our format
            var normalizedVersions = directXVersions
                .Select(v => {
                    var normalized = NormalizeDirectXVersion(v);
                    _logger.LogDebug($"Normalized '{v}' to '{normalized}'");
                    return normalized;
                })
                .Where(v => !string.IsNullOrEmpty(v))
                .Distinct()
                .ToList();
            
            // Sort versions from oldest to newest
            SortDirectXVersions(normalizedVersions);
            
            // Cache the results
            _dxVersionCache[appIdOrName] = normalizedVersions;
            
            _logger.LogInformation($"Found {normalizedVersions.Count} DirectX versions for {appIdOrName} from PCGamingWiki: {string.Join(", ", normalizedVersions)}");
            return normalizedVersions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error getting DirectX versions for {appIdOrName} from PCGamingWiki");
            return new List<string>();
        }
    }
    
    private async Task<string> GetSteamAppIdByNameAsync(string gameName)
    {
        try
        {
            // First, try to extract the AppID if this is actually a Steam AppID already
            if (gameName != null && gameName.All(char.IsDigit))
            {
                _logger.LogDebug($"Input is already a Steam AppID: {gameName}");
                return gameName;
            }
            
            // For now, we just return the gameName as is since we're using it as the AppID in the SteamService
            // In a real implementation, we would have a more sophisticated lookup system
            _logger.LogInformation($"Using name as-is for PCGamingWiki lookup: {gameName}");
            return await Task.FromResult(gameName ?? string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error in GetSteamAppIdByNameAsync for {gameName}");
            return string.Empty;
        }
    }

    private List<string> SplitAndNormalizeDirectXVersions(string versionString)
    {
        var versions = new List<string>();
        
        if (string.IsNullOrEmpty(versionString))
            return versions;
        
        // Split by common separators
        var splitVersions = versionString.Split(new[] { ',', ';', '/', '|', '+', '&', '·', '•' }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var version in splitVersions)
        {
            var trimmed = version.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                versions.Add(trimmed);
            }
        }
        
        return versions;
    }
    
    private string NormalizeDirectXVersion(string version)
    {
        // Convert variations of DirectX names to our standard "Direct3D X" format
        var normalized = version.Trim().ToLowerInvariant();
        
        // First check if this is just a number like "11" or "12"
        if (int.TryParse(normalized, out int versionNum))
        {
            _logger.LogDebug($"Found plain numeric version: {versionNum}");
            return $"Direct3D {versionNum}";
        }
        
        if (normalized.Contains("directx") || normalized.Contains("dx"))
        {
            // Extract the version number
            var versionNumber = ExtractVersionNumber(normalized);
            if (!string.IsNullOrEmpty(versionNumber))
            {
                return $"Direct3D {versionNumber}";
            }
        }
        
        // If the string already has "Direct3D" or similar, just standardize the format
        if (normalized.Contains("direct3d") || normalized.Contains("d3d"))
        {
            var versionNumber = ExtractVersionNumber(normalized);
            if (!string.IsNullOrEmpty(versionNumber))
            {
                return $"Direct3D {versionNumber}";
            }
        }
        
        return string.Empty;
    }
    
    private string ExtractVersionNumber(string input)
    {
        // Extract version numbers like 9, 10, 11, 12, etc.
        // Also handle cases like "9.0c", "11.0", etc.
        var match = System.Text.RegularExpressions.Regex.Match(input, @"(\d+)(?:\.\d+)?(?:c)?");
        if (match.Success)
        {
            return match.Groups[1].Value;
        }
        
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