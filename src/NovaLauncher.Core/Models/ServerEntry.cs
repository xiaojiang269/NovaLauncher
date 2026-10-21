namespace NovaLauncher.Core.Models;

/// <summary>服务器快捷入口（servers.json）。</summary>
public sealed class ServerEntry
{
    public string Name { get; set; } = "";
    /// <summary>服务器地址（不含端口）。</summary>
    public string Address { get; set; } = "";
    /// <summary>端口，默认 25565。</summary>
    public int Port { get; set; } = 25565;
    public string Description { get; set; } = "";
    public string? IconPath { get; set; }

    [Newtonsoft.Json.JsonIgnore]
    public string FullAddress => string.IsNullOrEmpty(Address) ? "" : $"{Address}:{Port}";
}
