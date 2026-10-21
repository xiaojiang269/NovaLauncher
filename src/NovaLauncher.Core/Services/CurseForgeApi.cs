using NovaLauncher.Core.Models;
using NovaLauncher.Core.Util;
using Newtonsoft.Json.Linq;

namespace NovaLauncher.Core.Services;

/// <summary>搜索结果统一模型（Modrinth 与 CurseForge 共用）。</summary>
public sealed class ModSearchItem : Models.ObservableObject
{
    public string Source { get; set; } = "modrinth";
    public string ModId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public int Downloads { get; set; }
    public string? IconUrl { get; set; }
    public string[] Categories { get; set; } = Array.Empty<string>();

    private bool _downloaded;
    /// <summary>是否已下载到目标版本（UI 打勾）。</summary>
    public bool Downloaded
    {
        get => _downloaded;
        set => SetProperty(ref _downloaded, value);
    }
}

/// <summary>模组可下载版本。</summary>
public sealed class ModFileVersion
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string VersionNumber { get; set; } = "";
    public string[] GameVersions { get; set; } = Array.Empty<string>();
    public string[] Loaders { get; set; } = Array.Empty<string>();
    public string DownloadUrl { get; set; } = "";
    public string FileName { get; set; } = "";
    public long Size { get; set; }
}

/// <summary>
/// CurseForge 开放 API（需 API key）。覆盖大量 Modrinth 未收录的 Forge / 老版本模组。
/// 文档：https://docs.curseforge.com
/// </summary>
public sealed class CurseForgeApi
{
    private const string Base = "https://api.curseforge.com/v1";
    private const int MinecraftGameId = 432;   // Minecraft

    private static readonly HttpClient Client = Http.Get();

    /// <summary>搜索模组。classId: 6=Mods 6552=Shader 12=ResourcePacks；loader: forge / fabric / quilt / neoforge / 空为不限。</summary>
    public async Task<List<ModSearchItem>> SearchAsync(string apiKey, string query, string? gameVersion, string? loader, int classId = 6, int limit = 50, CancellationToken ct = default)
    {
        var url = $"{Base}/mods/search?gameId={MinecraftGameId}&classId={classId}&searchFilter={Uri.EscapeDataString(query)}&pageSize={limit}&sortField=1&sortOrder=desc";
        if (!string.IsNullOrWhiteSpace(gameVersion)) url += $"&gameVersion={Uri.EscapeDataString(gameVersion.Trim())}";
        if (!string.IsNullOrWhiteSpace(loader)) url += $"&modLoaderType={LoaderType(loader)}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("x-api-key", apiKey);
        using var resp = await Client.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var data = JObject.Parse(json)["data"] as JArray ?? new JArray();

        var list = new List<ModSearchItem>();
        foreach (var m in data.OfType<JObject>())
        {
            list.Add(new ModSearchItem
            {
                Source = "curseforge",
                ModId = m["id"]?.ToString() ?? "",
                Title = m["name"]?.ToString() ?? "未知",
                Description = m["summary"]?.ToString() ?? "",
                Downloads = m["downloadCount"]?.ToObject<int>() ?? 0,
                IconUrl = m["logo"]?["thumbnailUrl"]?.ToString(),
                Categories = (m["categories"] as JArray)?.Select(c => c["name"]?.ToString() ?? "").Where(s => s != "").ToArray() ?? Array.Empty<string>(),
            });
        }
        return list;
    }

    /// <summary>获取模组的可用文件（下载版本）。</summary>
    public async Task<List<ModFileVersion>> GetFilesAsync(string apiKey, string modId, string? gameVersion, string? loader, CancellationToken ct = default)
    {
        var url = $"{Base}/mods/{modId}/files?gameVersionTypeId=0&pageSize=50";
        if (!string.IsNullOrWhiteSpace(gameVersion)) url += $"&gameVersion={Uri.EscapeDataString(gameVersion.Trim())}";
        if (!string.IsNullOrWhiteSpace(loader)) url += $"&modLoaderType={LoaderType(loader)}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("x-api-key", apiKey);
        using var resp = await Client.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var data = JObject.Parse(json)["data"] as JArray ?? new JArray();

        var list = new List<ModFileVersion>();
        foreach (var f in data.OfType<JObject>())
        {
            list.Add(new ModFileVersion
            {
                Id = f["id"]?.ToString() ?? "",
                Name = f["displayName"]?.ToString() ?? "",
                VersionNumber = f["fileName"]?.ToString() ?? "",
                GameVersions = f["gameVersions"]?.ToObject<string[]>() ?? Array.Empty<string>(),
                Loaders = (f["modLoaders"] as JArray)?.Select(l => l["name"]?.ToString() ?? "").ToArray() ?? Array.Empty<string>(),
                DownloadUrl = f["downloadUrl"]?.ToString() ?? "",
                FileName = f["fileName"]?.ToString() ?? "",
                Size = f["fileLength"]?.ToObject<long>() ?? 0,
            });
        }
        return list;
    }

    /// <summary>下载模组文件（无 downloadUrl 时走 CF API download 端点）。</summary>
    public async Task DownloadAsync(string apiKey, string modId, ModFileVersion file, string destPath, CancellationToken ct = default)
    {
        var url = string.IsNullOrEmpty(file.DownloadUrl)
            ? $"{Base}/mods/{modId}/files/{file.Id}/download"
            : file.DownloadUrl;
        if (!string.IsNullOrEmpty(file.DownloadUrl))
        {
            await Http.DownloadAsync(url, destPath, null, ct).ConfigureAwait(false);
            return;
        }
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("x-api-key", apiKey);
        using var resp = await Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? 0;
        await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        await using var dst = File.Create(destPath);
        await src.CopyToAsync(dst, ct).ConfigureAwait(false);
    }

    private static int LoaderType(string loader) => loader.ToLowerInvariant() switch
    {
        "forge" => 1,
        "fabric" => 4,
        "quilt" => 5,
        "neoforge" => 6,
        _ => 0, // Any
    };
}
