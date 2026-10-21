using NovaLauncher.Core.Configuration;
using NovaLauncher.Core.Models;
using NovaLauncher.Core.Util;
using Newtonsoft.Json.Linq;

namespace NovaLauncher.Core.Services;

/// <summary>官方版本清单获取。</summary>
public sealed class ManifestService
{
    private readonly string _mirror;
    private VersionManifest? _cache;

    public ManifestService(string mirror) => _mirror = mirror;

    /// <summary>获取版本清单（带缓存）。</summary>
    public async Task<VersionManifest> GetManifestAsync(CancellationToken ct = default)
    {
        if (_cache != null) return _cache;
        var url = DownloadMirror.ManifestUrl(_mirror);
        var json = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
        _cache = JObject.Parse(json).ToObject<VersionManifest>() ?? new VersionManifest();
        return _cache;
    }

    /// <summary>按版本 id 查找版本详情 URL。</summary>
    public async Task<string?> FindVersionUrlAsync(string id, CancellationToken ct = default)
    {
        var manifest = await GetManifestAsync(ct).ConfigureAwait(false);
        return manifest.Versions.FirstOrDefault(v =>
            string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase))?.Url;
    }
}
