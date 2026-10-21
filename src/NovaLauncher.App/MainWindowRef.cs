using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace NovaLauncher.App;

/// <summary>持有主窗口引用，提供文件选择/保存/剪贴板等辅助。</summary>
public static class MainWindowRef
{
    public static Window? Instance { get; set; }

    /// <summary>复制文本到系统剪贴板。</summary>
    public static async Task<bool> CopyToClipboardAsync(string text)
    {
        if (Instance == null || string.IsNullOrEmpty(text)) return false;
        IClipboard? clipboard = Instance.Clipboard;
        if (clipboard == null) return false;
        await clipboard.SetTextAsync(text);
        return true;
    }

    public static async Task<List<string>?> PickFilesAsync(string pattern)
    {
        if (Instance == null) return null;
        var provider = Instance.StorageProvider;
        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "选择文件",
            FileTypeFilter = new[] { new FilePickerFileType("文件") { Patterns = new[] { pattern } } },
        });
        return files.Count > 0 ? files.Select(f => f.Path.LocalPath).ToList() : null;
    }

    public static async Task<string?> SaveFileAsync(string defaultName)
    {
        if (Instance == null) return null;
        var provider = Instance.StorageProvider;
        var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = defaultName,
        });
        return file?.Path.LocalPath;
    }
}
