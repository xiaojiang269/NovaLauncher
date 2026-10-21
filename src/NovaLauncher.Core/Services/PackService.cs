using System.IO.Compression;
using NovaLauncher.Core.Configuration;
using NovaLauncher.Core.Models;
using Newtonsoft.Json;

namespace NovaLauncher.Core.Services;

/// <summary>整合包导入导出（NovaPack 自有格式）。</summary>
public sealed class PackService
{
    private readonly GameDir _dir;

    public PackService(LauncherConfig config) => _dir = new GameDir(config);

    /// <summary>导出某版本为整合包 zip。返回导出文件路径。</summary>
    public string Export(string versionId, string packName, string? description, string destZip)
    {
        var versionJson = _dir.VersionJson(versionId);
        var versionJar = _dir.VersionJar(versionId);
        var modsDir = _dir.VersionModsDir(versionId);
        if (!File.Exists(versionJson)) throw new InvalidOperationException($"版本 {versionId} 缺少 version json，无法导出。");

        if (File.Exists(destZip)) File.Delete(destZip);
        Directory.CreateDirectory(Path.GetDirectoryName(destZip)!);

        using var zip = ZipFile.Open(destZip, ZipArchiveMode.Create);

        // manifest
        var manifest = new PackManifest
        {
            PackName = packName,
            VersionId = versionId,
            GameVersion = versionId,
            Description = description ?? "",
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        };
        var manifestEntry = zip.CreateEntry("manifest.json");
        using (var sw = new StreamWriter(manifestEntry.Open()))
            sw.Write(JsonConvert.SerializeObject(manifest, Formatting.Indented));

        // version.json
        zip.CreateEntryFromFile(versionJson, "version.json");
        // version.jar（如存在）
        if (File.Exists(versionJar))
            zip.CreateEntryFromFile(versionJar, "version.jar");
        // mods
        if (Directory.Exists(modsDir))
        {
            foreach (var f in Directory.GetFiles(modsDir))
                zip.CreateEntryFromFile(f, $"mods/{Path.GetFileName(f)}");
        }

        return destZip;
    }

    /// <summary>导入整合包 zip 到本地版本。返回版本 id。</summary>
    public string Import(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);

        var manifestEntry = zip.Entries.FirstOrDefault(e => e.FullName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                            ?? throw new InvalidOperationException("不是有效的 NovaPack 整合包（缺少 manifest.json）。");
        PackManifest manifest;
        using (var sr = new StreamReader(manifestEntry.Open()))
            manifest = JsonConvert.DeserializeObject<PackManifest>(sr.ReadToEnd()) ?? new PackManifest();

        var versionId = string.IsNullOrEmpty(manifest.VersionId) ? "imported-pack" : manifest.VersionId;
        var vDir = _dir.VersionDir(versionId);
        Directory.CreateDirectory(vDir);

        // version.json
        var vj = zip.GetEntry("version.json") ?? zip.GetEntry($"{versionId}.json");
        if (vj != null)
        {
            var dest = _dir.VersionJson(versionId);
            using var src = vj.Open();
            using var dst = File.Create(dest);
            src.CopyTo(dst);
        }

        // version.jar
        var vjar = zip.GetEntry("version.jar") ?? zip.GetEntry($"{versionId}.jar");
        if (vjar != null)
        {
            using var src = vjar.Open();
            using var dst = File.Create(_dir.VersionJar(versionId));
            src.CopyTo(dst);
        }

        // mods
        var modsDir = _dir.VersionModsDir(versionId);
        foreach (var e in zip.Entries.Where(e => e.FullName.StartsWith("mods/", StringComparison.OrdinalIgnoreCase) && !e.FullName.EndsWith("/")))
        {
            var name = Path.GetFileName(e.FullName);
            var dest = Path.Combine(modsDir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using var src = e.Open();
            using var dst = File.Create(dest);
            src.CopyTo(dst);
        }

        return versionId;
    }

    private sealed class PackManifest
    {
        public string PackName { get; set; } = "";
        public string VersionId { get; set; } = "";
        public string GameVersion { get; set; } = "";
        public string Description { get; set; } = "";
        public string CreatedAt { get; set; } = "";
    }
}
