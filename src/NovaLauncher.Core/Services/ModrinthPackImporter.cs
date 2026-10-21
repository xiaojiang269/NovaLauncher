using System.IO.Compression;
using System.Text.Json;
using NovaLauncher.Core.Configuration;
using NovaLauncher.Core.Util;

namespace NovaLauncher.Core.Services;

/// <summary>
/// Modrinth 整合包（.mrpack）解析与一键导入。
/// 解压 mrpack，读取 modrinth.index.json，把各模组下载到目标版本 mods/，并把 overrides 内容合并到版本目录。
/// </summary>
public sealed class ModrinthPackImporter
{
    /// <summary>导入结果。</summary>
    public sealed record Result(string Name, string VersionId, int ModCount, int Ok, int Fail);

    public async Task<Result> ImportAsync(string mrpackPath, GameDir dir, string versionId,
        IProgress<(string msg, int cur, int total)>? packProgress = null, CancellationToken ct = default)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "nl_mrpack_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            ZipFile.ExtractToDirectory(mrpackPath, tmp);
            var indexPath = Path.Combine(tmp, "modrinth.index.json");
            if (!File.Exists(indexPath)) throw new InvalidOperationException("不是有效的 Modrinth 整合包（缺少 modrinth.index.json）。");

            using var doc = JsonDocument.Parse(File.ReadAllText(indexPath));
            var root = doc.RootElement;
            var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";

            // 1. 下载各模组到目标版本 mods/（按 index.files[].path）
            int ok = 0, fail = 0;
            if (root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
            {
                var total = files.GetArrayLength();
                var idx = 0;
                foreach (var f in files.EnumerateArray())
                {
                    idx++;
                    if (!f.TryGetProperty("downloads", out var dls) || dls.ValueKind != JsonValueKind.Array || dls.GetArrayLength() == 0) { fail++; continue; }
                    var url = dls[0].GetString() ?? "";
                    if (url == "") { fail++; continue; }
                    var rel = f.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
                    rel = rel.Trim('/');
                    var relDir = Path.GetDirectoryName(rel) ?? "";
                    var fileName = Path.GetFileName(rel);
                    packProgress?.Report((fileName, idx, total));
                    var targetDir = string.IsNullOrEmpty(relDir) ? dir.VersionModsDir(versionId) : dir.VersionSubDir(versionId, relDir);
                    Directory.CreateDirectory(targetDir);
                    var dest = Path.Combine(targetDir, fileName);
                    try
                    {
                        if (!File.Exists(dest)) await Http.DownloadAsync(url, dest, null, ct).ConfigureAwait(false);
                        ok++;
                    }
                    catch { fail++; }
                }
            }

            // 2. 合并 overrides 到版本目录
            var overrides = Path.Combine(tmp, "overrides");
            if (Directory.Exists(overrides))
                CopyDir(overrides, dir.VersionDir(versionId));

            return new Result(name, versionId, ok + fail, ok, fail);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            var target = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
