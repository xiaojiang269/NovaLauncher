using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLauncher.Core.Models;
using NovaLauncher.Core.Services;

namespace NovaLauncher.App.ViewModels;

public partial class ModDownloadViewModel : ViewModelBase
{
    private readonly ModrinthApi _modrinth = new();
    private readonly CurseForgeApi _curse = new();

    public ObservableCollection<string> Sources { get; } = new() { "Modrinth", "CurseForge" };
    public ObservableCollection<string> Loaders { get; } = new() { "不限", "fabric", "forge", "quilt", "neoforge" };
    public ObservableCollection<string> ProjectTypes { get; } = new() { "模组", "光影", "资源包", "整合包" };
    public ObservableCollection<string> GameVersions { get; } = new()
        { "不限", "1.21.4", "1.21.1", "1.20.6", "1.20.4", "1.20.1", "1.19.4", "1.18.2", "1.17.1", "1.16.5", "1.15.2", "1.14.4", "1.12.2" };

    public ObservableCollection<ModSearchItem> Projects { get; } = new();
    [ObservableProperty] private ModSearchItem? selectedProject;
    public ObservableCollection<ModFileVersion> Versions { get; } = new();
    [ObservableProperty] private ModFileVersion? selectedVersion;

    public ObservableCollection<string> LocalVersions { get; } = new();
    [ObservableProperty] private string? targetVersion;

    [ObservableProperty] private int sourceIndex;
    [ObservableProperty] private int projectTypeIndex;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string gameVersion = "不限";
    [ObservableProperty] private int loaderIndex;
    [ObservableProperty] private string status = "就绪";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool showProgress;
    [ObservableProperty] private double progressValue;

    public ModDownloadViewModel()
    {
        // 把本地已安装版本加入游戏版本下拉（排在"不限"之后，去重），方便筛选自己的版本
        var insertIdx = 1;
        foreach (var v in AppServices.Dir.ListInstalledVersions())
        {
            if (string.IsNullOrWhiteSpace(v) || v == "不限" || GameVersions.Contains(v)) continue;
            GameVersions.Insert(insertIdx++, v);
        }
        foreach (var v in AppServices.Dir.ListInstalledVersions()) LocalVersions.Add(v);
        TargetVersion = LocalVersions.FirstOrDefault();
    }

    private bool UseCurse => SourceIndex == 1;

    private string SelectedLoader => LoaderIndex <= 0 ? "" : Loaders[LoaderIndex];

    /// <summary>mod / shader / resourcepack / modpack</summary>
    private string SelectedProjectType => ProjectTypeIndex switch { 1 => "shader", 2 => "resourcepack", 3 => "modpack", _ => "mod" };

    private string SelectedProjectTypeName => ProjectTypes[ProjectTypeIndex];

    /// <summary>下载目标子目录：模组→mods，光影→shaderpacks，资源包→resourcepacks。</summary>
    private string TargetSubDir => SelectedProjectType switch { "shader" => "shaderpacks", "resourcepack" => "resourcepacks", _ => "mods" };

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            Status = "请输入模组关键词（如 sodium、jei、科技）";
            return;
        }
        if (UseCurse && string.IsNullOrWhiteSpace(AppServices.Config.CurseForgeApiKey))
        {
            Status = "使用 CurseForge 源需先在“设置”页填写 CurseForge API key。";
            return;
        }

        IsBusy = true;
        Projects.Clear();
        Versions.Clear();
        Status = $"正在从 {Sources[SourceIndex]} 搜索{SelectedProjectTypeName}…";
        try
        {
            var loader = SelectedLoader;
            var gv = GameVersion == "不限" ? null : GameVersion;
            if (UseCurse)
            {
                var items = await _curse.SearchAsync(AppServices.Config.CurseForgeApiKey, SearchText, gv, loader, CurseClassId(SelectedProjectType));
                foreach (var i in items) Projects.Add(i);
            }
            else
            {
                var items = await _modrinth.SearchAsync(SearchText, gv, loader, SelectedProjectType);
                foreach (var i in items)
                    Projects.Add(new ModSearchItem
                    {
                        Source = "modrinth",
                        ModId = i.Slug,
                        Title = i.Title,
                        Description = i.Description,
                        Downloads = i.Downloads,
                        IconUrl = i.IconUrl,
                        Categories = i.Categories,
                    });
            }
            Status = $"找到 {Projects.Count} 个{SelectedProjectTypeName}（筛选：{GameVersion} / {loader}）。点击项目查看可用版本并下载。";
        }
        catch (Exception ex)
        {
            Status = "搜索失败：" + ex.Message;
        }
        finally { IsBusy = false; }
    }

    partial void OnSelectedProjectChanged(ModSearchItem? value) => _ = LoadVersionsAsync(value);

    private async Task LoadVersionsAsync(ModSearchItem? project)
    {
        Versions.Clear();
        SelectedVersion = null;
        if (project == null) return;
        IsBusy = true;
        Status = $"加载 {project.Title} 的版本…";
        try
        {
            var loader = SelectedLoader;
            var gv = GameVersion == "不限" ? null : GameVersion;
            if (project.Source == "curseforge")
            {
                var files = await _curse.GetFilesAsync(AppServices.Config.CurseForgeApiKey, project.ModId, gv, loader);
                foreach (var f in files) Versions.Add(f);
            }
            else
            {
                var list = await _modrinth.GetVersionsAsync(project.ModId, gv, loader);
                foreach (var v in list)
                {
                    var f = v.Files.FirstOrDefault(x => x.Primary) ?? v.Files.FirstOrDefault();
                    if (f == null) continue;
                    Versions.Add(new ModFileVersion
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
            }
            Status = $"共 {Versions.Count} 个可用版本。选择版本后点“下载”到本地游戏。";
        }
        catch (Exception ex)
        {
            Status = "加载版本失败：" + ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task DownloadAsync(ModFileVersion? version)
    {
        if (version == null) { Status = "请先在列表选择一个模组版本"; return; }
        if (SelectedProject == null) { Status = "请先选择模组"; return; }
        if (string.IsNullOrEmpty(TargetVersion)) { Status = "请选择要下载到的本地游戏版本"; return; }

        if (SelectedProjectType == "modpack")
        {
            await DownloadPackAsync(version);
            return;
        }
        await DownloadOneAsync(SelectedProject, version);
    }

    /// <summary>下载并导入 Modrinth 整合包（.mrpack）到目标版本。</summary>
    private async Task DownloadPackAsync(ModFileVersion version)
    {
        IsBusy = true;
        ShowProgress = true;
        ProgressValue = 0;
        Status = $"正在下载整合包 {version.FileName}…";
        var tmp = Path.Combine(Path.GetTempPath(), "nl_pack_" + Guid.NewGuid().ToString("N") + ".mrpack");
        try
        {
            var progress = new Progress<(long done, long total)>(p =>
                ProgressValue = p.total > 0 ? p.done / (double)p.total * 100 : 0);
            await _modrinth.DownloadAsync(version.DownloadUrl, tmp, progress);
            Status = "整合包下载完成，正在解析并下载各模组…";
            ProgressValue = 0;
            var importer = new ModrinthPackImporter();
            var packProgress = new Progress<(string msg, int cur, int total)>(p =>
            {
                Status = $"正在导入模组 {p.cur}/{p.total}：{p.msg}";
                ProgressValue = p.total > 0 ? p.cur / (double)p.total * 100 : 0;
            });
            var res = await importer.ImportAsync(tmp, AppServices.Dir, TargetVersion!, packProgress);
            Status = $"整合包「{res.Name}」导入完成：模组 {res.Ok} 成功 / {res.Fail} 失败（目标版本 {TargetVersion}）。";
        }
        catch (Exception ex)
        {
            Status = "整合包下载/导入失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            ShowProgress = false;
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    /// <summary>批量下载当前搜索结果全部项目（每个取第一个匹配版本）到目标版本。</summary>
    [RelayCommand]
    private async Task DownloadAllAsync()
    {
        if (Projects.Count == 0) { Status = "没有可批量下载的结果，请先搜索。"; return; }
        if (string.IsNullOrEmpty(TargetVersion)) { Status = "请选择要下载到的本地游戏版本"; return; }
        if (SelectedProjectType == "modpack") { Status = "整合包请单独下载（见下方整合包说明）。"; return; }

        IsBusy = true;
        ShowProgress = true;
        ProgressValue = 0;
        int ok = 0, fail = 0, skip = 0, idx = 0;
        foreach (var proj in Projects.ToList())
        {
            idx++;
            Status = $"批量下载 {idx}/{Projects.Count}：{proj.Title}…";
            try
            {
                ModFileVersion? v = null;
                var gv = GameVersion == "不限" ? null : GameVersion;
                if (proj.Source == "curseforge")
                    v = (await _curse.GetFilesAsync(AppServices.Config.CurseForgeApiKey, proj.ModId, gv, SelectedLoader)).FirstOrDefault();
                else
                {
                    var list = await _modrinth.GetVersionsAsync(proj.ModId, gv, SelectedLoader);
                    var f = list.SelectMany(x => x.Files).FirstOrDefault(y => y.Primary) ?? list.SelectMany(x => x.Files).FirstOrDefault();
                    if (f != null)
                        v = new ModFileVersion { FileName = f.Filename, DownloadUrl = f.Url, Size = f.Size, GameVersions = list.FirstOrDefault()?.GameVersions ?? Array.Empty<string>() };
                }
                if (v == null) { fail++; continue; }
                var r = await DownloadOneAsync(proj, v);
                if (r == "exist") skip++; else if (r == "ok") ok++; else fail++;
            }
            catch { fail++; }
        }
        IsBusy = false;
        ShowProgress = false;
        Status = $"批量下载完成：成功 {ok}，跳过 {skip}，失败 {fail}（目标 {TargetVersion}）。";
    }

    /// <summary>下载单个项目版本到目标目录。返回 "ok" / "exist" / "fail"。</summary>
    private async Task<string> DownloadOneAsync(ModSearchItem proj, ModFileVersion version)
    {
        var sub = TargetSubDir;
        var modsDir = AppServices.Dir.VersionSubDir(TargetVersion!, sub);
        Directory.CreateDirectory(modsDir);
        var dest = Path.Combine(modsDir, version.FileName);
        if (File.Exists(dest)) { Status = $"已存在：{version.FileName}（跳过）"; return "exist"; }

        Status = $"正在下载 {version.FileName}…";
        var progress = new Progress<(long done, long total)>(p =>
        {
            ProgressValue = p.total > 0 ? p.done / (double)p.total * 100 : 0;
        });
        try
        {
            if (proj.Source == "curseforge")
                await _curse.DownloadAsync(AppServices.Config.CurseForgeApiKey, proj.ModId, version, dest);
            else
                await _modrinth.DownloadAsync(version.DownloadUrl, dest, progress);
            proj.Downloaded = true;
            return "ok";
        }
        catch
        {
            Status = $"下载失败：{version.FileName}";
            return "fail";
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        LocalVersions.Clear();
        foreach (var v in AppServices.Dir.ListInstalledVersions()) LocalVersions.Add(v);
        TargetVersion = LocalVersions.FirstOrDefault();
    }

    [RelayCommand]
    private void OpenTargetModsFolder()
    {
        if (string.IsNullOrEmpty(TargetVersion)) { Status = "请先选择目标游戏版本"; return; }
        var dir = AppServices.Dir.VersionSubDir(TargetVersion, TargetSubDir);
        Directory.CreateDirectory(dir);
        OpenInFileManager(dir);
    }

    private static int CurseClassId(string type) => type switch
    {
        "shader" => 6552,
        "resourcepack" => 12,
        _ => 6, // Mods
    };

    private static void OpenInFileManager(string path)
    {
        var name = OperatingSystem.IsWindows() ? "explorer" : OperatingSystem.IsMacOS() ? "open" : "xdg-open";
        var psi = new System.Diagnostics.ProcessStartInfo { UseShellExecute = true, FileName = name };
        psi.ArgumentList.Add(path);
        try { System.Diagnostics.Process.Start(psi); } catch { }
    }
}
