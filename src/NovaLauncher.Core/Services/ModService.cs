using System.IO.Compression;
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

    /// <summary>读取 jar 的模组标识（id / 版本 / 显示名）。供版本切换、冲突识别使用。</summary>
    public ModIdentity? ReadIdentity(string versionId, string fileName)
    {
        var full = Path.Combine(_dir.VersionModsDir(versionId), fileName);
        if (!File.Exists(full) || !full.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(full);
            // fabric.mod.json
            var fabric = zip.GetEntry("fabric.mod.json");
            if (fabric != null)
            {
                using var sr = new StreamReader(fabric.Open());
                using var doc = System.Text.Json.JsonDocument.Parse(sr.ReadToEnd());
                var r = doc.RootElement;
                var id = r.TryGetProperty("id", out var i) ? i.GetString() : null;
                var ver = r.TryGetProperty("version", out var v) ? v.GetString() : null;
                var name = r.TryGetProperty("name", out var nn) ? nn.GetString() : null;
                if (!string.IsNullOrEmpty(id)) return new ModIdentity(id, ver, name, "fabric");
            }
            // mods.toml
            var toml = zip.GetEntry("META-INF/mods.toml");
            if (toml != null)
            {
                using var sr = new StreamReader(toml.Open());
                var text = sr.ReadToEnd();
                var id = System.Text.RegularExpressions.Regex.Match(text, @"modId\s*=\s*""([^""]+)""").Groups[1].Value;
                var ver = System.Text.RegularExpressions.Regex.Match(text, @"version\s*=\s*""([^""]+)""").Groups[1].Value;
                if (!string.IsNullOrEmpty(id)) return new ModIdentity(id, ver, null, "forge");
            }
            // mcmod.info
            var info = zip.GetEntry("mcmod.info");
            if (info != null)
            {
                using var sr = new StreamReader(info.Open());
                using var doc = System.Text.Json.JsonDocument.Parse(sr.ReadToEnd());
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var e in doc.RootElement.EnumerateArray())
                    {
                        var id2 = e.TryGetProperty("modid", out var i2) ? i2.GetString() : null;
                        var ver2 = e.TryGetProperty("version", out var v2) ? v2.GetString() : null;
                        var name2 = e.TryGetProperty("name", out var nn2) ? nn2.GetString() : null;
                        if (!string.IsNullOrEmpty(id2)) return new ModIdentity(id2, ver2, name2, "forge");
                    }
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>一键提取：扫描 mods 目录内嵌套的 jar（如 .zip 资源包或 .litemod 内附带的模组），把其中的 .jar 提取到 mods 目录。</summary>
    public int ExtractNested(string versionId)
    {
        var modsDir = _dir.VersionModsDir(versionId);
        if (!Directory.Exists(modsDir)) return 0;
        int extracted = 0;
        foreach (var file in Directory.GetFiles(modsDir))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is not (".zip" or ".litemod" or ".jar")) continue;
            var name = Path.GetFileName(file);
            if (name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                using var zip = System.IO.Compression.ZipFile.OpenRead(file);
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.Contains('/') || entry.FullName.Contains('\\')) continue; // 仅顶层
                    if (!entry.FullName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) continue;
                    var target = Path.Combine(modsDir, entry.Name);
                    if (File.Exists(target)) continue;
                    entry.ExtractToFile(target, true);
                    extracted++;
                }
            }
            catch { }
        }
        return extracted;
    }
}

/// <summary>模组标识（从 jar 清单读取）。</summary>
public sealed record ModIdentity(string Id, string? Version, string? DisplayName, string? Loader)
{
    public string Display => string.IsNullOrEmpty(DisplayName) ? Id : DisplayName!;
}
