using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NovaLauncher.Core.Models;
using NovaLauncher.Core.Util;

namespace NovaLauncher.Core.Services;

/// <summary>
/// 第三方认证服务器登录（外置登录 / authlib-injector）。
/// 通过标准 Yggdrasil authenticate 接口在第三方服务器上换取账号凭证。
/// </summary>
public sealed class ExternalLoginService
{
    private readonly HttpClient _http = Http.Get();

    /// <summary>在第三方认证服务器上登录。</summary>
    /// <param name="api">认证服务器 API 地址，如 https://littleskin.cn/api/yggdrasil。</param>
    public async Task<AccountInfo> LoginAsync(string api, string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(api) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("认证服务器地址、账号、密码均不能为空");

        var baseUrl = api.TrimEnd('/');
        var payload = new
        {
            username,
            password,
            clientToken = Guid.NewGuid().ToString("N"),
            requestUser = true,
        };
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync($"{baseUrl}/authserver/authenticate", content, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            var hint = ExtractErrorHint(body);
            throw new InvalidOperationException($"第三方登录失败（HTTP {(int)resp.StatusCode}）{hint}。请检查服务器地址、账号密码。");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var accessToken = root.GetProperty("accessToken").GetString() ?? "";
        if (!root.TryGetProperty("selectedProfile", out var profile))
            throw new InvalidOperationException("该账号没有可用于游戏的档案（selectedProfile）。");

        var id = profile.GetProperty("id").GetString() ?? "";
        var name = profile.GetProperty("name").GetString() ?? "";

        return new AccountInfo
        {
            Name = name,
            Uuid = id,
            Type = "external",
            AccessToken = accessToken,
            ExternalServer = api.Trim(),
        };
    }

    private static string ExtractErrorHint(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errorMessage", out var em))
            {
                var m = em.GetString();
                if (!string.IsNullOrEmpty(m)) return $"（{m}）";
            }
        }
        catch { }
        return "";
    }
}

/// <summary>authlib-injector 运行时下载。</summary>
public static class AuthlibInjectorService
{
    private const string LatestApi = "https://api.github.com/repos/yushijinhun/authlib-injector/releases/latest";
    // GitHub /releases/latest/download/ 需精确资产名，故回退时直接用已知最新版完整下载链接
    private const string LatestDirectUrl =
        "https://github.com/yushijinhun/authlib-injector/releases/download/v1.2.8/authlib-injector-1.2.8.jar";

    /// <summary>获取最新版 authlib-injector jar 的下载 URL（API 失败时回退到 latest 下载直链）。</summary>
    public static async Task<string> GetLatestDownloadUrlAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, LatestApi);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            req.Headers.UserAgent.ParseAdd("NovaLauncher/1.0");
            using var resp = await Http.Get().SendAsync(req, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var assets = doc.RootElement.GetProperty("assets");
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    return asset.GetProperty("browser_download_url").GetString() ?? "";
            }
            throw new InvalidOperationException("未在最新 release 中找到 jar 资产。");
        }
        catch (Exception)
        {
            // GitHub API 限流或不可用：使用 latest 下载直链（Http 会自动跟随重定向）
            return LatestDirectUrl;
        }
    }

    /// <summary>下载 authlib-injector 到指定路径（跳过已存在且非空的）。</summary>
    public static async Task<string> EnsureDownloadedAsync(string destPath, CancellationToken ct = default)
    {
        if (File.Exists(destPath) && new FileInfo(destPath).Length > 0)
            return destPath;
        var url = await GetLatestDownloadUrlAsync(ct).ConfigureAwait(false);
        await Http.DownloadAsync(url, destPath, null, ct).ConfigureAwait(false);
        return destPath;
    }
}
