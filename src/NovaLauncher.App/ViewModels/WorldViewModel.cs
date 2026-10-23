using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLauncher.Core.Models;
using NovaLauncher.Core.Services;

namespace NovaLauncher.App.ViewModels;

public partial class WorldViewModel : ViewModelBase
{
    private readonly WorldService _worlds = AppServices.Worlds;

    public ObservableCollection<WorldInfo> Worlds { get; } = new();
    [ObservableProperty] private WorldInfo? selectedWorld;
    [ObservableProperty] private string status = "就绪";
    public ObservableCollection<string> Backups { get; } = new();
    [ObservableProperty] private string? selectedBackup;
    [ObservableProperty] private bool isBusy;

    public WorldViewModel()
    {
        RefreshWorlds();
        RefreshBackups();
    }

    private void RefreshWorlds()
    {
        Worlds.Clear();
        foreach (var w in _worlds.ListWorlds()) Worlds.Add(w);
        Status = $"共 {Worlds.Count} 个存档";
    }

    private void RefreshBackups()
    {
        Backups.Clear();
        foreach (var b in _worlds.ListBackups()) Backups.Add(b);
    }

    [RelayCommand]
    private void Refresh()
    {
        RefreshWorlds();
        RefreshBackups();
    }

    [RelayCommand]
    private void OpenSelected()
    {
        if (SelectedWorld == null) return;
        try
        {
            var p = _worlds.OpenFolder(SelectedWorld.FolderName);
            OpenPath(p);
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    [RelayCommand]
    private void ExportSelected()
    {
        if (SelectedWorld == null) return;
        try
        {
            var zip = _worlds.Export(SelectedWorld.FolderName);
            Status = $"已导出：{zip}";
        }
        catch (Exception ex) { Status = "导出失败：" + ex.Message; }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var file = await PickZipAsync();
        if (file == null) return;
        IsBusy = true;
        try
        {
            Status = "正在导入存档…";
            var name = _worlds.Import(file);
            RefreshWorlds();
            Status = $"已导入存档 {name}";
        }
        catch (Exception ex) { Status = "导入失败：" + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task BackupAsync()
    {
        if (SelectedWorld == null) { Status = "请先选择一个存档"; return; }
        IsBusy = true;
        try
        {
            var zip = _worlds.Backup(SelectedWorld.FolderName);
            RefreshBackups();
            Status = $"已备份：{Path.GetFileName(zip)}";
        }
        catch (Exception ex) { Status = "备份失败：" + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (SelectedBackup == null) { Status = "请先在下方选择一个备份"; return; }
        IsBusy = true;
        try
        {
            var bkDir = Path.Combine(AppServices.Dir.Root, "saves", "backups");
            var name = _worlds.RestoreFromBackup(Path.Combine(bkDir, SelectedBackup));
            RefreshWorlds();
            Status = $"已从备份恢复为存档 {name}";
        }
        catch (Exception ex) { Status = "恢复失败：" + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedWorld == null) return;
        try
        {
            _worlds.Delete(SelectedWorld.FolderName);
            RefreshWorlds();
            Status = $"已删除存档 {SelectedWorld.Name}";
        }
        catch (Exception ex) { Status = "删除失败：" + ex.Message; }
    }

    private static void OpenPath(string p)
    {
        try
        {
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{p}\"") { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS()) Process.Start(new ProcessStartInfo("open", $"\"{p}\"") { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo("xdg-open", $"\"{p}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { /* 打开失败不阻塞 */ _ = ex; }
    }

    private async Task<string?> PickZipAsync()
    {
        try
        {
            var files = await MainWindowRef.PickFilesAsync("*.zip");
            return files?.FirstOrDefault();
        }
        catch { return null; }
    }
}
