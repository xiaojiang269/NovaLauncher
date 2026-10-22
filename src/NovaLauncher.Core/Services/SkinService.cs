using System.Text.Json;
using NovaLauncher.Core.Configuration;

namespace NovaLauncher.Core.Services;

/// <summary>一条已导入的皮肤记录。</summary>
public sealed class SkinEntry
{
    public string Name { get; set; } = "";          // 皮肤名（不含扩展名）
    public string FileName { get; set; } = "";      // 在 skins 目录下的文件名
    public string ImportedAt { get; set; } = "";    // 导入时间
    public bool IsActive { get; set; }              // 是否当前账户正在使用
}

/// <summary>
/// 皮肤库管理：导入本地皮肤、记录所有已导入皮肤、选用/删除。
/// 皮肤文件存放于 {GameDir}/skins/，清单 skins.json。
/// </summary>
public sealed class SkinService
{
    private readonly GameDir _dir;
    private readonly string _manifestPath;
    private List<SkinEntry> _entries = new();

    public string SkinsDir => Path.Combine(_dir.Root, "skins");

    public SkinService(GameDir dir)
    {
        _dir = dir;
        _manifestPath = Path.Combine(SkinsDir, "skins.json");
        Load();
    }

    private void Load()
    {
        _entries = new List<SkinEntry>();
        if (!File.Exists(_manifestPath)) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<SkinEntry>>(File.ReadAllText(_manifestPath));
            if (list != null) _entries = list;
        }
        catch { /* 损坏则忽略 */ }
    }

    private void Save()
    {
        Directory.CreateDirectory(SkinsDir);
        File.WriteAllText(_manifestPath, JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>全部已导入皮肤。</summary>
    public IReadOnlyList<SkinEntry> List() => _entries;

    /// <summary>把本地皮肤文件导入皮肤库。返回新条目；文件非法/重复返回 null。</summary>
    public SkinEntry? Import(string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath)) return null;
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".gif")) return null;

        Directory.CreateDirectory(SkinsDir);
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        // 避免重名覆盖：同名追加序号
        var used = _entries.Select(e => e.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fileName = Path.GetFileName(sourcePath);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var finalName = fileName;
        int i = 1;
        while (used.Contains(finalName))
        {
            finalName = $"{baseName}_{i++}{ext}";
        }

        var dest = Path.Combine(SkinsDir, finalName);
        File.Copy(sourcePath, dest, true);

        var entry = new SkinEntry
        {
            Name = baseName,
            FileName = finalName,
            ImportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            IsActive = false,
        };
        _entries.Add(entry);
        Save();
        return entry;
    }

    /// <summary>选用某皮肤为当前账户皮肤。返回是否成功。</summary>
    public bool Activate(string fileName)
    {
        var target = _entries.FirstOrDefault(e => e.FileName == fileName);
        if (target == null) return false;
        foreach (var e in _entries) e.IsActive = false;
        target.IsActive = true;
        Save();
        return true;
    }

    /// <summary>删除某皮肤（含文件）。返回是否成功。</summary>
    public bool Delete(string fileName)
    {
        var target = _entries.FirstOrDefault(e => e.FileName == fileName);
        if (target == null) return false;
        _entries.Remove(target);
        try { File.Delete(Path.Combine(SkinsDir, target.FileName)); } catch { }
        Save();
        return true;
    }

    /// <summary>当前激活的皮肤绝对路径（无则返回 null）。</summary>
    public string? ActiveSkinPath()
    {
        var active = _entries.FirstOrDefault(e => e.IsActive);
        if (active == null) return null;
        var p = Path.Combine(SkinsDir, active.FileName);
        return File.Exists(p) ? p : null;
    }

    /// <summary>某个皮肤的绝对路径。</summary>
    public string SkinPath(SkinEntry e) => Path.Combine(SkinsDir, e.FileName);
}
