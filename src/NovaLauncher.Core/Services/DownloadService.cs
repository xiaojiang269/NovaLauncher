using System.Collections.Concurrent;
using System.IO.Compression;
using NovaLauncher.Core.Configuration;
using NovaLauncher.Core.Models;
using NovaLauncher.Core.Util;

namespace NovaLauncher.Core.Services;

/// <summary>
/// 版本下载：负责下载并校验客户端 jar、全部库（含 natives）、资源文件。
/// 支持并发下载与镜像重写。
/// </summary>
public sealed class DownloadService
{
    private readonly GameDir _dir;
    private readonly string _mirror;
    private const int MaxConcurrent = 8;

    public DownloadService(LauncherConfig config, string mirror)
    {
        _dir = new GameDir(config);
        _mirror = mirror;
    }

    public GameDir Dir => _dir;

    /// <summary>下载一个版本的全部运行所需文件。</summary>
    public async Task<DownloadSummary> DownloadVersionAsync(
        MinecraftVersion version, string versionId,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var summary = new DownloadSummary();

        // 1. 客户端 jar
        var jarDone = await EnsureFileAsync(
            _dir.VersionJar(versionId),
            version.Client?.Url, version.Client?.Sha1, version.Client?.Size ?? 0,
            $"{versionId}.jar", progress, ct).ConfigureAwait(false);
        summary.Downloaded += jarDone.Downloaded; summary.Skipped += jarDone.Skipped;
        if (!jarDone.Ok) { summary.Failed++; summary.Errors.Add($"客户端 {versionId}.jar 下载失败"); return summary; }
        version.ClientDownloaded = true;

        // 2. 库文件
        var tasks = new List<LibTask>();
        foreach (var lib in version.Libraries)
        {
            foreach (var (item, dl, isNative) in LibraryResolver.ResolveNeeded(lib))
            {
                string relPath;
                if (isNative)
                {
                    var nativePath = LibraryResolver.ResolveNativePath(item);
                    if (nativePath == null) continue;
                    relPath = nativePath;
                }
                else
                {
                    relPath = LibraryResolver.ResolveMainPath(item);
                }
                var absPath = Path.Combine(_dir.Libraries, relPath);
                var url = dl?.Url;
                if (string.IsNullOrEmpty(url)) url = $"https://libraries.minecraft.net/{relPath}";
                tasks.Add(new LibTask
                {
                    Name = item.Name,
                    RelPath = relPath,
                    AbsPath = absPath,
                    Url = DownloadMirror.Rewrite(url, _mirror),
                    Sha1 = dl?.Sha1,
                    Size = dl?.Size ?? 0,
                    IsNative = isNative,
                });
            }
        }

        // 先跳过已存在且正确的文件，统计进度总量
        long totalBytes = 0;
        int totalItems = 0;
        foreach (var t in tasks)
        {
            if (IsFileValid(t.AbsPath, t.Sha1)) { t.Skip = true; summary.Skipped++; continue; }
            totalBytes += Math.Max(t.Size, 0);
            totalItems++;
        }

        progress?.Report(new DownloadProgress { Phase = "下载库文件", TotalItems = totalItems, TotalBytes = totalBytes, CurrentFile = "" });

        var failed = new ConcurrentQueue<string>();
        var semaphore = new SemaphoreSlim(MaxConcurrent);
        var doneBytes = 0L;
        var doneItems = 0;
        var lockObj = new object();

        await Task.WhenAll(tasks.Where(t => !t.Skip).Select(async t =>
        {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                progress?.Report(new DownloadProgress
                {
                    Phase = "下载库文件", DoneItems = doneItems, TotalItems = totalItems,
                    DoneBytes = doneBytes, TotalBytes = totalBytes, CurrentFile = Path.GetFileName(t.RelPath),
                });
                await Http.DownloadAsync(t.Url, t.AbsPath, null, ct).ConfigureAwait(false);
                if (!IsFileValid(t.AbsPath, t.Sha1))
                {
                    File.Delete(t.AbsPath);
                    throw new InvalidOperationException($"校验失败 {t.Name}");
                }
                if (t.IsNative)
                    await ExtractNativeAsync(t.AbsPath, versionId, ct).ConfigureAwait(false);
                summary.Downloaded++;
                lock (lockObj) { doneItems++; doneBytes += Math.Max(t.Size, 0); }
            }
            catch (Exception ex)
            {
                failed.Enqueue($"{t.Name}: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        })).ConfigureAwait(false);

        summary.Failed = failed.Count;
        foreach (var e in failed) summary.Errors.Add(e);

        // 3. 资源文件
        if (version.AssetIndex != null)
        {
            var assetResult = await DownloadAssetsAsync(version, progress, ct).ConfigureAwait(false);
            summary.Downloaded += assetResult.Downloaded;
            summary.Skipped += assetResult.Skipped;
            summary.Failed += assetResult.Failed;
            summary.Errors.AddRange(assetResult.Errors);
        }

        return summary;
    }

    /// <summary>下载并校验 asset index 与全部缺失资源。</summary>
    public async Task<DownloadSummary> DownloadAssetsAsync(MinecraftVersion version, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var summary = new DownloadSummary();
        if (version.AssetIndex == null) return summary;

        var indexPath = _dir.AssetsIndexes + $"/{version.AssetIndex.Id}.json";
        var indexUrl = DownloadMirror.Rewrite(version.AssetIndex.Url, _mirror);
        var indexFile = await EnsureFileAsync(indexPath, indexUrl, null, version.AssetIndex.Size, "asset index", null, ct).ConfigureAwait(false);
        if (!indexFile.Ok)
        {
            summary.Failed = 1; summary.Errors.Add("资源索引下载失败");
            return summary;
        }
        summary.Downloaded += indexFile.Downloaded; summary.Skipped += indexFile.Skipped;

        // 解析 objects
        Dictionary<string, (string hash, long size)> objects;
        try
        {
            var root = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(indexPath));
            objects = new Dictionary<string, (string, long)>();
            if (root["objects"] is Newtonsoft.Json.Linq.JObject objs)
            {
                foreach (var p in objs.Properties())
                {
                    var hash = p.Value?["hash"]?.ToString() ?? "";
                    var size = p.Value?["size"]?.ToObject<long>() ?? 0;
                    if (!string.IsNullOrEmpty(hash)) objects[hash] = (hash, size);
                }
            }
        }
        catch
        {
            summary.Failed = 1; summary.Errors.Add("资源索引解析失败");
            return summary;
        }

        var totalBytes = objects.Values.Sum(o => o.size);
        progress?.Report(new DownloadProgress { Phase = "下载资源文件", TotalItems = objects.Count, TotalBytes = totalBytes, CurrentFile = "" });

        var semaphore = new SemaphoreSlim(MaxConcurrent);
        var failed = new ConcurrentQueue<string>();
        var doneItems = 0;
        var doneBytes = 0L;
        var lockObj = new object();

        await Task.WhenAll(objects.Select(async kv =>
        {
            var (hash, size) = kv.Value;
            var rel = $"{hash[..2]}/{hash}";
            var abs = Path.Combine(_dir.AssetsObjects, rel);
            if (File.Exists(abs) && new FileInfo(abs).Length == size) { lock (lockObj) { summary.Skipped++; doneItems++; doneBytes += size; } return; }

            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                progress?.Report(new DownloadProgress
                {
                    Phase = "下载资源文件", DoneItems = doneItems, TotalItems = objects.Count,
                    DoneBytes = doneBytes, TotalBytes = totalBytes, CurrentFile = hash[..2] + "/...",
                });
                var url = DownloadMirror.Rewrite($"https://resources.download.minecraft.net/{rel}", _mirror);
                await Http.DownloadAsync(url, abs, null, ct).ConfigureAwait(false);
                if (new FileInfo(abs).Length != size) throw new InvalidOperationException($"资源大小不符 {hash}");
                lock (lockObj) { summary.Downloaded++; doneItems++; doneBytes += size; }
            }
            catch (Exception ex)
            {
                failed.Enqueue($"{hash[..2]}/{hash}: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        })).ConfigureAwait(false);

        summary.Failed = failed.Count;
        foreach (var e in failed) summary.Errors.Add(e);
        return summary;
    }

    /// <summary>解压 native jar 到 natives 目录（先清空）。</summary>
    private async Task ExtractNativeAsync(string jarPath, string versionId, CancellationToken ct)
    {
        var nativesDir = _dir.NativesDir(versionId);
        if (Directory.Exists(nativesDir)) Directory.Delete(nativesDir, true);
        Directory.CreateDirectory(nativesDir);
        await Task.Run(() => ZipFile.ExtractToDirectory(jarPath, nativesDir), ct).ConfigureAwait(false);
        // 删除 macOS 可能产生的无用文件
        foreach (var extra in new[] { "META-INF", "__MACOSX" })
        {
            var p = Path.Combine(nativesDir, extra);
            if (Directory.Exists(p)) Directory.Delete(p, true);
        }
    }

    private async Task<(bool Ok, int Downloaded, int Skipped)> EnsureFileAsync(
        string absPath, string? url, string? sha1, long size, string name, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        if (IsFileValid(absPath, sha1)) return (true, 0, 1);
        if (string.IsNullOrEmpty(url)) return (false, 0, 0);
        await Http.DownloadAsync(url, absPath, null, ct).ConfigureAwait(false);
        if (!IsFileValid(absPath, sha1)) { try { File.Delete(absPath); } catch { } return (false, 0, 0); }
        return (true, 1, 0);
    }

    private bool IsFileValid(string absPath, string? sha1)
    {
        if (!File.Exists(absPath)) return false;
        if (string.IsNullOrEmpty(sha1)) return new FileInfo(absPath).Length > 0;
        try
        {
            return string.Equals(HashUtil.Sha1OfFile(absPath), sha1, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private sealed class LibTask
    {
        public string Name { get; set; } = "";
        public string RelPath { get; set; } = "";
        public string AbsPath { get; set; } = "";
        public string Url { get; set; } = "";
        public string? Sha1 { get; set; }
        public long Size { get; set; }
        public bool IsNative { get; set; }
        public bool Skip { get; set; }
    }
}
