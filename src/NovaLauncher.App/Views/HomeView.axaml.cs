using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace NovaLauncher.App.Views;

public partial class HomeView : UserControl
{
    private bool _animated;

    public HomeView() => InitializeComponent();

    /// <summary>
    /// PCL 风格界面元素浮现动效：进入页面时卡片逐个淡入上移（staggered）。
    /// 初始状态由 XAML 定义（Opacity=0 + TranslateTransform.Y=18），
    /// 此处通过代码创建 Transitions，分帧改回正常值实现平滑错落浮现。
    /// </summary>
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_animated) return;

        var cards = new[]
        {
            FindCard("AccountCard"),
            FindCard("LaunchConfigCard"),
            FindCard("ProgressCard"),
            FindCard("CrashCard"),
            FindCard("GameLogCard"),
        };

        // 为每个可见卡片挂载过渡动画（透明度 + 上移归位）
        foreach (var c in cards)
        {
            if (c == null) continue;
            c.Transitions = new Transitions
            {
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(420) },
                new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(420) },
            };
        }

        _ = AnimateInAsync(cards);
        _animated = true;
    }

    private Border? FindCard(string name) => this.FindControl<Border>(name);

    private async Task AnimateInAsync(Border?[] cards)
    {
        // 等视觉树稳定后开始
        await Task.Delay(30);
        foreach (var c in cards)
        {
            if (c == null) continue;
            // 只有当前可见的卡片才浮现（进度/崩溃/日志按需显示）
            if (!c.IsVisible)
            {
                c.Opacity = 1;
                if (c.RenderTransform is TranslateTransform t) t.Y = 0;
                continue;
            }
            if (c.RenderTransform is TranslateTransform tt) tt.Y = 0;
            c.Opacity = 1;
            // 错落浮现：每张间隔 110ms
            await Task.Delay(110);
        }
    }
}
