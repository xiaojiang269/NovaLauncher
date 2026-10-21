namespace NovaLauncher.Core.Models;

/// <summary>模组（或任何 mods 目录下的文件）信息。</summary>
public sealed class ModInfo
{
    public string FileName { get; set; } = "";
    public long Size { get; set; }
    public DateTime ModifiedAt { get; set; }
    /// <summary>显示名称（由文件名推断，可后续用 mod 元数据完善）。</summary>
    public string DisplayName => Path.GetFileNameWithoutExtension(FileName);
    /// <summary>是否启用（默认启用，非 .disabled 后缀）。</summary>
    public bool Enabled => !FileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
    /// <summary>模组类型：mod / coremod / resourcepack / datapack 等，按扩展名推断。</summary>
    public string Kind => InferKind(FileName);

    public string SizeText => Core.Models.DownloadItem.FormatSize(Size);

    private static string InferKind(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".jar" => "模组",
            ".zip" => "资源包",
            ".litemod" => "LiteMod",
            _ => "文件",
        };
    }
}
