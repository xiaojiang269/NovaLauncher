using NovaLauncher.Core.Configuration;
using NovaLauncher.Core.Models;

namespace NovaLauncher.Core.Services;

/// <summary>模组管理。</summary>
public sealed class ModService
{
    private readonly GameDir _dir;

    public ModService(LauncherConfig config) => _dir = new GameDir(config);

    /// <summary>列出指定版本 mods 目录下的文件。</summary>
    public List<ModInfo> ListMods(string versionId)
    {
        var modsDir = _dir.VersionModsDir(versionId);
        if (!Directory.Exists(modsDir)) return new List<ModInfo>();
        return Directory.GetFiles(modsDir)
            .Select(f => new FileInfo(f))
            .OrderBy(f => f.Name)
            .Select(f => new ModInfo
            {
                FileName = f.Name,
                Size = f.Length,
                ModifiedAt = f.LastWriteTime,
            })
            .ToList();
    }

    /// <summary>启用/禁用模组（通过 .disabled 后缀）。</summary>
    public bool SetEnabled(string versionId, string fileName, bool enabled)
    {
        var modsDir = _dir.VersionModsDir(versionId);
        var full = Path.Combine(modsDir, fileName);
        if (!File.Exists(full)) return false;
        var target = enabled
            ? Path.Combine(modsDir, fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? fileName[..^9] : fileName)
            : Path.Combine(modsDir, fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".disabled");
        if (string.Equals(full, target, StringComparison.OrdinalIgnoreCase)) return true;
        File.Move(full, target);
        return true;
    }

    /// <summary>删除模组。</summary>
    public bool Delete(string versionId, string fileName)
    {
        var full = Path.Combine(_dir.VersionModsDir(versionId), fileName);
        if (!File.Exists(full)) return false;
        File.Delete(full);
        return true;
    }

    /// <summary>模组目录路径（用于「打开文件夹」）。</summary>
    public string ModsDir(string versionId)
    {
        var d = _dir.VersionModsDir(versionId);
        Directory.CreateDirectory(d);
        return d;
    }
}
