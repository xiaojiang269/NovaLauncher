namespace NovaLauncher.Core.Configuration;

/// <summary>
/// 下载源策略：仅允许官方海外源下载。
/// 因法律合规原因，本启动器已关闭大陆（境内加速镜像）下载功能，强制使用 Mojang 官方海外源。
/// </summary>
public static class DownloadMirror
{
    public const string Official = "official";
    public const string Bmclapi = "bmclapi";

    /// <summary>大陆下载源被禁用的提示语。</summary>
    public const string BannedNotice = "因为法律原因，本启动器已关闭大陆下载功能，仅支持官方海外源下载。";

    /// <summary>版本清单地址：固定使用官方海外源。</summary>
    public static string ManifestUrl(string mirror) =>
        "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    /// <summary>重写下载 URL：一律保留官方海外地址，忽略大陆镜像。</summary>
    public static string Rewrite(string url, string mirror) => url;
}
