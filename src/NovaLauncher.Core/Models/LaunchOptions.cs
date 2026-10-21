namespace NovaLauncher.Core.Models;

/// <summary>一次启动任务的配置。</summary>
public sealed class LaunchOptions
{
    public string VersionId { get; set; } = "";
    public AccountInfo Account { get; set; } = new();
    public int MinMemoryMb { get; set; } = 1024;
    public int MaxMemoryMb { get; set; } = 4096;
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public bool Fullscreen { get; set; }
    public string ExtraJvmArgs { get; set; } = "";
    /// <summary>启动后自动加入的服务器地址（可选）。</summary>
    public string? QuickConnectServer { get; set; }
}

/// <summary>启动结果。</summary>
public sealed class LaunchResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public System.Diagnostics.Process? Process { get; set; }
    public List<string> Log { get; set; } = new();
}
