using NovaLauncher.Core.Configuration;
using NovaLauncher.Core.Models;
using NovaLauncher.Core.Util;
using Newtonsoft.Json.Linq;

namespace NovaLauncher.Core.Services;

/// <summary>版本获取与本地存储。</summary>
public sealed class VersionService
{
    private readonly GameDir _dir;
    private readonly string _mirror;

    public VersionService(LauncherConfig config, string mirror)
    {
        _dir = new GameDir(config);
        _mirror = mirror;
    }

    public GameDir Dir => _dir;

    /// <summary>加载本地版本 json。</summary>
    public MinecraftVersion? LoadLocal(string id)
    {
        var path = _dir.VersionJson(id);
        if (!File.Exists(path)) return null;
        try
        {
            var version = VersionJsonParser.Parse(File.ReadAllText(path), id);
            version.ClientDownloaded = File.Exists(_dir.VersionJar(id));
            return version;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>从网络获取版本 json 并保存到本地（镜像重写 URL）。</summary>
    public async Task<MinecraftVersion> FetchRemoteAsync(string id, string? jsonUrl, CancellationToken ct = default)
    {
        // 若未提供 URL，从清单查询
        if (string.IsNullOrEmpty(jsonUrl))
        {
            var manifest = new ManifestService(_mirror);
            jsonUrl = await manifest.FindVersionUrlAsync(id, ct).ConfigureAwait(false);
        }
        if (string.IsNullOrEmpty(jsonUrl))
            throw new InvalidOperationException($"无法在版本清单中找到版本 {id}");

        var url = DownloadMirror.Rewrite(jsonUrl, _mirror);
        var json = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
        var version = VersionJsonParser.Parse(json, id);

        var dir = _dir.VersionDir(id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(_dir.VersionJson(id), json);
        return version;
    }

    /// <summary>判断客户端 jar 是否已就绪。</summary>
    public bool IsClientJarReady(string id) => File.Exists(_dir.VersionJar(id));
}
