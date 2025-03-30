namespace DxvkVersionManager.Models;

public class InstalledVersions
{
    public List<string> Dxvk { get; set; } = new();
    public List<string> DxvkGplasync { get; set; } = new();
}