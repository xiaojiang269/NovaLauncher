using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLauncher.Core.Configuration;

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
}
