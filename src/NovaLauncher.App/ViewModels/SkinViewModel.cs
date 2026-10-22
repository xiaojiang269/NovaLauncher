using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLauncher.Core.Services;

namespace NovaLauncher.App.ViewModels;

/// <summary>皮肤库视图模型：导入皮肤、记录所有已导入皮肤、预览、选用/删除。</summary>
public partial class SkinViewModel : ViewModelBase
{
    private readonly SkinService _skins = AppServices.Skins;

    public ObservableCollection<SkinItem> Skins { get; } = new();

    [ObservableProperty] private SkinItem? selected;
    [ObservableProperty] private string status = "就绪";

    public SkinViewModel()
    {
        foreach (var e in _skins.List()) Skins.Add(new SkinItem(e, _skins.SkinPath(e)));
    }

    /// <summary>导入本地皮肤文件。</summary>
    [RelayCommand]
    private async Task ImportAsync()
    {
        var picked = await MainWindowRef.PickFilesAsync("*.png;*.jpg;*.jpeg;*.gif");
        if (picked == null || picked.Count == 0) return;
        var entry = _skins.Import(picked[0]);
        if (entry == null)
        {
            Status = "导入失败：仅支持 PNG / JPG / GIF 图片";
            return;
        }
        Skins.Add(new SkinItem(entry, _skins.SkinPath(entry)));
        Status = $"已导入皮肤「{entry.Name}」";
        Selected = Skins.LastOrDefault();
    }

    /// <summary>选用为当前账户皮肤。</summary>
    [RelayCommand]
    private void Activate()
    {
        if (Selected == null) return;
        if (_skins.Activate(Selected.Source.FileName))
        {
            foreach (var s in Skins) s.IsActive = s.Source.FileName == Selected.Source.FileName;
            Status = $"已选用皮肤「{Selected.Source.Name}」，下次启动游戏生效。";
        }
    }

    /// <summary>删除皮肤。</summary>
    [RelayCommand]
    private void Delete()
    {
        if (Selected == null) return;
        var name = Selected.Source.Name;
        if (_skins.Delete(Selected.Source.FileName))
        {
            Skins.Remove(Selected);
            Selected = null;
            Status = $"已删除皮肤「{name}」。";
        }
    }
}

/// <summary>UI 层的皮肤条目（含预览位图）。</summary>
public partial class SkinItem : ObservableObject
{
    public SkinEntry Source { get; }
    public string Name => Source.Name;
    public string ImportedAt => Source.ImportedAt;

    public Bitmap? Preview { get; }

    [ObservableProperty] private bool isActive;

    public SkinItem(SkinEntry entry, string path)
    {
        Source = entry;
        IsActive = entry.IsActive;
        try
        {
            if (File.Exists(path))
                Preview = new Bitmap(path);
        }
        catch { Preview = null; }
    }
}
