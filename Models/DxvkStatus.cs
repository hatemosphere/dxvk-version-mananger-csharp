namespace DxvkVersionManager.Models;

public class DxvkStatus
{
    public bool Patched { get; set; }
    public bool Backuped { get; set; }
    public string? DxvkVersion { get; set; }
    public string? DxvkType { get; set; }
    public DateTime? DxvkTimestamp { get; set; }
}