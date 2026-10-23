using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NovaLauncher.Core.Services;

/// <summary>
/// 模组依赖缺失检查：读取每个 mod 清单的依赖声明，找出必须但当前 mods 目录中缺少的前置模组。
/// 排除 Minecraft/加载器/Java 等环境依赖。版本范围做近似校验（仅提示可能不符）。
/// </summary>
public sealed class ModDependencyChecker
{
    /// <summary>一个缺失的必装依赖。</summary>
    public sealed record MissingDep(string Mod, string DepModId, string? VersionRange, string? Source);

    /// <summary>环境性依赖名（由 Minecraft/加载器/Java 提供，不计为缺失）。</summary>
    private static readonly HashSet<string> EnvDeps = new(StringComparer.OrdinalIgnoreCase)
    {
        "minecraft", "fabricloader", "fabric", "forge", "neoforge", "quilt", "quilt_loader",
        "quilted_fabric_api", "java", "loader", "parchment",
    };

    /// <summary>扫描 mods 目录，返回缺失的必装依赖。</summary>
    public List<MissingDep> Check(string modsDir)
    {
        var result = new List<MissingDep>();
        if (!Directory.Exists(modsDir)) return result;

        // 收集所有已装 modid
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var jar in Directory.GetFiles(modsDir).Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)))
        {
            if (Path.GetFileName(jar).EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)) continue;
            var (id, _, _) = ReadManifest(jar);
            if (id != null) installed.Add(id);
        }

        // 逐个检查依赖
        foreach (var jar in Directory.GetFiles(modsDir).Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)))
        {
            if (Path.GetFileName(jar).EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)) continue;
            var (modId, _, deps) = ReadManifest(jar);
            if (modId == null || deps == null) continue;
            foreach (var (depId, range, mandatory, source) in deps)
            {
                if (!mandatory) continue;
                if (EnvDeps.Contains(depId)) continue;
                if (installed.Contains(depId)) continue;
                result.Add(new MissingDep(modId, depId, range, source));
            }
        }
        return result.OrderBy(m => m.Mod).ThenBy(m => m.DepModId).ToList();
    }

    /// <summary>按依赖关系对 mods 目录文件做拓扑排序：被依赖的模组排在前面。返回排序后的文件名列表。</summary>
    public List<string> TopoSort(string modsDir)
    {
        var files = Directory.Exists(modsDir)
            ? Directory.GetFiles(modsDir)
                .Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                .Where(f => !Path.GetFileName(f).EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                .Select(f => Path.GetFileName(f)!)
                .ToList()
            : new List<string>();

        if (files.Count <= 1) return files;

        // id -> file 映射
        var idToFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var id = ReadId(Path.Combine(modsDir, file));
            if (id != null) idToFile.TryAdd(id, file);
        }

        // file -> 依赖的 file 集合
        var depsOf = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var (id, _, deps) = ReadManifest(Path.Combine(modsDir, file));
            if (id == null) continue;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (deps != null)
                foreach (var (depId, _, mandatory, _) in deps)
                    if (mandatory && idToFile.ContainsKey(depId) && !string.Equals(depId, id, StringComparison.OrdinalIgnoreCase))
                        set.Add(idToFile[depId]);
            depsOf[id] = set;
        }

        // Kahn 拓扑排序（被依赖的在前，同层按文件名保序）
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        void Visit(string file)
        {
            if (!visited.Add(file)) return;
            var id = ReadId(Path.Combine(modsDir, file));
            if (id != null && depsOf.TryGetValue(id, out var dset))
                foreach (var d in dset.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    Visit(d);
            result.Add(file);
        }
        foreach (var f in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)) Visit(f);
        return result;
    }

    private static string? ReadId(string jarPath)
    {
        var (id, _, _) = ReadManifest(jarPath);
        return id;
    }

    /// <summary>读取 jar 清单：返回 (modId, version, 依赖列表)。</summary>
    private static (string?, string?, List<(string id, string? range, bool mandatory, string? src)>?) ReadManifest(string jarPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(jarPath);
            var fabric = zip.GetEntry("fabric.mod.json");
            if (fabric != null)
            {
                using var sr = new StreamReader(fabric.Open());
                using var doc = JsonDocument.Parse(sr.ReadToEnd());
                var r = doc.RootElement;
                var id = r.TryGetProperty("id", out var i) ? i.GetString() : null;
                var ver = r.TryGetProperty("version", out var v) ? v.GetString() : null;
                var deps = new List<(string, string?, bool, string?)>();
                if (r.TryGetProperty("depends", out var d) && d.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in d.EnumerateObject())
                        deps.Add((p.Name, DepVersion(p.Value), true, "fabric"));
                }
                else if (r.TryGetProperty("depends", out var da) && da.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in da.EnumerateArray())
                    {
                        var did = e.TryGetProperty("id", out var d2) ? d2.GetString() : null;
                        if (did == null) continue;
                        var dr = e.TryGetProperty("version", out var d3) ? d3.GetString() : null;
                        var optional = e.TryGetProperty("optional", out var o) && o.GetBoolean();
                        deps.Add((did, dr, !optional, "fabric"));
                    }
                }
                return (id, ver, deps);
            }

            var toml = zip.GetEntry("META-INF/mods.toml");
            if (toml != null)
            {
                using var sr = new StreamReader(toml.Open());
                var text = sr.ReadToEnd();
                var id = Regex.Match(text, @"modId\s*=\s*""([^""]+)""").Groups[1].Value;
                var ver = Regex.Match(text, @"version\s*=\s*""([^""]+)""").Groups[1].Value;
                var deps = new List<(string, string?, bool, string?)>();
                // 匹配每个 [[dependencies.X]] 块
                foreach (Match m in Regex.Matches(text, @"\[\[dependencies\.([^\]]+)\]\]\s*\{(.*?)\}", RegexOptions.Singleline))
                {
                    var block = m.Groups[2].Value;
                    var dId = Regex.Match(block, @"modId\s*=\s*""([^""]+)""").Groups[1].Value;
                    if (dId == "") continue;
                    var dRange = Regex.Match(block, @"versionRange\s*=\s*""([^""]*)""").Groups[1].Value;
                    var mandatory = !Regex.IsMatch(block, @"mandatory\s*=\s*false");
                    deps.Add((dId, dRange == "" ? null : dRange, mandatory, "mods.toml"));
                }
                return (string.IsNullOrEmpty(id) ? null : id, string.IsNullOrEmpty(ver) ? null : ver, deps);
            }

            var info = zip.GetEntry("mcmod.info");
            if (info != null)
            {
                using var sr = new StreamReader(info.Open());
                using var doc = JsonDocument.Parse(sr.ReadToEnd());
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in doc.RootElement.EnumerateArray())
                    {
                        var id2 = e.TryGetProperty("modid", out var i2) ? i2.GetString() : null;
                        var ver2 = e.TryGetProperty("version", out var v2) ? v2.GetString() : null;
                        var deps = new List<(string, string?, bool, string?)>();
                        if (e.TryGetProperty("dependencies", out var dd) && dd.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var d in dd.EnumerateArray())
                            {
                                var did = d.TryGetProperty("modid", out var d2) ? d2.GetString() : null;
                                if (did == null) continue;
                                var dver = d.TryGetProperty("version", out var d3) ? d3.GetString() : null;
                                deps.Add((did, dver, true, "mcmod.info"));
                            }
                        }
                        return (id2, ver2, deps);
                    }
                }
            }
            return (null, null, null);
        }
        catch
        {
            return (null, null, null);
        }
    }

    private static string? DepVersion(JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.String) return v.GetString();
        if (v.ValueKind == JsonValueKind.Object && v.TryGetProperty("version", out var s)) return s.GetString();
        return null;
    }
}
