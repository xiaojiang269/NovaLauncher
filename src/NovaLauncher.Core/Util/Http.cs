using System.Net;
using System.Net.Http.Headers;

namespace NovaLauncher.Core.Util;

/// <summary>
/// HTTP 工具：提供全局 HttpClient，支持下载进度回调与 SHA-1 校验下载。
/// </summary>
public static class Http
{
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = true,
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NovaLauncher/1.0");
        return client;
    }

    public static HttpClient Get() => Client;

    /// <summary>下载字节数组。</summary>
    public static async Task<byte[]> GetBytesAsync(string url, CancellationToken ct = default)
    {
        using var response = await Client.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    /// <summary>GET 字符串。</summary>
    public static async Task<string> GetStringAsync(string url, CancellationToken ct = default)
    {
        using var response = await Client.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>带进度回调的下载。</summary>
    public static async Task DownloadAsync(string url, string destPath, IProgress<(long done, long total)>? progress = null, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = destPath + ".part";

        using (var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0;
            await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var target = File.Create(tmp);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                progress?.Report((done, total));
            }
        }

        if (File.Exists(destPath)) File.Delete(destPath);
        File.Move(tmp, destPath);
    }

    /// <summary>下载并校验 SHA-1，返回是否成功。</summary>
    public static async Task<bool> DownloadWithHashAsync(string url, string destPath, string? sha1, IProgress<(long done, long total)>? progress = null, CancellationToken ct = default)
    {
        await DownloadAsync(url, destPath, progress, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(sha1)) return true;
        return string.Equals(HashUtil.Sha1OfFile(destPath), sha1, StringComparison.OrdinalIgnoreCase);
    }
}
