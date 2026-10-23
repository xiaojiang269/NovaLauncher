using NovaLauncher.Core.Configuration;

namespace NovaLauncher.Core.Services;

/// <summary>资源包与光影管理。资源包存放于版本目录 resourcepacks/，光影存放于 shaderpacks/。</summary>
public sealed class ResourceService
{
    private readonly GameDir _dir;

    public ResourceService(LauncherConfig config) => _dir = new GameDir(config);

    /// <summary>列出某版本资源包目录下的文件（zip 或文件夹）。</summary>
    public List<ResourceEntry> List(string versionId, ResourceKind kind)
    {
        var dir = KindDir(versionId, kind);
        var list = new List<ResourceEntry>();
        if (!Directory.Exists(dir)) return list;
        foreach (var f in Directory.GetFiles(dir))
        {
            var fi = new FileInfo(f);
            list.Add(new ResourceEntry { FileName = fi.Name, Path = f, SizeBytes = fi.Length, IsFolder = false, Kind = kind, ModifiedAt = fi.LastWriteTime });
        }
        foreach (var d in Directory.GetDirectories(dir))
        {
            var di = new DirectoryInfo(d);
            list.Add(new ResourceEntry { FileName = di.Name, Path = d, SizeBytes = DirSize(d), IsFolder = true, Kind = kind, ModifiedAt = di.LastWriteTime });
        }
        return list.OrderBy(r => r.FileName).ToList();
    }

    /// <summary>启用/禁用（通过 .disabled 后缀）。</summary>
    public bool SetEnabled(string versionId, ResourceKind kind, string fileName, bool enabled)
    {
        var dir = KindDir(versionId, kind);
        var full = Path.Combine(dir, fileName);
        if (!File.Exists(full) && !Directory.Exists(full)) return false;
        var suffix = ".disabled";
        var target = enabled
            ? Path.Combine(dir, fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? fileName[..^suffix.Length] : fileName)
            : Path.Combine(dir, fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? fileName : fileName + suffix);
        if (string.Equals(full, target, StringComparison.OrdinalIgnoreCase)) return true;
        File.Move(full, target);
        return true;
    }

    /// <summary>删除资源包/光影。</summary>
    public void Delete(string versionId, ResourceKind kind, string fileName)
    {
        var full = Path.Combine(KindDir(versionId, kind), fileName);
        if (Directory.Exists(full)) Directory.Delete(full, true);
        else if (File.Exists(full)) File.Delete(full);
    }

    /// <summary>导入资源包/光影 zip 到对应目录。返回文件名。</summary>
    public string Import(string versionId, ResourceKind kind, string zipPath)
    {
        var dir = KindDir(versionId, kind);
        Directory.CreateDirectory(dir);
        var name = Path.GetFileName(zipPath);
        var dest = Path.Combine(dir, name);
        if (File.Exists(dest)) File.Delete(dest);
        File.Copy(zipPath, dest);
        return name;
    }

    /// <summary>打开目录。</summary>
    public string OpenFolder(string versionId, ResourceKind kind)
    {
        var d = KindDir(versionId, kind);
        Directory.CreateDirectory(d);
        return d;
    }

    private string KindDir(string versionId, ResourceKind kind)
    {
        var sub = kind == ResourceKind.ResourcePack ? "resourcepacks" : "shaderpacks";
        return _dir.VersionSubDir(versionId, sub);
    }

    private static long DirSize(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
        }
        catch { return 0; }
    }
}

public enum ResourceKind
{
    ResourcePack = 0,
    ShaderPack = 1,
}

/// <summary>资源包/光影条目。</summary>
public sealed class ResourceEntry
{
    public string FileName { get; set; } = "";
    public string Path { get; set; } = "";
    public long SizeBytes { get; set; }
    public bool IsFolder { get; set; }
    public ResourceKind Kind { get; set; }
    public DateTime ModifiedAt { get; set; }

    public bool Enabled => !FileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
    public string DisplayName => FileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? FileName[..^9] : FileName;
    public string TypeText => IsFolder ? "文件夹" : "文件";
    public string SizeText
    {
        get
        {
            var b = SizeBytes;
            if (b >= 1 << 30) return $"{b / (double)(1 << 30):0.0} GB";
            if (b >= 1 << 20) return $"{b / (double)(1 << 20):0.0} MB";
            if (b >= 1 << 10) return $"{b / (double)(1 << 10):0} KB";
            return $"{b} B";
        }
    }
}
