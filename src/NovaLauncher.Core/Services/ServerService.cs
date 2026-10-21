using NovaLauncher.Core.Configuration;
using NovaLauncher.Core.Models;
using Newtonsoft.Json;

namespace NovaLauncher.Core.Services;

/// <summary>服务器快捷入口管理（servers.json）。</summary>
public sealed class ServerService
{
    private readonly GameDir _dir;

    public ServerService(LauncherConfig config) => _dir = new GameDir(config);

    public List<ServerEntry> Load()
    {
        if (!File.Exists(_dir.ServersFile)) return new List<ServerEntry>();
        try
        {
            return JsonConvert.DeserializeObject<List<ServerEntry>>(File.ReadAllText(_dir.ServersFile)) ?? new();
        }
        catch
        {
            return new List<ServerEntry>();
        }
    }

    public void Save(List<ServerEntry> servers)
    {
        Directory.CreateDirectory(_dir.Root);
        File.WriteAllText(_dir.ServersFile, JsonConvert.SerializeObject(servers, Formatting.Indented));
    }
}
