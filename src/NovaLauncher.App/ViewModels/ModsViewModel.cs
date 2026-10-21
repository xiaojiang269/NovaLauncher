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
}
