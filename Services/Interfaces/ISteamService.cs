using DxvkVersionManager.Models;

namespace DxvkVersionManager.Services.Interfaces;

public interface ISteamService
{
    Task<List<SteamGame>> GetInstalledGamesAsync(bool forceRefresh = true);
    Task<GameMetadata> GetGameMetadataAsync(string appId, string gameName, string installDir);
    Task<bool> SaveCustomGameMetadataAsync(string appId, Dictionary<string, object> metadataUpdates);
    Task<DxvkStatus?> GetGameDxvkStatusAsync(string appId);
    Task<bool> UpdateGameDxvkStatusAsync(string appId, DxvkStatus status);
    Task<List<GameInstallationStatus>> AnalyzeGameByAppIdAsync(string appId);
}