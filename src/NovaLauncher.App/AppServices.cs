using NovaLauncher.Core.Configuration;
using NovaLauncher.Core.Services;

namespace NovaLauncher.App;

/// <summary>全局服务容器：初始化配置与 Core 各服务。</summary>
public static class AppServices
{
    public static LauncherConfig Config { get; private set; } = null!;
    public static GameDir Dir { get; private set; } = null!;
    public static ManifestService Manifest { get; private set; } = null!;
    public static VersionService Versions { get; private set; } = null!;
    public static DownloadService Downloads { get; private set; } = null!;
    public static AccountService Accounts { get; private set; } = null!;
    public static LaunchService Launch { get; private set; } = null!;
    public static ModService Mods { get; private set; } = null!;
    public static ServerService Servers { get; private set; } = null!;
    public static PackService Packs { get; private set; } = null!;
    public static LoaderService Loaders { get; private set; } = null!;
    public static SkinService Skins { get; private set; } = null!;

    public static string MirrorName =>
        Config.DownloadMirror == 1 ? Core.Configuration.DownloadMirror.Bmclapi : Core.Configuration.DownloadMirror.Official;

    public static void Init()
    {
        // 游戏根目录：用户目录下 .novalauncher（跨平台统一）
        var userDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var gameDir = Path.Combine(userDir, ".novalauncher");

        // 尝试加载已有配置，否则默认
        var cfgPath = Path.Combine(gameDir, "launcher_config.json");
        Config = LauncherConfig.Load(cfgPath) ?? LauncherConfig.CreateDefault(gameDir);

        Dir = new GameDir(Config);
        Dir.EnsureDirectories();
        Config.Save();

        var mirror = MirrorName;
        Manifest = new ManifestService(mirror);
        Versions = new VersionService(Config, mirror);
        Downloads = new DownloadService(Config, mirror);
        Accounts = new AccountService();
        Launch = new LaunchService(Config);
        Mods = new ModService(Config);
        Servers = new ServerService(Config);
        Packs = new PackService(Config);
        Loaders = new LoaderService(Config, mirror);
        Skins = new SkinService(Dir);
    }

    public static void SaveConfig() => Config.Save();
}
