using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLauncher.Core.Models;
using NovaLauncher.Core.Services;

namespace NovaLauncher.App.ViewModels;

public partial class ResourceViewModel : ViewModelBase
{
    private readonly ResourceService _res = AppServices.Resources;

    public ObservableCollection<string> Versions { get; } = new();
    [ObservableProperty] private string? selectedVersion;
    public ObservableCollection<ResourceEntry> Packs { get; } = new();
    [ObservableProperty] private ResourceEntry? selectedPack;
    [ObservableProperty] private string status = "就绪";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isShaderMode;
    public ObservableCollection<string> KindOptions { get; } = new() { "资源包 (resourcepacks)", "光影 (shaderpacks)" };
    [ObservableProperty] private int kindIndex;

    public ResourceViewModel()
    {
        foreach (var v in AppServices.Dir.ListInstalledVersions()) Versions.Add(v);
        SelectedVersion = Versions.FirstOrDefault();
        Refresh();
    }

    partial void OnSelectedVersionChanged(string? value) => Refresh();
    partial void OnKindIndexChanged(int value) => Refresh();

    private ResourceKind Kind => KindIndex == 1 ? ResourceKind.ShaderPack : ResourceKind.ResourcePack;
    private string KindLabel => KindIndex == 1 ? "光影" : "资源包";

    private void Refresh()
    {
        Packs.Clear();
        if (SelectedVersion == null) return;
        foreach (var p in _res.List(SelectedVersion, Kind)) Packs.Add(p);
        Status = $"共 {Packs.Count} 个 {KindLabel}（{SelectedVersion}）";
    }

    [RelayCommand]
    private void RefreshCmd() => Refresh();

    [RelayCommand]
    private void ToggleSelected()
    {
        if (SelectedVersion == null || SelectedPack == null) return;
        _res.SetEnabled(SelectedVersion, Kind, SelectedPack.FileName, !SelectedPack.Enabled);
        Refresh();
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedVersion == null || SelectedPack == null) return;
        _res.Delete(SelectedVersion, Kind, SelectedPack.FileName);
        Refresh();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (SelectedVersion == null) { Status = "请先选择版本"; return; }
        var p = _res.OpenFolder(SelectedVersion, Kind);
        OpenPath(p);
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (SelectedVersion == null) { Status = "请先选择版本"; return; }
        var files = await MainWindowRef.PickFilesAsync("*.zip");
        if (files == null || files.Count == 0) return;
        IsBusy = true;
        try
        {
            Status = $"正在导入 {KindLabel}…";
            var name = _res.Import(SelectedVersion, Kind, files[0]);
            Refresh();
            Status = $"已导入 {KindLabel}：{name}";
        }
        catch (Exception ex) { Status = "导入失败：" + ex.Message; }
        finally { IsBusy = false; }
    }

    private static void OpenPath(string p)
    {
        try
        {
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{p}\"") { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS()) Process.Start(new ProcessStartInfo("open", $"\"{p}\"") { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo("xdg-open", $"\"{p}\"") { UseShellExecute = true });
        }
        catch { }
    }
}
