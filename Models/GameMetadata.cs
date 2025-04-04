namespace DxvkVersionManager.Models;

using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

public partial class GameMetadata : ObservableObject
{
    private string _appId = string.Empty;
    private string _name = string.Empty;
    private string _pageName = string.Empty;
    private string _coverUrl = string.Empty;
    private string? _localCoverPath;
    private string _direct3dVersions = "Unknown";
    private bool _executable32bit;
    private bool _executable64bit;
    private string? _vulkanVersions;
    private string _installDir = string.Empty;
    private bool _customD3d;
    private bool _customExec;
    private bool _supportsVulkan;
    private bool _d3dVersionAutoDetected;
    private bool _architectureAutoDetected;
    private string _detectionMethod = string.Empty;
    private string _detailedDetectionInfo = string.Empty;
    private List<string>? _availableDirect3dVersions;
    private bool _supportsMultipleDirect3dVersions;
    private List<string>? _officialDirectXVersions;
    private bool _hasWikiInformation;
    private string? _targetExecutablePath; // Path to the primary executable detected for DXVK installation

    public GameMetadata()
    {
        // Initialize HasCompleteInfo
        UpdateHasCompleteInfo();
    }

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

    public string PageName
    {
        get => _pageName;
        set => SetProperty(ref _pageName, value);
    }

    public string CoverUrl
    {
        get => _coverUrl;
        set => SetProperty(ref _coverUrl, value);
    }

    public string? LocalCoverPath
    {
        get => _localCoverPath;
        set => SetProperty(ref _localCoverPath, value);
    }

    public string Direct3dVersions
    {
        get => _direct3dVersions;
        set
        {
            if (SetProperty(ref _direct3dVersions, value))
            {
                UpdateHasCompleteInfo();
            }
        }
    }

    public bool Executable32bit
    {
        get => _executable32bit;
        set
        {
            if (SetProperty(ref _executable32bit, value))
            {
                UpdateHasCompleteInfo();
            }
        }
    }

    public bool Executable64bit
    {
        get => _executable64bit;
        set
        {
            if (SetProperty(ref _executable64bit, value))
            {
                UpdateHasCompleteInfo();
            }
        }
    }

    public string? VulkanVersions
    {
        get => _vulkanVersions;
        set => SetProperty(ref _vulkanVersions, value);
    }

    public string InstallDir
    {
        get => _installDir;
        set => SetProperty(ref _installDir, value);
    }

    public bool CustomD3d
    {
        get => _customD3d;
        set => SetProperty(ref _customD3d, value);
    }

    public bool CustomExec
    {
        get => _customExec;
        set => SetProperty(ref _customExec, value);
    }
    
    // Flags to track if auto-detection was successful
    public bool D3dVersionAutoDetected
    {
        get => _d3dVersionAutoDetected;
        set => SetProperty(ref _d3dVersionAutoDetected, value);
    }
    
    public bool ArchitectureAutoDetected
    {
        get => _architectureAutoDetected;
        set => SetProperty(ref _architectureAutoDetected, value);
    }
    
    // Detection method details
    public string DetectionMethod
    {
        get => _detectionMethod;
        set => SetProperty(ref _detectionMethod, value);
    }
    
    // Detailed detection information with list of DLLs found in specific executables
    public string DetailedDetectionInfo
    {
        get => _detailedDetectionInfo;
        set => SetProperty(ref _detailedDetectionInfo, value);
    }
    
    // Helper property to determine if we have complete information for DXVK management
    private bool _hasCompleteInfo;
    public bool HasCompleteInfo
    {
        get => _hasCompleteInfo;
        set => SetProperty(ref _hasCompleteInfo, value);
    }
    
    // Helper method to update HasCompleteInfo property
    private void UpdateHasCompleteInfo()
    {
        HasCompleteInfo = (Executable32bit || Executable64bit);
    }
    
    // Helper property to determine if the game supports Vulkan natively
    public bool SupportsVulkan
    {
        get => _supportsVulkan || (!string.IsNullOrEmpty(VulkanVersions) && VulkanVersions != "Unknown");
        set => SetProperty(ref _supportsVulkan, value);
    }
    
    // Collection of available Direct3D versions detected in the game
    public List<string>? AvailableDirect3dVersions
    {
        get => _availableDirect3dVersions;
        set => SetProperty(ref _availableDirect3dVersions, value);
    }
    
    // Flag indicating if the game supports multiple DirectX versions
    public bool SupportsMultipleDirect3dVersions
    {
        get => _supportsMultipleDirect3dVersions;
        set => SetProperty(ref _supportsMultipleDirect3dVersions, value);
    }
    
    // Collection of officially supported DirectX versions from PCGamingWiki
    public List<string>? OfficialDirectXVersions
    {
        get => _officialDirectXVersions;
        set => SetProperty(ref _officialDirectXVersions, value);
    }
    
    // Flag indicating if wiki information has been retrieved
    public bool HasWikiInformation
    {
        get => _hasWikiInformation;
        set => SetProperty(ref _hasWikiInformation, value);
    }
    
    // Path to the primary executable detected for DXVK installation
    public string? TargetExecutablePath
    {
        get => _targetExecutablePath;
        set => SetProperty(ref _targetExecutablePath, value);
    }
}