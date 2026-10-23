using Newtonsoft.Json;

namespace NovaLauncher.Core.Configuration;

/// <summary>启动器全局配置。</summary>
public sealed class LauncherConfig
{
    /// <summary>配置文件路径（默认在游戏根目录下）。</summary>
    [JsonIgnore]
    public string ConfigFilePath { get; set; } = "";

    /// <summary>游戏根目录（.minecraft 等价物）。</summary>
    public string GameDir { get; set; } = "";

    /// <summary>当前选中的版本名。</summary>
    public string SelectedVersion { get; set; } = "";

    /// <summary>当前登录账号的用户名。</summary>
    public string AccountName { get; set; } = "";

    /// <summary>账号 UUID（离线生成）。</summary>
    public string AccountUuid { get; set; } = "";

    /// <summary>账号类型：offline / microsoft。</summary>
    public string AccountType { get; set; } = "offline";

    /// <summary>Java 运行时可执行文件路径（空则自动查找）。</summary>
    public string JavaPath { get; set; } = "";

    /// <summary>最小内存（MB）。</summary>
    public int MinMemoryMb { get; set; } = 1024;

    /// <summary>最大内存（MB）。</summary>
    public int MaxMemoryMb { get; set; } = 4096;

    /// <summary>窗口宽度（0 为游戏默认）。</summary>
    public int WindowWidth { get; set; }

    /// <summary>窗口高度（0 为游戏默认）。</summary>
    public int WindowHeight { get; set; }

    /// <summary>是否全屏。</summary>
    public bool Fullscreen { get; set; }

    /// <summary>额外 JVM 参数。</summary>
    public string ExtraJvmArgs { get; set; } = "";

    /// <summary>是否显示调试日志。</summary>
    public bool ShowGameLog { get; set; } = true;

    /// <summary>下载镜像源（0=官方，1=BMCLAPI）。</summary>
    public int DownloadMirror { get; set; }

    /// <summary>微软登录 ClientId（为空则不启用微软登录）。</summary>
    public string MicrosoftClientId { get; set; } = "";

    /// <summary>外置登录（第三方认证服务器）API 地址，如 https://littleskin.cn/api/yggdrasil。</summary>
    public string ExternalAuthServer { get; set; } = "";
    /// <summary>外置登录 access token（由 authenticate 获取，有效期较短）。</summary>
    public string ExternalAccessToken { get; set; } = "";

    /// <summary>CurseForge API key（可选，填入后模组下载可用 CurseForge 源）。</summary>
    public string CurseForgeApiKey { get; set; } = "";

    /// <summary>版本隔离：开启后每个版本使用独立游戏目录（versions/{id}/），存档/配置/资源包与其他版本隔离。</summary>
    public bool VersionIsolation { get; set; }

    public static LauncherConfig CreateDefault(string gameDir)
    {
        return new LauncherConfig
        {
            ConfigFilePath = Path.Combine(gameDir, "launcher_config.json"),
            GameDir = gameDir,
        };
    }

    public static LauncherConfig? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var cfg = JsonConvert.DeserializeObject<LauncherConfig>(File.ReadAllText(path));
            if (cfg != null)
            {
                cfg.ConfigFilePath = path;
                if (string.IsNullOrEmpty(cfg.GameDir)) cfg.GameDir = Path.GetDirectoryName(path) ?? "";
            }
            return cfg;
        }
        catch
        {
            return null;
        }
    }

    public void Save()
    {
        if (string.IsNullOrEmpty(ConfigFilePath)) return;
        var dir = Path.GetDirectoryName(ConfigFilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(ConfigFilePath, JsonConvert.SerializeObject(this, Formatting.Indented));
    }
}
