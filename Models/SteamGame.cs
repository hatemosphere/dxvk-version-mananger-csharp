namespace DxvkVersionManager.Models;

using CommunityToolkit.Mvvm.ComponentModel;

public class SteamGame : ObservableObject
{
    private string _appId = string.Empty;
    private string _name = string.Empty;
    private string _installDir = string.Empty;
    private string _path = string.Empty;
    private GameMetadata? _metadata;
    private DxvkStatus? _dxvkStatus;

    public string AppId
    {
        get => _appId;
        set => SetProperty(ref _appId, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string InstallDir
    {
        get => _installDir;
        set => SetProperty(ref _installDir, value);
    }

    public string Path
    {
        get => _path;
        set => SetProperty(ref _path, value);
    }

    public GameMetadata? Metadata
    {
        get => _metadata;
        set => SetProperty(ref _metadata, value);
    }

    public DxvkStatus? DxvkStatus
    {
        get => _dxvkStatus;
        set => SetProperty(ref _dxvkStatus, value);
    }
}