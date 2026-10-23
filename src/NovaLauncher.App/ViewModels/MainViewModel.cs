using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
            new("联机", "🔗", new LanPlayViewModel()),
            new("服务器", "🖥️", new ServersViewModel()),
            new("设置", "⚙️", new SettingsViewModel()),
        };
        SelectedNav = Pages[0];
        CurrentPage = Pages[0].ViewModel;
    }

    partial void OnSelectedNavChanged(NavItem value)
    {
        if (value != null)
            CurrentPage = value.ViewModel;
    }
}
