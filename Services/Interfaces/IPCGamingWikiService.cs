namespace DxvkVersionManager.Services.Interfaces;

using System.Collections.Generic;
using System.Threading.Tasks;

public interface IPCGamingWikiService
{
    /// <summary>
    /// Gets the official DirectX versions supported by a game according to PCGamingWiki
    /// </summary>
    /// <param name="gameName">The name of the game to look up</param>
    /// <returns>List of supported DirectX versions (e.g. "DirectX 11", "DirectX 12")</returns>
    Task<List<string>> GetSupportedDirectXVersionsAsync(string gameName);
} 