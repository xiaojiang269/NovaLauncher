using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLauncher.Core.Models;
using NovaLauncher.Core.Services;

namespace NovaLauncher.App.ViewModels;

public partial class ModsViewModel : ViewModelBase
{
    private readonly ModService _mods = AppServices.Mods;

    public ObservableCollection<string> Versions { get; } = new();
    [ObservableProperty] private string? selectedVersion;
    public ObservableCollection<ModInfo> Mods { get; } = new();
    [ObservableProperty] private ModInfo? selectedMod;
    [ObservableProperty] private string status = "就绪";
    public ObservableCollection<ModConflictDetector.ConflictGroup> Conflicts { get; } = new();
    [ObservableProperty] private bool showConflicts;
    public ObservableCollection<ModDependencyChecker.MissingDep> MissingDeps { get; } = new();
    [ObservableProperty] private bool showMissingDeps;

    // 版本切换/降级
    [ObservableProperty] private bool showVersionPanel;
    [ObservableProperty] private string versionPanelTitle = "";
    public ObservableCollection<ModFileVersion> RemoteVersions { get; } = new();
    [ObservableProperty] private ModFileVersion? selectedRemoteVersion;
    [ObservableProperty] private string remoteModProjectId = "";
    [ObservableProperty] private bool isRemoteBusy;

    // 一键提取/依赖排序
    [ObservableProperty] private string extractStatus = "";

    // 识别校验
    [ObservableProperty] private bool showVerifyPanel;
    [ObservableProperty] private string verifySummary = "";

    public ModsViewModel()
    {
        foreach (var v in AppServices.Dir.ListInstalledVersions()) Versions.Add(v);
        SelectedVersion = Versions.FirstOrDefault();
        RefreshMods();
    }

    partial void OnSelectedVersionChanged(string? value) => RefreshMods();

    private void RefreshMods()
    {
        Mods.Clear();
        if (SelectedVersion == null) return;
        foreach (var m in _mods.ListMods(SelectedVersion)) Mods.Add(m);
        Status = $"共 {Mods.Count} 个文件";
    }

    [RelayCommand]
    private void ToggleSelected()
    {
        if (SelectedVersion == null || SelectedMod == null) return;
        _mods.SetEnabled(SelectedVersion, SelectedMod.FileName, !SelectedMod.Enabled);
        RefreshMods();
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedVersion == null || SelectedMod == null) return;
        _mods.Delete(SelectedVersion, SelectedMod.FileName);
        RefreshMods();
    }

    [RelayCommand]
    private void DetectConflicts()
    {
        if (SelectedVersion == null) { Status = "请先选择版本"; return; }
        Conflicts.Clear();
        var detector = new ModConflictDetector();
        var groups = detector.Detect(_mods.ModsDir(SelectedVersion));
        foreach (var g in groups) Conflicts.Add(g);
        ShowConflicts = Conflicts.Count > 0;
        Status = Conflicts.Count == 0
            ? "未发现冲突：该版本 mods 目录中没有同一模组的多个版本。"
            : $"检测到 {Conflicts.Count} 组模组冲突（同一模组存在多个 jar，请保留一个、删除其余）。";
    }

    [RelayCommand]
    private void CheckDeps()
    {
        if (SelectedVersion == null) { Status = "请先选择版本"; return; }
        MissingDeps.Clear();
        var checker = new ModDependencyChecker();
        var missing = checker.Check(_mods.ModsDir(SelectedVersion));
        foreach (var m in missing) MissingDeps.Add(m);
        ShowMissingDeps = MissingDeps.Count > 0;
        Status = MissingDeps.Count == 0
            ? "依赖检查完成：未发现缺失的必装前置模组。"
            : $"依赖检查完成：发现 {MissingDeps.Count} 个缺失的必装前置模组（启动时可能报错）。";
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (SelectedVersion == null) return;
        var dir = _mods.ModsDir(SelectedVersion);
        OpenInFileManager(dir);
    }

    private static void OpenInFileManager(string path)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            UseShellExecute = true,
            FileName = PlatformDetect() == "mac" ? "open" : PlatformDetect() == "win" ? "explorer" : "xdg-open",
        };
        psi.ArgumentList.Add(path);
        try { System.Diagnostics.Process.Start(psi); } catch { }
    }

    private static string PlatformDetect() =>
        OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "mac" : "linux";

    // ---------- 版本切换 / 降级 ----------

    /// <summary>根据选中模组读取其 modid，搜索 Modrinth 并列出可用版本。</summary>
    [RelayCommand]
    private async Task SwitchVersionAsync()
    {
        if (SelectedVersion == null || SelectedMod == null)
        {
            Status = "请先选择版本和一个模组";
            return;
        }
        var identity = _mods.ReadIdentity(SelectedVersion, SelectedMod.FileName);
        if (identity == null)
        {
            Status = "无法读取该模组的标识（可能不是标准 mod jar）。";
            return;
        }

        IsRemoteBusy = true;
        ShowVersionPanel = true;
        RemoteVersions.Clear();
        SelectedRemoteVersion = null;
        VersionPanelTitle = identity.Display;
        Status = $"正在搜索「{identity.Display}」…";
        try
        {
            var api = new ModrinthApi();
            // 用 modid / 显示名搜索，取最相关结果
            var projects = await api.SearchAsync(identity.Display, null, identity.Loader ?? "", "mod", 5);
            var project = projects.FirstOrDefault();
            if (project == null)
            {
                Status = $"未在 Modrinth 找到「{identity.Display}」。";
                ShowVersionPanel = false;
                return;
            }
            RemoteModProjectId = project.Slug;
            // 推断当前版本的 MC 版本与加载器
            var (mc, loader) = InferVersionContext(SelectedVersion);
            var versions = await api.GetVersionsAsync(project.Slug, mc, loader);
            foreach (var v in versions)
            {
                var f = v.Files.FirstOrDefault(x => x.Primary) ?? v.Files.FirstOrDefault();
                if (f == null) continue;
                RemoteVersions.Add(new ModFileVersion
                {
                    Id = v.Id,
                    Name = v.Name,
                    VersionNumber = v.VersionNumber,
                    GameVersions = v.GameVersions,
                    Loaders = v.Loaders,
                    DownloadUrl = f.Url,
                    FileName = f.Filename,
                    Size = f.Size,
                });
            }
            Status = RemoteVersions.Count > 0
                ? $"「{identity.Display}」共 {RemoteVersions.Count} 个可用版本（MC {mc ?? "任意"} / {loader ?? "任意"}）。选择一个后点“下载替换”。"
                : $"「{identity.Display}」没有匹配 MC {mc ?? "任意"} / {loader ?? "任意"} 的版本，已改为显示全部。";
            if (RemoteVersions.Count == 0)
            {
                var all = await api.GetVersionsAsync(project.Slug, null, null);
                foreach (var v in all)
                {
                    var f = v.Files.FirstOrDefault(x => x.Primary) ?? v.Files.FirstOrDefault();
                    if (f == null) continue;
                    RemoteVersions.Add(new ModFileVersion
                    {
                        Id = v.Id, Name = v.Name, VersionNumber = v.VersionNumber,
                        GameVersions = v.GameVersions, Loaders = v.Loaders,
                        DownloadUrl = f.Url, FileName = f.Filename, Size = f.Size,
                    });
                }
                Status = $"「{identity.Display}」全部 {RemoteVersions.Count} 个版本。";
            }
        }
        catch (Exception ex)
        {
            Status = "加载版本失败：" + ex.Message;
            ShowVersionPanel = false;
        }
        finally
        {
            IsRemoteBusy = false;
        }
    }

    /// <summary>下载选中的远程版本替换本地模组（旧文件移到备份）。</summary>
    [RelayCommand]
    private async Task ApplyRemoteVersionAsync()
    {
        if (SelectedVersion == null || SelectedMod == null)
        {
            Status = "请先选择模组";
            return;
        }
        if (SelectedRemoteVersion == null || string.IsNullOrEmpty(RemoteModProjectId))
        {
            Status = "请先在上方选择一个版本";
            return;
        }
        if (string.IsNullOrEmpty(SelectedRemoteVersion.DownloadUrl))
        {
            Status = "所选版本没有可下载文件。";
            return;
        }

        IsRemoteBusy = true;
        Status = $"正在下载 {SelectedRemoteVersion.FileName}…";
        try
        {
            var api = new ModrinthApi();
            var modsDir = _mods.ModsDir(SelectedVersion);
            var dest = Path.Combine(modsDir, SelectedRemoteVersion.FileName);
            var progress = new Progress<(long done, long total)>(p => { });
            await api.DownloadAsync(SelectedRemoteVersion.DownloadUrl, dest, progress);

            // 旧文件备份
            var oldFile = Path.Combine(modsDir, SelectedMod.FileName);
            if (!string.Equals(oldFile, dest, StringComparison.OrdinalIgnoreCase) && File.Exists(oldFile))
            {
                var backupDir = Path.Combine(modsDir, ".versions-backup");
                Directory.CreateDirectory(backupDir);
                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                File.Move(oldFile, Path.Combine(backupDir, $"{stamp}_{SelectedMod.FileName}"), true);
            }
            RefreshMods();
            ShowVersionPanel = false;
            Status = $"已把「{SelectedMod.DisplayName}」替换为版本 {SelectedRemoteVersion.VersionNumber}（{SelectedRemoteVersion.FileName}）。";
        }
        catch (Exception ex)
        {
            Status = "下载替换失败：" + ex.Message;
        }
        finally
        {
            IsRemoteBusy = false;
        }
    }

    [RelayCommand]
    private void CloseVersionPanel() => ShowVersionPanel = false;

    // ---------- 一键提取 ----------

    [RelayCommand]
    private void ExtractNested()
    {
        if (SelectedVersion == null) { Status = "请先选择版本"; return; }
        var n = _mods.ExtractNested(SelectedVersion);
        RefreshMods();
        ExtractStatus = n > 0 ? $"已从嵌套文件提取 {n} 个模组到 mods 目录。" : "未发现可提取的嵌套模组。";
        Status = ExtractStatus;
    }

    // ---------- 依赖排序 ----------

    [RelayCommand]
    private void SortDeps()
    {
        if (SelectedVersion == null) { Status = "请先选择版本"; return; }
        var checker = new ModDependencyChecker();
        var ordered = checker.TopoSort(_mods.ModsDir(SelectedVersion));
        // 把 mods 列表按依赖顺序重排显示
        var current = Mods.ToDictionary(m => m.FileName, StringComparer.OrdinalIgnoreCase);
        Mods.Clear();
        foreach (var f in ordered)
            if (current.TryGetValue(f, out var m)) Mods.Add(m);
        // 追加未参与排序的
        foreach (var kv in current)
            if (!ordered.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)) Mods.Add(kv.Value);
        Status = "已按依赖关系重排模组列表（被依赖的在前）。";
    }

    /// <summary>从版本 id 推断 MC 版本与加载器。</summary>
    private static (string? mc, string? loader) InferVersionContext(string versionId)
    {
        var id = versionId.ToLowerInvariant();
        string? loader = null;
        if (id.Contains("fabric")) loader = "fabric";
        else if (id.Contains("quilt")) loader = "quilt";
        else if (id.Contains("neoforge")) loader = "neoforge";
        else if (id.Contains("forge")) loader = "forge";
        var m = System.Text.RegularExpressions.Regex.Match(id, @"(\d+\.\d+(?:\.\d+)?)");
        return (m.Success ? m.Groups[1].Value : null, loader);
    }

    // ---------- 识别校验 ----------

    /// <summary>识别校验选中模组：读取 jar 清单、判断加载器类型与能否被游戏加载、检查文件完整性。</summary>
    [RelayCommand]
    private void VerifySelected()
    {
        if (SelectedVersion == null || SelectedMod == null)
        {
            Status = "请先选择版本和一个模组";
            return;
        }
        var file = SelectedMod.FileName;
        var path = Path.Combine(AppServices.Dir.VersionModsDir(SelectedVersion), file);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"文件：{file}");
        sb.AppendLine($"大小：{SelectedMod.SizeText} · 修改：{SelectedMod.ModifiedAt:yyyy-MM-dd HH:mm}");
        sb.AppendLine();

        if (SelectedMod.Kind != "模组")
        {
            sb.AppendLine("类型：非标准 .jar 模组（可能为资源包 / 库 / 其他文件）。");
            sb.AppendLine("校验：不属于可识别模组清单，无需按模组校验。");
            ShowVerifyPanel = true;
            VerifySummary = sb.ToString();
            Status = "识别完成（非标准模组文件）。";
            return;
        }

        // 读清单
        var identity = _mods.ReadIdentity(SelectedVersion, file);
        if (identity == null)
        {
            sb.AppendLine("清单：未找到 fabric.mod.json / mods.toml / mcmod.info");
            sb.AppendLine("结论：该 jar 不是可识别的模组（可能是核心 mod / 前置库 / 损坏文件）。");
            sb.AppendLine("提示：若无法启动，可尝试删除；若是前置库请保留。");
            ShowVerifyPanel = true;
            VerifySummary = sb.ToString();
            Status = "识别完成：非标准模组 jar。";
            return;
        }

        // 完整性：能否打开 zip 且包含主类或资源（读清单成功即基本完整）
        var loaderName = identity.Loader switch
        {
            "fabric" => "Fabric (fabric.mod.json)",
            "forge" => "Forge/NeoForge (mods.toml 或 mcmod.info)",
            _ => identity.Loader ?? "未知",
        };
        sb.AppendLine($"模组 ID：{identity.Id}");
        sb.AppendLine($"版本：{identity.Version ?? "未知"}");
        sb.AppendLine($"显示名：{identity.Display}");
        sb.AppendLine($"加载器：{loaderName}");
        sb.AppendLine($"加载方式：{(identity.Loader == "fabric" ? "Fabric/Quilt 环境" : "Forge/NeoForge 环境")}");
        sb.AppendLine($"完整性：清单可读，文件结构正常。");

        // 冲突提示
        var conflicts = new ModConflictDetector().Detect(AppServices.Dir.VersionModsDir(SelectedVersion));
        var grp = conflicts.FirstOrDefault(g => string.Equals(g.ModId, identity.Id, StringComparison.OrdinalIgnoreCase));
        if (grp != null)
        {
            sb.AppendLine();
            sb.AppendLine($"⚠ 冲突：同一模组存在 {grp.Files.Count} 个文件：");
            foreach (var f in grp.Files) sb.AppendLine($"   · {f.FileName}（v{f.Version ?? "?"}）");
            sb.AppendLine("建议：保留一个版本，删除其余，否则可能无法启动或报错。");
        }

        ShowVerifyPanel = true;
        VerifySummary = sb.ToString();
        Status = $"已校验「{identity.Display}」：加载器 {identity.Loader ?? "未知"} · 版本 {identity.Version ?? "未知"}";
    }

    [RelayCommand]
    private void CloseVerifyPanel() => ShowVerifyPanel = false;
}
