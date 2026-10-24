using NovaLauncher.Core.Configuration;

namespace NovaLauncher.Core.Services;

/// <summary>一键游戏修复结果。</summary>
public sealed class GameRepairResult
{
    public List<string> Report { get; } = new();
    public bool Ok { get; set; } = true;
}

/// <summary>一键游戏修复：清理日志/崩溃报告与临时缓存、修复目录结构、检查 Java 与下载源连通性（对齐 PCL2 的游戏修复工具）。</summary>
public sealed class GameRepairer
{
    private readonly GameDir _dir;
    private readonly LauncherConfig _config;

    public GameRepairer(LauncherConfig config)
    {
        _config = config;
        _dir = new GameDir(config);
    }

    public async Task<GameRepairResult> RepairAsync(CancellationToken ct = default)
    {
        var r = new GameRepairResult();
        r.Report.Add("NovaLauncher 一键游戏修复");
        r.Report.Add($"执行时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        r.Report.Add(new string('-', 46));

        // 1. 校验并修复目录结构
        r.Report.Add("[1/5] 校验目录结构");
        try
        {
            _dir.EnsureDirectories();
            r.Report.Add("  已确保根目录 / versions / assets / libraries / modpacks / java 存在");
        }
        catch (Exception ex) { r.Report.Add("  创建目录失败：" + ex.Message); r.Ok = false; }

        // 2. 清理日志与崩溃报告
        r.Report.Add("[2/5] 清理日志与崩溃报告");
        var cleanedLogs = CleanLogsAndCrashReports();
        r.Report.Add($"  已清理 {cleanedLogs} 个日志 / 崩溃报告文件");

        // 3. 清理临时缓存与原生库目录
        r.Report.Add("[3/5] 清理临时缓存");
        var cleanedCache = CleanTempCaches();
        r.Report.Add($"  已清理 {cleanedCache} 个临时目录（原生库 / 缓存）");

        // 4. Java 版本检查
        r.Report.Add("[4/5] Java 版本检查");
        CheckJava(r);

        // 5. 下载源连通性
        r.Report.Add("[5/5] 下载源连通性");
        await CheckNetworkAsync(r, ct);

        r.Report.Add(new string('-', 46));
        r.Report.Add(r.Ok ? "修复完成。建议重新启动游戏验证。" : "修复完成，但存在未能自动处理的问题，请参考上方提示。");
        return r;
    }

    private int CleanLogsAndCrashReports()
    {
        var count = 0;
        var roots = new List<string> { _dir.Root };
        if (_config.VersionIsolation)
            roots.AddRange(_dir.ListInstalledVersions().Select(_dir.VersionDir));

        foreach (var root in roots.Distinct())
        {
            foreach (var sub in new[] { "logs", "crash-reports" })
            {
                var d = Path.Combine(root, sub);
                if (!Directory.Exists(d)) continue;
                foreach (var f in Directory.GetFiles(d, "*", SearchOption.AllDirectories))
                {
                    try { File.Delete(f); count++; } catch { }
                }
            }
        }
        return count;
    }

    private int CleanTempCaches()
    {
        var count = 0;
        var targets = new List<string>();
        foreach (var v in _dir.ListInstalledVersions())
            targets.Add(_dir.NativesDir(v));
        targets.Add(Path.Combine(_dir.Assets, "cache"));

        foreach (var t in targets)
        {
            if (!Directory.Exists(t)) continue;
            try { Directory.Delete(t, true); count++; } catch { }
        }
        return count;
    }

    private void CheckJava(GameRepairResult r)
    {
        try
        {
            var java = JavaService.PickJava(_config.JavaPath, 21, _dir.Java)
                       ?? JavaService.FindSystemJava().OrderByDescending(j => j.MajorVersion).FirstOrDefault();
            if (java == null)
            {
                r.Report.Add("  未找到可用 Java —— 请在主页点「下载 Java」，或到设置指定 Java 路径");
                r.Ok = false;
            }
            else
            {
                r.Report.Add($"  可用 Java {java.Version}（主版本 {java.MajorVersion}）@ {java.Path}");
                r.Report.Add($"  Minecraft 1.20.5+ 需要 Java 21；1.17–1.20.4 需要 Java 17。当前可满足：{(java.MajorVersion >= 21 ? "是" : java.MajorVersion >= 17 ? "17+ 版本" : "否，建议下载更高 Java")}");
                if (java.MajorVersion < 17)
                {
                    r.Report.Add("  建议：下载并切换到 Java 17 或 21");
                    r.Ok = false;
                }
            }
        }
        catch (Exception ex) { r.Report.Add("  Java 检查失败：" + ex.Message); }
    }

    private async Task CheckNetworkAsync(GameRepairResult r, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(6));
            using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            using var resp = await hc.GetAsync("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
                HttpCompletionOption.ResponseHeadersRead, cts.Token);
            r.Report.Add($"  Minecraft 官方版本清单可达（HTTP {(int)resp.StatusCode}）");
        }
        catch (Exception ex)
        {
            r.Report.Add("  官方版本清单不可达：" + ex.Message + " —— 下载版本 / 补全可能较慢，请确保网络能访问 Mojang");
            r.Ok = false;
        }
    }
}
