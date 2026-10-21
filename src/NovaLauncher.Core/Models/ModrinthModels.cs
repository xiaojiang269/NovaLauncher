namespace NovaLauncher.Core.Models;

/// <summary>Modrinth 搜索结果的模组条目。</summary>
public sealed class ModrinthProject
{
    public string ProjectId { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public int Downloads { get; set; }
    public string? IconUrl { get; set; }
    public string[] Categories { get; set; } = Array.Empty<string>();
    /// <summary>该模组支持的游戏版本（来自搜索命中）。</summary>
    public string[] Versions { get; set; } = Array.Empty<string>();
}

/// <summary>Modrinth 某个模组的发布版本。</summary>
public sealed class ModrinthVersion
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string VersionNumber { get; set; } = "";
    public string[] GameVersions { get; set; } = Array.Empty<string>();
    public string[] Loaders { get; set; } = Array.Empty<string>();
    public List<ModrinthFile> Files { get; set; } = new();
}

public sealed class ModrinthFile
{
    public string Url { get; set; } = "";
    public string Filename { get; set; } = "";
    public long Size { get; set; }
    public bool Primary { get; set; }
}
