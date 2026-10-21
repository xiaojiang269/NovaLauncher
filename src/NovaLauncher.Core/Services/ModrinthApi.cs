using NovaLauncher.Core.Models;
using NovaLauncher.Core.Util;
using Newtonsoft.Json.Linq;

namespace NovaLauncher.Core.Services;

/// <summary>
/// Modrinth 开放 API 客户端：搜索模组、获取发布版本、下载文件。
/// 无需 API Key，覆盖绝大多数主流模组（含大量 MC百科收录模组）。
/// 文档：https://docs.modrinth.com
/// </summary>
public sealed class ModrinthApi
{
    private const string Base = "https://api.modrinth.com/v2";
    private static readonly HttpClient Client = Http.Get();

    /// <summary>搜索模组。projectType: mod / shader / resourcepack / modpack；loader: fabric / forge / quilt / neoforge / 空为不限。</summary>
    public async Task<List<ModrinthProject>> SearchAsync(string query, string? gameVersion, string? loader, string? projectType = "mod", int limit = 60, CancellationToken ct = default)
    {
        var facets = new List<string[]>();
        if (!string.IsNullOrWhiteSpace(projectType)) facets.Add(new[] { $"project_type:{projectType.Trim()}" });
        if (!string.IsNullOrWhiteSpace(gameVersion)) facets.Add(new[] { $"versions:{gameVersion.Trim()}" });
        if (!string.IsNullOrWhiteSpace(loader)) facets.Add(new[] { $"categories:{loader.Trim()}" });

        var url = $"{Base}/search?query={Uri.EscapeDataString(query.Trim())}&limit={limit}&index=relevance";
        if (facets.Count > 0)
            url += $"&facets={Uri.EscapeDataString(JArray.FromObject(facets).ToString(Newtonsoft.Json.Formatting.None))}";

        using var resp = await Client.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var root = JObject.Parse(json);
        var hits = root["hits"] as JArray ?? new JArray();

        var list = new List<ModrinthProject>();
        foreach (var h in hits.OfType<JObject>())
        {
            list.Add(new ModrinthProject
            {
                ProjectId = h["project_id"]?.ToString() ?? "",
                Slug = h["slug"]?.ToString() ?? "",
                Title = h["title"]?.ToString() ?? "未知",
                Description = h["description"]?.ToString() ?? "",
                Downloads = h["downloads"]?.ToObject<int>() ?? 0,
                IconUrl = h["icon_url"]?.ToString(),
                Categories = h["categories"]?.ToObject<string[]>() ?? Array.Empty<string>(),
                Versions = h["versions"]?.ToObject<string[]>() ?? Array.Empty<string>(),
            });
        }
        return list;
    }

    /// <summary>获取模组可用于指定游戏版本/加载器的发布版本。</summary>
    public async Task<List<ModrinthVersion>> GetVersionsAsync(string projectIdOrSlug, string? gameVersion, string? loader, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(gameVersion))
            query.Add($"game_versions={Uri.EscapeDataString($"[{Quote(gameVersion.Trim())}]")}");
        if (!string.IsNullOrWhiteSpace(loader))
            query.Add($"loaders={Uri.EscapeDataString($"[{Quote(loader.Trim())}]")}");
        var qs = query.Count > 0 ? "?" + string.Join("&", query) : "";

        var url = $"{Base}/project/{Uri.EscapeDataString(projectIdOrSlug)}/version{qs}";
        using var resp = await Client.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var arr = JArray.Parse(json);

        var list = new List<ModrinthVersion>();
        foreach (var v in arr.OfType<JObject>())
        {
            var files = new List<ModrinthFile>();
            foreach (var f in (v["files"] as JArray ?? new JArray()).OfType<JObject>())
            {
                files.Add(new ModrinthFile
                {
                    Url = f["url"]?.ToString() ?? "",
                    Filename = f["filename"]?.ToString() ?? "",
                    Size = f["size"]?.ToObject<long>() ?? 0,
                    Primary = f["primary"]?.ToObject<bool>() ?? false,
                });
            }
            list.Add(new ModrinthVersion
            {
                Id = v["id"]?.ToString() ?? "",
                Name = v["name"]?.ToString() ?? "",
                VersionNumber = v["version_number"]?.ToString() ?? "",
                GameVersions = v["game_versions"]?.ToObject<string[]>() ?? Array.Empty<string>(),
                Loaders = v["loaders"]?.ToObject<string[]>() ?? Array.Empty<string>(),
                Files = files,
            });
        }
        return list;
    }

    /// <summary>下载模组文件到目标路径。</summary>
    public async Task DownloadAsync(string fileUrl, string destPath, IProgress<(long done, long total)>? progress, CancellationToken ct = default)
    {
        await Http.DownloadAsync(fileUrl, destPath, progress, ct).ConfigureAwait(false);
    }

    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
