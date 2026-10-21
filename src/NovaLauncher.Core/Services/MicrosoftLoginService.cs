using System.Text;
using System.Text.Json;
using NovaLauncher.Core.Models;
using NovaLauncher.Core.Util;

namespace NovaLauncher.Core.Services;

/// <summary>
/// 微软账号登录（Device Code 流程）：
/// Microsoft OAuth → Xbox Live → XSTS → Minecraft 登录 → 获取游戏档案。
/// </summary>
public sealed class MicrosoftLoginService
{
    private readonly HttpClient _http = Http.Get();

    /// <summary>使用 Device Code 流程登录。通过 report 回调向用户展示需要输入的代码与验证链接。</summary>
    public async Task<AccountInfo?> LoginAsync(string clientId, IProgress<(string userCode, string verifyUri)>? report, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(clientId))
            throw new InvalidOperationException("未配置 MicrosoftClientId，无法进行微软登录。");

        // 1. 请求 device code
        using var codeResp = await _http.PostAsync(
            "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["scope"] = "XboxLive.signin offline_access",
            }), ct).ConfigureAwait(false);
        codeResp.EnsureSuccessStatusCode();
        using var codeJson = JsonDocument.Parse(await codeResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var deviceCode = codeJson.RootElement.GetProperty("device_code").GetString()!;
        var userCode = codeJson.RootElement.GetProperty("user_code").GetString()!;
        var verifyUri = codeJson.RootElement.GetProperty("verification_uri").GetString()!;
        var interval = codeJson.RootElement.TryGetProperty("interval", out var iv) ? iv.GetInt32() : 5;

        report?.Report((userCode, verifyUri));

        // 2. 轮询 token
        string accessToken = "";
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(interval * 1000, ct).ConfigureAwait(false);
            using var tokenResp = await _http.PostAsync(
                "https://login.microsoftonline.com/consumers/oauth2/v2.0/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    ["client_id"] = clientId,
                    ["device_code"] = deviceCode,
                }), ct).ConfigureAwait(false);
            var body = await tokenResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (tokenResp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(body);
                accessToken = doc.RootElement.GetProperty("access_token").GetString()!;
                break;
            }
            using var errDoc = JsonDocument.Parse(body);
            var err = errDoc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : "";
            if (err == "authorization_pending" || err == "slow_down") continue;
            if (err == "expired_token" || err == "authorization_declined") throw new InvalidOperationException("微软登录已过期或已取消。");
        }

        // 3. Xbox Live 认证
        var xblToken = await XboxLiveAuthAsync(accessToken, ct).ConfigureAwait(false);
        // 4. XSTS
        var xsts = await XstsAuthAsync(xblToken, ct).ConfigureAwait(false);
        // 5. Minecraft 登录
        var mcToken = await MinecraftLoginAsync(xsts.uhs, xsts.token, ct).ConfigureAwait(false);
        // 6. 档案
        return await FetchProfileAsync(mcToken, ct).ConfigureAwait(false);
    }

    private static async Task<string> XboxLiveAuthAsync(string accessToken, CancellationToken ct)
    {
        using var resp = await Http.Get().PostAsync("https://user.auth.xboxlive.com/user/authenticate",
            JsonContent(new
            {
                Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = "d=" + accessToken },
                RelyingParty = "http://auth.xboxlive.com",
                TokenType = "JWT",
            }), ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        return doc.RootElement.GetProperty("Token").GetString()!;
    }

    private static async Task<(string token, string uhs)> XstsAuthAsync(string xblToken, CancellationToken ct)
    {
        using var resp = await Http.Get().PostAsync("https://xsts.auth.xboxlive.com/xsts/authorize",
            JsonContent(new
            {
                Properties = new { SandboxId = "RETAIL", UserTokens = new[] { xblToken } },
                RelyingParty = "rp://api.minecraftservices.com/",
                TokenType = "JWT",
            }), ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            // 常见错误：未注册 XBox 账号 / 未成年人受限
            using var errDoc = JsonDocument.Parse(body);
            var errCode = errDoc.RootElement.TryGetProperty("XErr", out var xe) ? xe.GetInt64() : 0;
            var msg = errCode switch
            {
                2148916233 => "此账号尚未注册 Xbox Live，请先在 xbox.com 注册。",
                2148916238 => "此账号受未成年人保护限制，无法登录。",
                _ => "Xbox 身份验证失败。",
            };
            throw new InvalidOperationException(msg);
        }
        using var doc = JsonDocument.Parse(body);
        var token = doc.RootElement.GetProperty("Token").GetString()!;
        var uhs = doc.RootElement.GetProperty("DisplayClaims").GetProperty("xui")[0].GetProperty("uhs").GetString()!;
        return (token, uhs);
    }

    private static async Task<string> MinecraftLoginAsync(string uhs, string xstsToken, CancellationToken ct)
    {
        using var resp = await Http.Get().PostAsync("https://api.minecraftservices.com/authentication/login_with_xbox",
            JsonContent(new { identityToken = $"XBL3.0 x={uhs};{xstsToken}" }), ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    private static async Task<AccountInfo?> FetchProfileAsync(string mcToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", mcToken);
        using var resp = await Http.Get().SendAsync(req, ct).ConfigureAwait(false);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // 无 Minecraft
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        return new AccountInfo
        {
            Name = doc.RootElement.GetProperty("name").GetString()!,
            Uuid = doc.RootElement.GetProperty("id").GetString()!,
            Type = "microsoft",
            AccessToken = mcToken,
        };
    }

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
}
