using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using NovaLauncher.Core.Services;

namespace NovaLauncher.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ApplyOpacity();
        ApplyLiquidGlass();
    }

    private void ApplyOpacity()
    {
        var c = AppServices.Config;
        int bgAlpha = (int)(c.BackgroundOpacity / 100.0 * 255);
        NavPanel.Background = new SolidColorBrush(Color.FromArgb((byte)bgAlpha, 255, 255, 255));
        ContentPanel.Background = new SolidColorBrush(Color.FromArgb((byte)bgAlpha, 255, 255, 255));
    }

    private void ApplyLiquidGlass()
    {
        var c = AppServices.Config;
        if (c.LiquidGlassMode)
        {
            // 液态玻璃：更透明 + 毛玻璃感
            NavPanel.Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
            ContentPanel.Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            TransparencyLevelHint = new[] { Avalonia.Controls.WindowTransparencyLevel.AcrylicBlur };
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            BeginMoveDrag(e);
        }
    }

    private void MinBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaxBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
