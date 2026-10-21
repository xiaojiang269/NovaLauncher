namespace NovaLauncher.Core.Models;

/// <summary>下载任务（下载中心展示与进度）。</summary>
public sealed class DownloadItem
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string DestPath { get; set; } = "";
    public string? Sha1 { get; set; }
    public long Size { get; set; }
    public bool Done { get; set; }
    public string State { get; set; } = "待下载";

    public string SizeText => FormatSize(Size);
    public static string FormatSize(long size)
    {
        if (size <= 0) return "未知";
        if (size < 1024) return $"{size} B";
        if (size < 1024 * 1024) return $"{size / 1024.0:0.0} KB";
        if (size < 1024L * 1024 * 1024) return $"{size / (1024.0 * 1024):0.0} MB";
        return $"{size / (1024.0 * 1024 * 1024):0.0} GB";
    }
}
