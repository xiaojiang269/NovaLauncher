using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLauncher.Core.Configuration;
using NovaLauncher.Core.Services;

namespace NovaLauncher.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty] private string gameDir;
    [ObservableProperty] private string javaPath;
    [ObservableProperty] private int minMemoryMb;
    [ObservableProperty] private int maxMemoryMb;
    [ObservableProperty] private int windowWidth;
    [ObservableProperty] private int windowHeight;
    [ObservableProperty] private bool fullscreen;
    [ObservableProperty] private string extraJvmArgs;
    [ObservableProperty] private string microsoftClientId;
    [ObservableProperty] private string curseForgeApiKey;
    [ObservableProperty] private string status = "";

    public ObservableCollection<string> DownloadMirrors { get; } = new() { "官方海外源", "大陆源（已关闭）" };
    [ObservableProperty] private int downloadMirror;

    partial void OnDownloadMirrorChanged(int value)
    {
        if (value == 1)
        {
            // 大陆源已按法律合规关闭，强制回退到官方海外源
            Status = NovaLauncher.Core.Configuration.DownloadMirror.BannedNotice;
            DownloadMirror = 0;
        }
    }

    public SettingsViewModel()
    {
        var c = AppServices.Config;
        GameDir = c.GameDir;
        JavaPath = c.JavaPath;
        MinMemoryMb = c.MinMemoryMb;
        MaxMemoryMb = c.MaxMemoryMb;
        WindowWidth = c.WindowWidth;
        WindowHeight = c.WindowHeight;
        Fullscreen = c.Fullscreen;
        ExtraJvmArgs = c.ExtraJvmArgs;
        MicrosoftClientId = c.MicrosoftClientId;
        CurseForgeApiKey = c.CurseForgeApiKey;
        DownloadMirror = c.DownloadMirror;
    }

    [RelayCommand]
    private void Save()
    {
        var c = AppServices.Config;
        c.GameDir = GameDir.Trim();
        c.JavaPath = JavaPath.Trim();
        c.MinMemoryMb = Math.Clamp(MinMemoryMb, 512, 32768);
        c.MaxMemoryMb = Math.Clamp(MaxMemoryMb, 512, 65536);
        c.WindowWidth = Math.Max(0, WindowWidth);
        c.WindowHeight = Math.Max(0, WindowHeight);
        c.Fullscreen = Fullscreen;
        c.ExtraJvmArgs = ExtraJvmArgs;
        c.MicrosoftClientId = MicrosoftClientId.Trim();
        c.CurseForgeApiKey = CurseForgeApiKey.Trim();
        // 大陆下载源已按法律合规关闭，始终使用官方海外源
        c.DownloadMirror = 0;
        AppServices.SaveConfig();
        Status = $"已保存到 {c.ConfigFilePath}";
    }

    [RelayCommand]
    private async Task PickJavaAsync()
    {
        var files = await MainWindowRef.PickFilesAsync("*");
        if (files != null && files.Count > 0)
            JavaPath = files[0];
    }

    // ---------- 自动诊断 ----------

    [ObservableProperty] private string diagReport = "点击「运行自动诊断」检查运行环境。";
    [ObservableProperty] private bool isDiagBusy;

    /// <summary>运行自动诊断：检查 Java、目录结构、版本、磁盘与网络。可导出报告。</summary>
    [RelayCommand]
    private async Task RunDiagnosticsAsync()
    {
        IsDiagBusy = true;
        DiagReport = "正在诊断…";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("NovaLauncher 环境诊断报告");
        sb.AppendLine($"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"系统：{System.Environment.OSVersion} · {(System.Environment.Is64BitOperatingSystem ? "64 位" : "32 位")} · {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        sb.AppendLine(new string('-', 48));

        // Java
        sb.AppendLine("[Java]");
        try
        {
            var cfg = AppServices.Config;
            var java = JavaService.Probe(cfg.JavaPath) ??
                       JavaService.FindSystemJava().OrderByDescending(j => j.MajorVersion).FirstOrDefault();
            if (java == null) sb.AppendLine("  未找到 Java（安装 Java 17+ 并设置路径）");
            else sb.AppendLine($"  找到 Java {java.Version}（{java.MajorVersion}）@ {java.Path}");
        }
        catch (Exception ex) { sb.AppendLine("  Java 探测失败：" + ex.Message); }

        // 目录
        sb.AppendLine("[游戏目录]");
        var dir = AppServices.Dir;
        foreach (var (k, p) in new[] { ("根目录", dir.Root), ("版本", dir.Versions), ("资源", dir.Assets), ("库", dir.Libraries), ("整合包", dir.Modpacks) })
            sb.AppendLine($"  {k}: {(Directory.Exists(p) ? "存在" : "缺失")} · {p}");

        // 版本
        var versions = dir.ListInstalledVersions();
        sb.AppendLine($"[本地版本] 共 {versions.Count} 个");
        foreach (var v in versions.Take(12))
            sb.AppendLine($"  {v}{(File.Exists(dir.VersionJar(v)) ? "" : "（缺 jar）")}");

        // 磁盘
        try
        {
            var root = new DirectoryInfo(dir.Root);
            if (root.Exists)
            {
                var drive = new System.IO.DriveInfo(root.Root.FullName);
                sb.AppendLine($"[磁盘] 可用 {drive.AvailableFreeSpace / 1024.0 / 1024 / 1024:0.0} GB / 总 {drive.TotalSize / 1024.0 / 1024 / 1024:0.0} GB");
            }
        }
        catch (Exception ex) { sb.AppendLine("[磁盘] 读取失败：" + ex.Message); }

        // 网络（超时 5s）
        sb.AppendLine("[网络]");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var ok = await hc.GetAsync("https://piston-meta.mojang.com", HttpCompletionOption.ResponseHeadersRead, cts.Token);
            sb.AppendLine($"  Minecraft 官方源可达（{(int)ok.StatusCode}）");
        }
        catch (Exception ex) { sb.AppendLine("  Minecraft 官方源不可达：" + ex.Message); }

        sb.AppendLine(new string('-', 48));
        sb.AppendLine("诊断完成。");
        DiagReport = sb.ToString();
        IsDiagBusy = false;
    }

    /// <summary>把诊断报告复制到剪贴板。</summary>
    [RelayCommand]
    private async Task CopyDiagAsync()
    {
        await MainWindowRef.CopyToClipboardAsync(DiagReport);
        Status = "诊断报告已复制到剪贴板";
    }
}
