using System.IO.Compression;
using NovaLauncher.Core.Util;

namespace NovaLauncher.Core.Services;

/// <summary>
/// EasyTier 联机组件的自动下载与安装（陶瓦联机底层）。
/// 下载对应平台的 zip 到游戏目录 tools/ 下并解压，供 LanPlayService 使用。
/// 下载源：GitHub EasyTier 官方发布直链。
/// </summary>
public sealed class EasyTierInstaller
{
    public const string Version = "v2.6.4";

    private static readonly HttpClient Client = Http.Get();

    /// <summary>当前平台对应的资产文件名，如 easytier-linux-x86_64-v2.6.4.zip。</summary>
    public static string AssetName(string version = Version)
    {
        var os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                   == System.Runtime.InteropServices.Architecture.Arm64 ? "aarch64" : "x86_64";
        return $"easytier-{os}-{arch}-{version}.zip";
    }

    public static string DownloadUrl(string version = Version) =>
        $"https://github.com/EasyTier/EasyTier/releases/download/{version}/{AssetName(version)}";

    /// <summary>从 GitHub API 查询 EasyTier 最新发布版本（如 v2.6.4）；失败返回 null。</summary>
    public static async Task<string?> GetLatestVersionAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await Client.GetAsync("https://api.github.com/repos/EasyTier/EasyTier/releases/latest", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        }
        catch { return null; }
    }

    /// <summary>下载并解压安装指定版本到 toolsDir/easytier/，返回 easytier-core 可执行路径。</summary>
    public async Task<string?> InstallAsync(string toolsDir, IProgress<(long done, long total)>? progress, CancellationToken ct = default, string version = Version)
    {
        Directory.CreateDirectory(toolsDir);
        var zip = Path.Combine(toolsDir, AssetName(version));
        var destRoot = Path.Combine(toolsDir, "easytier");
        try
        {
            await Http.DownloadAsync(DownloadUrl(version), zip, progress, ct).ConfigureAwait(false);
            // 清空旧解压目录
            if (Directory.Exists(destRoot)) Directory.Delete(destRoot, true);
            ZipFile.ExtractToDirectory(zip, destRoot);
            return FindBinRecursive(destRoot) ?? LanPlayService.FindBackend(toolsDir)?.Path;
        }
        finally
        {
            try { if (File.Exists(zip)) File.Delete(zip); } catch { }
        }
    }

    private static string? FindBinRecursive(string root)
    {
        if (!Directory.Exists(root)) return null;
        var exe = OperatingSystem.IsWindows() ? "easytier-core.exe" : "easytier-core";
        var direct = Path.Combine(root, exe);
        if (File.Exists(direct)) return direct;
        foreach (var dir in Directory.GetDirectories(root))
        {
            var found = FindBinRecursive(dir);
            if (found != null) return found;
        }
        return null;
    }
}
