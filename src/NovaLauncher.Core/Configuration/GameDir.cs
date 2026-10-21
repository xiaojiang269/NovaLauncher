using NovaLauncher.Core.Configuration;

namespace NovaLauncher.Core.Configuration;

/// <summary>
/// 游戏目录结构管理。约定与官方/HMCL 一致的布局：
///   GameDir/
///     versions/{id}/{id}.json, {id}.jar, natives, mods
///     assets/indexes, objects
///     libraries/
///     modpacks/
///     java/
///     servers.json（服务器快捷入口）
/// </summary>
public sealed class GameDir
{
    private readonly LauncherConfig _config;

    public GameDir(LauncherConfig config) => _config = config;

    public string Root => _config.GameDir;
    public string Versions => Path.Combine(Root, "versions");
    public string Assets => Path.Combine(Root, "assets");
    public string AssetsIndexes => Path.Combine(Assets, "indexes");
    public string AssetsObjects => Path.Combine(Assets, "objects");
    public string Libraries => Path.Combine(Root, "libraries");
    public string Modpacks => Path.Combine(Root, "modpacks");
    public string Java => Path.Combine(Root, "java");
    public string Tools => Path.Combine(Root, "tools");
    public string ServersFile => Path.Combine(Root, "servers.json");

    public string VersionDir(string versionId) => Path.Combine(Versions, versionId);
    public string VersionJson(string versionId) => Path.Combine(VersionDir(versionId), $"{versionId}.json");
    public string VersionJar(string versionId) => Path.Combine(VersionDir(versionId), $"{versionId}.jar");
    public string NativesDir(string versionId) => Path.Combine(VersionDir(versionId), $"{versionId}-natives");
    public string VersionModsDir(string versionId) => Path.Combine(VersionDir(versionId), "mods");
    /// <summary>版本下的任意子目录（mods / shaderpacks / resourcepacks / datapacks）。</summary>
    public string VersionSubDir(string versionId, string sub) => Path.Combine(VersionDir(versionId), sub);

    /// <summary>创建全部目录。</summary>
    public void EnsureDirectories()
    {
        foreach (var d in new[] { Root, Versions, AssetsIndexes, AssetsObjects, Libraries, Modpacks, Java })
            Directory.CreateDirectory(d);
    }

    /// <summary>列出本地已安装的版本（有 json 的目录）。</summary>
    public List<string> ListInstalledVersions()
    {
        if (!Directory.Exists(Versions)) return new List<string>();
        return Directory.GetDirectories(Versions)
            .Select(Path.GetFileName)
            .Where(name => File.Exists(VersionJson(name!)))
            .OrderByDescending(n => n)
            .Select(n => n!)
            .ToList();
    }
}
