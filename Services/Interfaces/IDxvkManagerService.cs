using DxvkVersionManager.Models;

namespace DxvkVersionManager.Services.Interfaces;

public interface IDxvkManagerService
{
    Task<OperationResult> ApplyDxvkToGameAsync(SteamGame game, string dxvkType, string version);
    Task<OperationResult> RemoveDxvkFromGameAsync(SteamGame game);
    Task<bool> CheckBackupExistsAsync(string gameDir);
    List<string> GetRequiredDlls(string directXVersion);
    string GetArchSubfolder(GameMetadata metadata);
    Task<OperationResult> DiagnoseAndLogDxvkEnvironmentAsync(SteamGame game, string dxvkType = "dxvk");
    Task<OperationResult> RevertDxvkChangesAsync(SteamGame game);
}