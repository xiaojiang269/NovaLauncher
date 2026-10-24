using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using NovaLauncher.App.ViewModels;
using NovaLauncher.App.Views;

namespace NovaLauncher.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 先展示启动画面（Logo + 加载动画），短暂停留后进入主界面（对齐 PCL 启动动画）
            var splash = new SplashWindow();
            splash.Show();

            _ = Task.Run(async () =>
            {
                await Task.Delay(2300); // 启动动画展示时长
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    AppServices.Init(); // 初始化全局服务

                    var mainWindow = new MainWindow
                    {
                        DataContext = new MainViewModel(),
                    };
                    MainWindowRef.Instance = mainWindow;
                    desktop.MainWindow = mainWindow;
                    mainWindow.Show();
                    splash.Close();
                });
            });
        }

        base.OnFrameworkInitializationCompleted();
    }
}
