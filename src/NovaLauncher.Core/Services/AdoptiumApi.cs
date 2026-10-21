using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using NovaLauncher.Core.Util;

namespace NovaLauncher.Core.Services;

/// <summary>从 Adoptium（Eclipse Temurin）下载并安装 JDK，供内置 Java 使用。</summary>
public sealed class AdoptiumApi
{
    private const string Base = "https://api.adoptium.net/v3";
    private static readonly HttpClient Client = Http.Get();

    /// <summary>JDK 下载信息。</summary>
    public sealed record JdkInfo(string Url, string Name, long Size);

    /// <summary>获取指定大版本 JDK 的下载信息（当前平台）。</summary>
    public async Task<JdkInfo?> GetLatestAsync(int major, CancellationToken ct = default)
    {
        var os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "mac" : "linux";
        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                   == System.Runtime.InteropServices.Architecture.Arm64 ? "aarch64" : "x64";

        var url = $"{Base}/assets/latest/{major}/hotspot?os={os}&arch={arch}&image_type=jdk&vendor=eclipse";
        using var resp = await Client.GetAsync(url, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return null;
        var first = root[0];
        var binary = first.GetProperty("binary");
        var pkg = binary.GetProperty("package");
        return new JdkInfo(
            pkg.GetProperty("link").GetString() ?? "",
            pkg.GetProperty("name").GetString() ?? "",
            pkg.TryGetProperty("size", out var s) ? s.GetInt64() : 0);
    }

    /// <summary>下载并解压 JDK 到 {javaDir}/{major}/，返回 bin/java 路径。</summary>
    public async Task<string?> InstallAsync(int major, string javaDir, IProgress<(long done, long total)>? progress, CancellationToken ct = default)
    {
        var info = await GetLatestAsync(major, ct).ConfigureAwait(false);
        if (info == null || string.IsNullOrEmpty(info.Url)) return null;

        var destRoot = Path.Combine(javaDir, major.ToString());
        Directory.CreateDirectory(destRoot);

        // 已安装则直接返回
        var existing = FindJavaBin(destRoot);
        if (existing != null) return existing;

        var ext = info.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? ".zip" : ".tar.gz";
        var tmp = Path.Combine(javaDir, $"_tmp_{major}{ext}");
        try
        {
            await Http.DownloadAsync(info.Url, tmp, progress, ct).ConfigureAwait(false);
            if (ext == ".zip")
            {
                ZipFile.ExtractToDirectory(tmp, destRoot);
            }
            else
            {
                await using var fs = File.OpenRead(tmp);
                using var gz = new GZipStream(fs, CompressionMode.Decompress);
                await using var tar = new TarReader(gz);
                while (await tar.GetNextEntryAsync().ConfigureAwait(false) is { } entry)
                {
                    if (entry.EntryType is TarEntryType.Directory or TarEntryType.SymbolicLink) continue;
                    var rel = entry.Name.TrimStart('/');
                    if (rel == "") continue;
                    var target = Path.Combine(destRoot, rel);
                    var dir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    await using var outFs = File.Create(target);
                    await using (var src = entry.DataStream)
                    {
                        if (src != null) await src.CopyToAsync(outFs, ct).ConfigureAwait(false);
                    }
                }
            }
            return FindJavaBin(destRoot) ?? throw new InvalidOperationException("JDK 解压后未找到 java 可执行文件");
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    /// <summary>在 {root} 下递归查找 bin/java（或 bin/java.exe）。</summary>
    public static string? FindJavaBin(string root)
    {
        if (!Directory.Exists(root)) return null;
        var exe = OperatingSystem.IsWindows() ? "java.exe" : "java";
        foreach (var dir in Directory.GetDirectories(root))
        {
            var bin = Path.Combine(dir, "bin", exe);
            if (File.Exists(bin)) return bin;
            var sub = FindJavaBin(dir);
            if (sub != null) return sub;
        }
        var directBin = Path.Combine(root, "bin", exe);
        return File.Exists(directBin) ? directBin : null;
    }
}
