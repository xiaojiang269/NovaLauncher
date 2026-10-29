using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLauncher.App.ViewModels;

namespace NovaLauncher.App.ViewModels;

public partial class MoreViewModel : ViewModelBase
{
    // 今日人品
    [ObservableProperty] private int todayLuck;
    [ObservableProperty] private string luckText = "";
    [ObservableProperty] private bool showEggResult = false;
    [ObservableProperty] private string eggText = "";

    private static readonly string[] LuckDescs = new[]
    {
        "欧皇附体，今天开箱必出传奇！",
        "运气爆棚，抽卡十连保底金！",
        "运势极佳，打怪掉落翻倍！",
        "小吉，今天挖矿必出钻石！",
        "平平淡淡，适合养老生存。",
        "运势一般，别赌别赌。",
        "小凶，今天小心苦力怕。",
        "大凶，别出门别开创造。",
        "霉运当头，今天适合下线。",
        "绝绝子，今天点啥啥爆炸！"
    };

    public MoreViewModel()
    {
        RollLuck();
    }

    [RelayCommand]
    private void RollLuck()
    {
        // 按日期生成固定人品（同一天内不变）
        var seed = DateTime.Now.ToString("yyyyMMdd").GetHashCode();
        var rng = new Random(seed);
        TodayLuck = rng.Next(1, 101);
        var idx = Math.Clamp((TodayLuck - 1) / 10, 0, LuckDescs.Length - 1);
        idx = LuckDescs.Length - 1 - idx;
        LuckText = LuckDescs[idx];
    }

    [RelayCommand]
    private async Task DontClickAsync()
    {
        // PCL2 经典彩蛋：三个确定按钮的弹窗
        var eggs = new[]
        {
            "你真的点了？那今天 MC 必炸一次！💥",
            "恭喜你获得「非酋之证」称号！🎭",
            "警告：再点一次，你的存档将变成泥土。🟫",
            "哈哈被骗了吧！PCL2 同款彩蛋~",
            "别点了别点了，再点启动器就变白了。⚪",
            "你发现了隐藏彩蛋！+100 人品值！✨"
        };
        var rng = new Random(DateTime.Now.Millisecond);
        var eggText = eggs[rng.Next(eggs.Length)];

        // 用 Avalonia 弹窗，三个确定按钮
        var window = MainWindowRef.Instance;
        var dialog = new Avalonia.Controls.Window
        {
            Title = "警告",
            Width = 360,
            Height = 200,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        var panel = new Avalonia.Controls.StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        var text = new Avalonia.Controls.TextBlock
        {
            Text = eggText,
            FontSize = 14,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        panel.Children.Add(text);
        var btnPanel = new Avalonia.Controls.StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Spacing = 10
        };
        for (int i = 0; i < 3; i++)
        {
            var btn = new Avalonia.Controls.Button { Content = "确定", MinWidth = 70 };
            btn.Click += (s, e) => dialog.Close();
            btnPanel.Children.Add(btn);
        }
        panel.Children.Add(btnPanel);
        dialog.Content = panel;
        await dialog.ShowDialog(window);
    }
}
