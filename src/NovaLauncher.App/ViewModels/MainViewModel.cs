using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLauncher.Core.Services;

namespace NovaLauncher.App.ViewModels;

/// <summary>侧边栏导航项。</summary>
public sealed class NavItem
{
    public string Title { get; }
    public string Icon { get; }
    public ViewModelBase ViewModel { get; }
    public NavItem(string title, string icon, ViewModelBase vm) { Title = title; Icon = icon; ViewModel = vm; }
}

public partial class MainViewModel : ViewModelBase
{
    public List<NavItem> Pages { get; }

    [ObservableProperty]
    private ViewModelBase currentPage;

    [ObservableProperty]
    private NavItem selectedNav;

    // 更新提示
    [ObservableProperty] private bool showUpdateBanner;
    [ObservableProperty] private string updateBannerText = "";

    public string CurrentVersionText => $"v{AppVersion.Current}";

    public MainViewModel()
    {
        Pages = new List<NavItem>
        {
            new("主页", "🏠", new HomeViewModel()),
            new("版本管理", "📦", new VersionsViewModel()),
            new("模组管理", "🧩", new ModsViewModel()),
            new("模组下载", "⬇️", new ModDownloadViewModel()),
            new("皮肤库", "🎨", new SkinViewModel()),
            new("存档管理", "🗺️", new WorldViewModel()),
            new("资源与光影", "🎨", new ResourceViewModel()),
            new("联机", "🔗", new LanPlayViewModel()),
            new("服务器", "🖥️", new ServersViewModel()),
            new("设置", "⚙️", new SettingsViewModel()),
        };
        SelectedNav = Pages[0];
        CurrentPage = Pages[0].ViewModel;

        // 启动后静默检查更新
        _ = CheckUpdateSilentlyAsync();
    }

    private async Task CheckUpdateSilentlyAsync()
    {
        try
        {
            var info = await AppServices.Updates.CheckAsync();
            if (info == null) return;
            if (info.IsCurrent) return;
            ShowUpdateBanner = true;
            UpdateBannerText = $"发现新版本 v{info.Version}（当前 v{AppVersion.Current}）。可到「设置」页下载更新。";
        }
        catch { }
    }

    [RelayCommand]
    private void DismissUpdate() => ShowUpdateBanner = false;

    partial void OnSelectedNavChanged(NavItem value)
    {
        if (value != null)
            CurrentPage = value.ViewModel;
    }
}
