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
    [ObservableProperty] private bool versionIsolation;
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
        VersionIsolation = c.VersionIsolation;
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
        c.VersionIsolation = VersionIsolation;
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

    // ---------- 一键游戏修复 ----------

    [ObservableProperty] private string repairReport = "「一键修复」会清理日志/崩溃报告与临时缓存、修复目录结构、检查 Java 与下载源。";
    [ObservableProperty] private bool isRepairBusy;

    /// <summary>一键游戏修复（对齐 PCL2 游戏修复工具）。</summary>
    [RelayCommand]
    private async Task RunRepairAsync()
    {
        IsRepairBusy = true;
        RepairReport = "正在执行一键修复…";
        try
        {
            var result = await new GameRepairer(AppServices.Config).RepairAsync(CancellationToken.None);
            RepairReport = string.Join("\n", result.Report);
            Status = result.Ok ? "一键修复完成，无未处理问题。" : "一键修复完成，存在需要人工处理的问题，见报告。";
        }
        catch (Exception ex)
        {
            RepairReport = "一键修复失败：" + ex.Message;
            Status = "一键修复失败";
        }
        finally
        {
            IsRepairBusy = false;
        }
    }

    /// <summary>复制修复报告。</summary>
    [RelayCommand]
    private async Task CopyRepairAsync()
    {
        await MainWindowRef.CopyToClipboardAsync(RepairReport);
        Status = "修复报告已复制到剪贴板";
    }

    // ---------- 自动更新 ----------

    [ObservableProperty] private string updateStatus = $"当前版本 v{Core.Services.AppVersion.Current}";
    [ObservableProperty] private bool isUpdateBusy;
    [ObservableProperty] private bool showUpdateProgress;
    [ObservableProperty] private double updateProgress;
    [ObservableProperty] private string latestVersion = "";

    /// <summary>检查更新：查询 GitHub 最新 Release。</summary>
    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        IsUpdateBusy = true;
        ShowUpdateProgress = false;
        UpdateStatus = "正在检查更新…";
        try
        {
            var info = await AppServices.Updates.CheckAsync();
            if (info == null)
            {
                UpdateStatus = "无法连接更新服务器（GitHub 不可达或限流）。请稍后重试，或到 GitHub/蓝奏云手动下载最新版。";
                return;
            }
            LatestVersion = info.Version;
            UpdateStatus = info.IsCurrent
                ? $"已是最新版本 v{AppVersion.Current}。"
                : $"发现新版本 v{info.Version}（当前 v{AppVersion.Current}）。" + (info.AssetUrl == null ? "当前平台暂未提供安装包。" : "点击「下载并更新」自动安装。");
        }
        catch (Exception ex)
        {
            UpdateStatus = "检查更新失败：" + ex.Message;
        }
        finally { IsUpdateBusy = false; }
    }

    /// <summary>下载并更新（替换自身后重启）。</summary>
    [RelayCommand]
    private async Task DownloadUpdateAsync()
    {
        if (IsUpdateBusy) return;
        try
        {
            var info = await AppServices.Updates.CheckAsync();
            if (info == null) { UpdateStatus = "无法连接更新服务器，请稍后重试。"; return; }
            if (info.IsCurrent) { UpdateStatus = "已是最新版本。"; return; }
            if (string.IsNullOrEmpty(info.AssetUrl)) { UpdateStatus = "当前平台暂未提供安装包。"; return; }

            IsUpdateBusy = true;
            ShowUpdateProgress = true;
            UpdateStatus = $"正在下载 v{info.Version}…";
            var progress = new Progress<(long done, long total)>(p =>
                UpdateProgress = p.total > 0 ? p.done / (double)p.total * 100 : 0);
            var result = await AppServices.Updates.DownloadAndRestartAsync(info.AssetUrl, progress);
            UpdateStatus = result.Message;
            if (result.Restarting)
            {
                // 触发应用退出，交给脚本重启
                UpdateStatus = "更新完成，正在重启…";
                await Task.Delay(300);
                Environment.Exit(0);
            }
        }
        catch (Exception ex)
        {
            UpdateStatus = "更新失败：" + ex.Message;
        }
        finally
        {
            IsUpdateBusy = false;
            ShowUpdateProgress = false;
        }
    }
}
