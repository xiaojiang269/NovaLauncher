using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NovaLauncher.App.ViewModels;

/// <summary>侧边栏导航项。</summary>
public sealed class NavItem
{
    public string Title { get; }
    public ViewModelBase ViewModel { get; }
    public NavItem(string title, ViewModelBase vm) { Title = title; ViewModel = vm; }
}

public partial class MainViewModel : ViewModelBase
{
    public List<NavItem> Pages { get; }

    [ObservableProperty]
    private ViewModelBase currentPage;

    public MainViewModel()
    {
        Pages = new List<NavItem>
        {
            new("主页", new HomeViewModel()),
            new("版本管理", new VersionsViewModel()),
            new("模组管理", new ModsViewModel()),
            new("模组下载", new ModDownloadViewModel()),
            new("联机", new LanPlayViewModel()),
            new("服务器", new ServersViewModel()),
            new("设置", new SettingsViewModel()),
        };
        CurrentPage = Pages[0].ViewModel;
    }

    [RelayCommand]
    private void Navigate(object? param)
    {
        if (param is NavItem item)
            CurrentPage = item.ViewModel;
    }
}
