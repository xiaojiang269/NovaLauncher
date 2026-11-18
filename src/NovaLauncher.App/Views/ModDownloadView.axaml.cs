using Avalonia.Controls;

namespace NovaLauncher.App.Views;

public partial class ModDownloadView : UserControl
{
    public ModDownloadView()
    {
        InitializeComponent();
        // 进入页面即拉取热门模组，直接在本页展示，不必先搜索
        AttachedToVisualTree += (_, __) =>
        {
            if (DataContext is ViewModels.ModDownloadViewModel vm && vm.Projects.Count == 0)
                vm.LoadFeaturedCommand.Execute(null);
        };
    }
}
