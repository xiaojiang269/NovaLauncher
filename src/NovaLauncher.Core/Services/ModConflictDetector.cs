using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using NovaLauncher.Core.Models;

namespace NovaLauncher.Core.Services;

/// <summary>模组冲突检测：扫描 mods 目录，识别同一模组的多个 jar（可能冲突）。</summary>
public sealed class ModConflictDetector
{
    /// <summary>一个冲突候选文件（含解析出的模组标识）。</summary>
    public sealed record ConflictFile(string FileName, string? ModId, string? Version);

    /// <summary>同一 ModId 出现多次的文件组（即冲突）。</summary>
    public sealed record ConflictGroup(string ModId, string? DisplayName, List<ConflictFile> Files);

    /// <summary>扫描 mods 目录，返回所有冲突组（同一 modid 有多个文件的）。</summary>
    public List<ConflictGroup> Detect(string modsDir)
    {
        var map = new Dictionary<string, List<ConflictFile>>(StringComparer.OrdinalIgnoreCase);
        var display = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(modsDir)) return new List<ConflictGroup>();

        foreach (var file in Directory.GetFiles(modsDir).Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)))
        {
            if (Path.GetFileName(file).EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)) continue;
            var (id, ver, name) = ReadManifest(file);
            if (id == null) continue;
            var fileName = Path.GetFileName(file);
            if (!map.TryGetValue(id, out var list)) map[id] = list = new List<ConflictFile>();
            list.Add(new ConflictFile(fileName, id, ver));
            if (name != null && !display.ContainsKey(id)) display[id] = name;
        }

        return map
            .Where(kv => kv.Value.Count > 1)
            .Select(kv => new ConflictGroup(kv.Key, display.TryGetValue(kv.Key, out var n) ? n : null, kv.Value))
            .OrderBy(g => g.ModId)
            .ToList();
    }

    /// <summary>读取 jar 清单，返回 (modId, version, displayName)。</summary>
    private static (string?, string?, string?) ReadManifest(string jarPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(jarPath);
            // 1) Fabric: fabric.mod.json（根目录）
            var fabric = zip.GetEntry("fabric.mod.json");
            if (fabric != null)
            {
                using var sr = new StreamReader(fabric.Open());
                using var doc = JsonDocument.Parse(sr.ReadToEnd());
                var r = doc.RootElement;
                var id = r.TryGetProperty("id", out var i) ? i.GetString() : null;
                var ver = r.TryGetProperty("version", out var v) ? v.GetString() : null;
                var name = r.TryGetProperty("name", out var nn) ? nn.GetString() : null;
                return (id, ver, name);
            }
            // 2) Forge/NeoForge: META-INF/mods.toml（正则提取首个 modId / version）
            var toml = zip.GetEntry("META-INF/mods.toml");
            if (toml != null)
            {
                using var sr = new StreamReader(toml.Open());
                var text = sr.ReadToEnd();
                var id = Regex.Match(text, @"modId\s*=\s*""([^""]+)""").Groups[1].Value;
                var ver = Regex.Match(text, @"version\s*=\s*""([^""]+)""").Groups[1].Value;
                return (string.IsNullOrEmpty(id) ? null : id, string.IsNullOrEmpty(ver) ? null : ver, null);
            }
            // 3) 旧版 Forge: mcmod.info（JSON 数组）
            var info = zip.GetEntry("mcmod.info");
            if (info != null)
            {
                using var sr = new StreamReader(info.Open());
                using var doc = JsonDocument.Parse(sr.ReadToEnd());
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in doc.RootElement.EnumerateArray())
                    {
                        var id = e.TryGetProperty("modid", out var i) ? i.GetString() : null;
                        var ver = e.TryGetProperty("version", out var v) ? v.GetString() : null;
                        var name = e.TryGetProperty("name", out var nn) ? nn.GetString() : null;
                        if (id != null) return (id, ver, name);
                    }
                }
            }
            return (null, null, null);
        }
        catch
        {
            return (null, null, null); // 损坏/无法读取的 jar 忽略
        }
    }
}
