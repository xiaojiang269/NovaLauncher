namespace NovaLauncher.Core.Services;

/// <summary>下载进度报告。</summary>
public sealed class DownloadProgress
{
    public int DoneItems { get; set; }
    public int TotalItems { get; set; }
    public long DoneBytes { get; set; }
    public long TotalBytes { get; set; }
    public string CurrentFile { get; set; } = "";
    public string Phase { get; set; } = "";
}

/// <summary>下载汇总。</summary>
public sealed class DownloadSummary
{
    public int Downloaded { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; } = new();
    public bool Success => Failed == 0;
}
