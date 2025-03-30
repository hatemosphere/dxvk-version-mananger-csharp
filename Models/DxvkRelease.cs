using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace DxvkVersionManager.Models;

public partial class DxvkRelease : ObservableObject
{
    [JsonProperty("tag_name")]
    [ObservableProperty]
    private string _version = string.Empty;
    
    [JsonProperty("published_at")]
    [ObservableProperty]
    private DateTime _date = DateTime.MinValue;
    
    [ObservableProperty]
    private string _downloadUrl = string.Empty;
    
    [ObservableProperty]
    private string _type = string.Empty;
    
    [ObservableProperty]
    private bool _isDownloaded;

    [JsonProperty("assets")]
    public List<GitHubAsset> Assets { get; set; } = new();
}

public class GitHubAsset
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;
}