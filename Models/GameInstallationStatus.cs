namespace DxvkVersionManager.Models;

public class GameInstallationStatus
{
    public string AppId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string InstallDir { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public int StateFlags { get; set; }
    public bool DirectoryExists { get; set; }
    public int ExecutablesFound { get; set; }
    public int TotalFileCount { get; set; }
    public int SubdirectoryCount { get; set; }
    public bool HasSteamAppIdFile { get; set; }
    public bool HasSteamworksCommonFiles { get; set; }
    public bool IsProperlyInstalled { get; set; }
    public string AnalysisDetails { get; set; } = string.Empty;
} 