using System.Collections.ObjectModel;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLauncher.Core.Models;
using NovaLauncher.Core.Services;
using NovaLauncher.Core.Util;

namespace NovaLauncher.App.ViewModels;

public partial class VersionsViewModel : ViewModelBase
{
    private readonly VersionService _versions = AppServices.Versions;
    private readonly DownloadService _downloads = AppServices.Downloads;
    private readonly LoaderService _loaders = AppServices.Loaders;
    private readonly PackService _packs = AppServices.Packs;

    // 本地版本
    public ObservableCollection<string> LocalVersions { get; } = new();
    [ObservableProperty] private string? selectedLocalVersion;

    // 官方版本
    public ObservableCollection<VersionManifest.VersionEntry> OfficialVersions { get; } = new();
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string officialCount = "";

    // Fabric
    [ObservableProperty] private string fabricMcVersion = "1.20.1";
    public ObservableCollection<string> FabricLoaders { get; } = new();
    [ObservableProperty] private string? selectedFabricLoader;

    // Forge
    [ObservableProperty] private string forgeMcVersion = "1.20.1";
    [ObservableProperty] private string forgeVersion = "";

    // Quilt
    [ObservableProperty] private string quiltMcVersion = "1.20.1";
    public ObservableCollection<string> QuiltLoaders { get; } = new();
    [ObservableProperty] private string? selectedQuiltLoader;

    // NeoForge
    [ObservableProperty] private string neoForgeMcVersion = "1.20.1";
    public ObservableCollection<string> NeoForgeVersions { get; } = new();
    [ObservableProperty] private string? selectedNeoForgeVersion;

    // 版本迁移
    [ObservableProperty] private string? migrateSourceVersion;
    [ObservableProperty] private string? migrateTargetVersion;
    [ObservableProperty] private bool migrateMods = true;
    [ObservableProperty] private bool migrateResourcePacks = true;
    [ObservableProperty] private bool migrateShaders = true;
    [ObservableProperty] private string migrateSummary = "";

    // 状态
    [ObservableProperty] private string status = "就绪";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool showProgress;
    [ObservableProperty] private int progressValue;
    [ObservableProperty] private int progressMax = 1;
    [ObservableProperty] private string progressText = "";

    private List<VersionManifest.VersionEntry> _allOfficial = new();

    public VersionsViewModel()
    {
        RefreshLocal();
        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                var manifest = await AppServices.Manifest.GetManifestAsync();
                _allOfficial = manifest.Versions.ToList();
                ApplyFilter();
            }
            catch (Exception ex) { Status = "加载官方版本清单失败：" + ex.Message; }
        });
    }

    private void RefreshLocal()
    {
        LocalVersions.Clear();
        foreach (var v in AppServices.Dir.ListInstalledVersions()) LocalVersions.Add(v);
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        OfficialVersions.Clear();
        var kw = SearchText?.Trim() ?? "";
        var list = string.IsNullOrEmpty(kw)
            ? _allOfficial
            : _allOfficial.Where(v => v.Id.Contains(kw, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var v in list) OfficialVersions.Add(v);
        OfficialCount = $"共 {_allOfficial.Count} 个版本" + (list.Count < _allOfficial.Count ? $"，筛选 {list.Count}" : "");
    }

    [RelayCommand]
    private void Refresh() => RefreshLocal();

    [RelayCommand]
    private async Task InstallOfficialAsync(object? param)
    {
        var id = param as string ?? SelectedLocalVersion;
        if (string.IsNullOrEmpty(id)) return;
        await InstallVersionAsync(id);
    }

    private async Task InstallVersionAsync(string versionId)
    {
        IsBusy = true;
        ShowProgress = true;
        Status = $"正在安装版本 {versionId}…";
        ProgressText = "获取版本信息…";
        try
        {
            MinecraftVersion? version = _versions.LoadLocal(versionId);
            if (version == null) version = await _versions.FetchRemoteAsync(versionId, null);
            var progress = new Progress<DownloadProgress>(p =>
            {
                if (p.TotalItems > 0)
                {
                    ProgressMax = Math.Max(p.TotalItems, 1);
                    ProgressValue = Math.Min(p.DoneItems, ProgressMax);
                }
                ProgressText = $"{p.Phase}：{p.CurrentFile} ({p.DoneItems}/{p.TotalItems})";
            });
            var summary = await _downloads.DownloadVersionAsync(version, versionId, progress, CancellationToken.None);
            if (summary.Success)
            {
                Status = $"版本 {versionId} 安装完成（新增 {summary.Downloaded}，跳过 {summary.Skipped}）";
                RefreshLocal();
            }
            else
            {
                Status = $"安装存在失败项（{summary.Failed}）\n{string.Join("；", summary.Errors.Take(3))}";
            }
        }
        catch (Exception ex) { Status = "安装失败：" + ex.Message; }
        finally { IsBusy = false; ShowProgress = false; }
    }

    [RelayCommand]
    private async Task LoadFabricLoadersAsync()
    {
        if (string.IsNullOrWhiteSpace(FabricMcVersion)) { Status = "请输入 Fabric 对应的游戏版本"; return; }
        IsBusy = true;
        FabricLoaders.Clear();
        try
        {
            var loaders = await _loaders.GetFabricLoadersAsync(FabricMcVersion.Trim());
            foreach (var l in loaders) FabricLoaders.Add(l);
            SelectedFabricLoader = FabricLoaders.FirstOrDefault();
            Status = loaders.Count > 0 ? $"找到 {loaders.Count} 个 Fabric Loader 版本" : "该游戏版本暂无 Fabric Loader";
        }
        catch (Exception ex) { Status = "获取 Fabric Loader 失败：" + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task InstallFabricAsync()
    {
        if (string.IsNullOrWhiteSpace(FabricMcVersion) || string.IsNullOrEmpty(SelectedFabricLoader))
        {
            Status = "请先选择游戏版本并加载 Loader 列表";
            return;
        }
        IsBusy = true;
        try
        {
            Status = "安装 Fabric…";
            var id = await _loaders.InstallFabricAsync(FabricMcVersion.Trim(), SelectedFabricLoader, null);
            RefreshLocal();
            Status = $"Fabric 版本 {id} 已安装，可在主页选择后启动（首次启动会自动下载补全）。";
        }
        catch (Exception ex) { Status = "Fabric 安装失败：" + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task InstallForgeAsync()
    {
        if (string.IsNullOrWhiteSpace(ForgeMcVersion) || string.IsNullOrWhiteSpace(ForgeVersion))
        {
            Status = "请输入 Forge 对应的游戏版本和 Forge 版本号";
            return;
        }
        IsBusy = true;
        try
        {
            Status = "下载 Forge 安装器…";
            var installer = await _loaders.DownloadForgeInstallerAsync(ForgeMcVersion.Trim(), ForgeVersion.Trim(), null);

            var java = JavaService.Probe(AppServices.Config.JavaPath) ??
                       JavaService.FindSystemJava().OrderByDescending(j => j.MajorVersion).FirstOrDefault();
            if (java == null) { Status = "未找到 Java，无法运行 Forge 安装器"; return; }

            Status = "运行 Forge 安装器（请耐心等待）…";
            var ok = _loaders.RunForgeInstaller(installer, java.Path, new Progress<string>(s => Status = s));
            if (ok)
            {
                RefreshLocal();
                Status = "Forge 安装完成。";
            }
            else
            {
                Status = "Forge 安装失败，请查看上方日志。";
            }
        }
        catch (Exception ex) { Status = "Forge 安装失败：" + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ImportPackAsync()
    {
        var files = await MainWindowRef.PickFilesAsync("*.zip");
        if (files == null || files.Count == 0) return;
        IsBusy = true;
        try
        {
            Status = "导入整合包…";
            var id = _packs.Import(files[0]);
            RefreshLocal();
            Status = $"整合包已导入为版本「{id}」。";
        }
        catch (Exception ex) { Status = "导入失败：" + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ExportPackAsync()
    {
        var id = SelectedLocalVersion;
        if (string.IsNullOrEmpty(id)) { Status = "请先选择要导出的本地版本"; return; }
        var dest = await MainWindowRef.SaveFileAsync($"NovaPack_{id}.zip");
        if (dest == null) return;
        IsBusy = true;
        try
        {
            var path = _packs.Export(id, $"NovaPack {id}", "", dest);
            Status = $"整合包已导出：{path}";
        }
        catch (Exception ex) { Status = "导出失败：" + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void DeleteVersion()
    {
        var id = SelectedLocalVersion;
        if (string.IsNullOrEmpty(id)) return;
        var dir = AppServices.Dir.VersionDir(id);
        if (!Directory.Exists(dir)) return;
        Directory.Delete(dir, true);
        RefreshLocal();
        Status = $"已删除版本 {id}";
    }

    // ---------- Quilt ----------

    [RelayCommand]
    private async Task LoadQuiltLoadersAsync()
    {
        if (string.IsNullOrWhiteSpace(QuiltMcVersion)) { Status = "请输入 Quilt 对应的游戏版本"; return; }
        IsBusy = true;
        QuiltLoaders.Clear();
        try
        {
            var loaders = await _loaders.GetQuiltLoadersAsync(QuiltMcVersion.Trim());
            foreach (var l in loaders) QuiltLoaders.Add(l);
            SelectedQuiltLoader = QuiltLoaders.FirstOrDefault();
            Status = loaders.Count > 0 ? $"找到 {loaders.Count} 个 Quilt Loader 版本" : "该游戏版本暂无 Quilt Loader";
        }
        catch (Exception ex) { Status = "获取 Quilt Loader 失败：" + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task InstallQuiltAsync()
    {
        if (string.IsNullOrWhiteSpace(QuiltMcVersion) || string.IsNullOrEmpty(SelectedQuiltLoader))
        {
            Status = "请先选择游戏版本并加载 Quilt Loader 列表";
            return;
        }
        IsBusy = true;
        try
        {
            Status = "安装 Quilt…";
            var id = await _loaders.InstallQuiltAsync(QuiltMcVersion.Trim(), SelectedQuiltLoader, null);
            RefreshLocal();
            Status = $"Quilt 版本 {id} 已安装。";
        }
        catch (Exception ex) { Status = "Quilt 安装失败：" + ex.Message; }
        finally { IsBusy = false; }
    }

    // ---------- NeoForge ----------

    [RelayCommand]
    private async Task LoadNeoForgeVersionsAsync()
    {
        if (string.IsNullOrWhiteSpace(NeoForgeMcVersion)) { Status = "请输入 NeoForge 对应的游戏版本"; return; }
        IsBusy = true;
        NeoForgeVersions.Clear();
        try
        {
            var versions = await _loaders.GetNeoForgeVersionsAsync(NeoForgeMcVersion.Trim());
            foreach (var v in versions) NeoForgeVersions.Add(v);
            SelectedNeoForgeVersion = NeoForgeVersions.FirstOrDefault();
            Status = versions.Count > 0 ? $"找到 {versions.Count} 个 NeoForge 版本" : "该游戏版本暂无 NeoForge";
        }
        catch (Exception ex) { Status = "获取 NeoForge 版本失败：" + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task InstallNeoForgeAsync()
    {
        if (string.IsNullOrWhiteSpace(NeoForgeMcVersion) || string.IsNullOrEmpty(SelectedNeoForgeVersion))
        {
            Status = "请先选择游戏版本和 NeoForge 版本";
            return;
        }
        IsBusy = true;
        try
        {
            Status = "下载 NeoForge 安装器…";
            var installer = await _loaders.DownloadNeoForgeInstallerAsync(NeoForgeMcVersion.Trim(), SelectedNeoForgeVersion, null);
            var java = JavaService.Probe(AppServices.Config.JavaPath) ??
                       JavaService.FindSystemJava().OrderByDescending(j => j.MajorVersion).FirstOrDefault();
            if (java == null) { Status = "未找到 Java，无法运行 NeoForge 安装器"; return; }
            Status = "运行 NeoForge 安装器（请耐心等待）…";
            var ok = _loaders.RunInstallerClient(installer, java.Path, new Progress<string>(s => Status = s));
            if (ok) { RefreshLocal(); Status = "NeoForge 安装完成。"; }
            else Status = "NeoForge 安装失败，请查看上方日志。";
        }
        catch (Exception ex) { Status = "NeoForge 安装失败：" + ex.Message; }
        finally { IsBusy = false; }
    }

    // ---------- 版本迁移 ----------

    /// <summary>把源版本的内容（模组/资源包/光影）迁移到目标版本。用于把旧实例配置搬到新隔离实例。</summary>
    [RelayCommand]
    private async Task MigrateAsync()
    {
        if (string.IsNullOrEmpty(MigrateSourceVersion) || string.IsNullOrEmpty(MigrateTargetVersion))
        {
            Status = "请先选择源版本和目标版本";
            return;
        }
        if (string.Equals(MigrateSourceVersion, MigrateTargetVersion, StringComparison.Ordinal))
        {
            Status = "源版本与目标版本相同，无需迁移";
            return;
        }
        if (!Directory.Exists(AppServices.Dir.VersionDir(MigrateSourceVersion)))
        {
            Status = "源版本目录不存在：请确认已安装 " + MigrateSourceVersion;
            return;
        }
        // 目标版本需已存在目录
        Directory.CreateDirectory(AppServices.Dir.VersionDir(MigrateTargetVersion));

        IsBusy = true;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"迁移：{MigrateSourceVersion} → {MigrateTargetVersion}");
        var total = 0;
        try
        {
            if (MigrateMods)
            {
                var n = CopyDirContents("mods", MigrateSourceVersion, MigrateTargetVersion);
                total += n;
                sb.AppendLine($"模组：迁移 {n} 个文件");
            }
            if (MigrateResourcePacks)
            {
                var n = CopyDirContents("resourcepacks", MigrateSourceVersion, MigrateTargetVersion);
                total += n;
                sb.AppendLine($"资源包：迁移 {n} 个文件");
            }
            if (MigrateShaders)
            {
                var n = CopyDirContents("shaderpacks", MigrateSourceVersion, MigrateTargetVersion);
                total += n;
                sb.AppendLine($"光影：迁移 {n} 个文件");
            }
            sb.AppendLine($"共迁移 {total} 个文件。");
            MigrateSummary = sb.ToString();
            Status = $"迁移完成：{MigrateSourceVersion} → {MigrateTargetVersion}，共 {total} 个文件。";
        }
        catch (Exception ex)
        {
            Status = "迁移失败：" + ex.Message;
            MigrateSummary = sb + "\n错误：" + ex.Message;
        }
        finally { IsBusy = false; }
    }

    /// <summary>把源版本某子目录的文件复制到目标版本同名子目录（跳过同名已存在）。返回复制数。</summary>
    private int CopyDirContents(string sub, string srcVersion, string dstVersion)
    {
        var src = AppServices.Dir.VersionSubDir(srcVersion, sub);
        var dst = AppServices.Dir.VersionSubDir(dstVersion, sub);
        if (!Directory.Exists(src)) return 0;
        Directory.CreateDirectory(dst);
        int n = 0;
        foreach (var f in Directory.GetFiles(src))
        {
            var name = Path.GetFileName(f);
            var target = Path.Combine(dst, name);
            if (File.Exists(target)) continue;
            File.Copy(f, target);
            n++;
        }
        return n;
    }
}
