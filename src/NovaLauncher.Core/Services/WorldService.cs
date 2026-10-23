using System.IO.Compression;
using NovaLauncher.Core.Configuration;
using NovaLauncher.Core.Models;

namespace NovaLauncher.Core.Services;

/// <summary>存档（世界）管理。Minecraft 世界存放于游戏根目录 saves/ 下，每个子目录一个世界。</summary>
public sealed class WorldService
{
    private readonly GameDir _dir;
    private readonly LauncherConfig _config;

    public WorldService(LauncherConfig config)
    {
        _config = config;
        _dir = new GameDir(config);
    }

    /// <summary>saves 目录（游戏根目录下）。</summary>
    public string SavesDir
    {
        get
        {
            var d = Path.Combine(_dir.Root, "saves");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    /// <summary>列出本地所有世界（每个子目录一个世界）。</summary>
    public List<WorldInfo> ListWorlds()
    {
        var dir = SavesDir;
        var list = new List<WorldInfo>();
        foreach (var sub in Directory.GetDirectories(dir))
        {
            var name = Path.GetFileName(sub);
            // 跳过备份目录等系统目录
            if (name is "backups") continue;
            var levelDat = Path.Combine(sub, "level.dat");
            if (!File.Exists(levelDat)) continue;
            var info = new FileInfo(levelDat);
            list.Add(new WorldInfo
            {
                Name = ReadLevelName(sub) ?? name,
                FolderName = name,
                Path = sub,
                SizeBytes = DirSize(sub),
                LastPlayed = info.LastWriteTime,
                LevelDatModified = info.LastWriteTime,
            });
        }
        return list.OrderByDescending(w => w.LastPlayed).ToList();
    }

    /// <summary>从 level.dat 读取世界显示名（level.dat 是 NBT，直接读前若干字节取标题不可靠，退回文件夹名）。</summary>
    private static string? ReadLevelName(string dir)
    {
        try
        {
            var dat = Path.Combine(dir, "level.dat");
            if (!File.Exists(dat)) return null;
            // level.dat 是 gzip NBT，简单读取 UTF 片段中的可打印标题。稳健起见仅尝试。
            using var fs = File.OpenRead(dat);
            using var gz = new System.IO.Compression.GZipStream(fs, CompressionMode.Decompress);
            using var ms = new MemoryStream();
            gz.CopyTo(ms);
            var bytes = ms.ToArray();
            // 查找 "LevelName" 后的字符串
            var text = System.Text.Encoding.UTF8.GetString(bytes);
            const string key = "LevelName";
            var idx = text.IndexOf(key, StringComparison.Ordinal);
            if (idx >= 0)
            {
                // NBT 字符串：后 2 字节为长度，再跟 UTF-8 字节
                int pos = idx + key.Length;
                if (pos + 2 <= bytes.Length)
                {
                    int len = (bytes[pos] << 8) | bytes[pos + 1];
                    var name = System.Text.Encoding.UTF8.GetString(bytes, pos + 2, Math.Min(len, bytes.Length - pos - 2));
                    if (name.Length > 0 && name.Length < 64 && !name.Any(c => c == '\0' || char.IsControl(c)))
                        return name;
                }
            }
            return null;
        }
        catch { return null; }
    }

    /// <summary>打开世界文件夹（游戏内"打开存档文件夹"）。</summary>
    public string OpenFolder(string folderName)
    {
        var p = Path.Combine(SavesDir, folderName);
        if (!Directory.Exists(p)) throw new DirectoryNotFoundException("存档不存在：" + folderName);
        return p;
    }

    /// <summary>删除世界。</summary>
    public void Delete(string folderName)
    {
        var p = Path.Combine(SavesDir, folderName);
        if (!Directory.Exists(p)) throw new DirectoryNotFoundException("存档不存在：" + folderName);
        Directory.Delete(p, true);
    }

    /// <summary>导出世界为 ZIP。返回导出文件路径。</summary>
    public string Export(string folderName, string? destDir = null)
    {
        var src = Path.Combine(SavesDir, folderName);
        if (!Directory.Exists(src)) throw new DirectoryNotFoundException("存档不存在：" + folderName);
        var outDir = destDir ?? Path.Combine(_dir.Root, "exports");
        Directory.CreateDirectory(outDir);
        var zipPath = Path.Combine(outDir, $"{folderName}.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);
        ZipFile.CreateFromDirectory(src, zipPath, CompressionLevel.Optimal, false);
        return zipPath;
    }

    /// <summary>导入世界 ZIP（解压到 saves 下）。返回文件夹名。</summary>
    public string Import(string zipPath)
    {
        if (!File.Exists(zipPath)) throw new FileNotFoundException("ZIP 文件不存在", zipPath);
        var tmp = Path.Combine(Path.GetTempPath(), "novalauncher_world_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            ZipFile.ExtractToDirectory(zipPath, tmp);
            // 寻找包含 level.dat 的目录
            var candidates = new List<string>();
            foreach (var d in Directory.GetDirectories(tmp, "*", SearchOption.AllDirectories))
                if (File.Exists(Path.Combine(d, "level.dat"))) candidates.Add(d);
            // 也可能 zip 根直接是 world 内容
            if (File.Exists(Path.Combine(tmp, "level.dat"))) candidates.Insert(0, tmp);
            if (candidates.Count == 0) throw new InvalidDataException("压缩包内未找到有效存档（缺少 level.dat）");

            var best = candidates.OrderBy(c => Path.GetRelativePath(tmp, c).Count(ch => ch == Path.DirectorySeparatorChar)).First();
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(best));
            if (string.IsNullOrWhiteSpace(name) || name == Path.GetFileName(tmp)) name = "world_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // 避免同名冲突
            var dest = Path.Combine(SavesDir, name);
            var final = dest;
            var i = 1;
            while (Directory.Exists(final)) { final = Path.Combine(SavesDir, $"{name}_{i++}"); }
            Directory.Move(best, final);
            return Path.GetFileName(final);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }
    }

    /// <summary>备份世界到 backups 目录。返回备份路径。</summary>
    public string Backup(string folderName)
    {
        var src = Path.Combine(SavesDir, folderName);
        if (!Directory.Exists(src)) throw new DirectoryNotFoundException("存档不存在：" + folderName);
        var bkDir = Path.Combine(SavesDir, "backups");
        Directory.CreateDirectory(bkDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var zipPath = Path.Combine(bkDir, $"{folderName}_{stamp}.zip");
        ZipFile.CreateFromDirectory(src, zipPath, CompressionLevel.Optimal, false);
        return zipPath;
    }

    /// <summary>从备份 ZIP 恢复（导入方式）。返回世界名。</summary>
    public string RestoreFromBackup(string backupZip) => Import(backupZip);

    /// <summary>列出备份文件。</summary>
    public List<string> ListBackups()
    {
        var bk = Path.Combine(SavesDir, "backups");
        if (!Directory.Exists(bk)) return new List<string>();
        return Directory.GetFiles(bk, "*.zip").Select(Path.GetFileName).OrderByDescending(n => n).ToList()!;
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

/// <summary>世界信息。</summary>
public sealed class WorldInfo
{
    public string Name { get; set; } = "";
    public string FolderName { get; set; } = "";
    public string Path { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime LastPlayed { get; set; }
    public DateTime LevelDatModified { get; set; }
    public string SizeText => FormatSize(SizeBytes);

    private static string FormatSize(long b)
    {
        if (b >= 1 << 30) return $"{b / (double)(1 << 30):0.0} GB";
        if (b >= 1 << 20) return $"{b / (double)(1 << 20):0.0} MB";
        if (b >= 1 << 10) return $"{b / (double)(1 << 10):0} KB";
        return $"{b} B";
    }
}
