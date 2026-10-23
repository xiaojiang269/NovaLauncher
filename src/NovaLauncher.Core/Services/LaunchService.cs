using System.Diagnostics;
using NovaLauncher.Core.Configuration;
using NovaLauncher.Core.Models;
using NovaLauncher.Core.Util;

namespace NovaLauncher.Core.Services;

/// <summary>游戏启动核心：解析参数、校验、启动进程。</summary>
public sealed class LaunchService
{
    private readonly GameDir _dir;
    private readonly LauncherConfig _config;

    public LaunchService(LauncherConfig config)
    {
        _config = config;
        _dir = new GameDir(config);
    }

    /// <summary>该版本实际使用的游戏数据目录（版本隔离开启时用 versions/{id}/，否则用根目录）。</summary>
    public string GameDataDir(string versionId)
    {
        if (_config.VersionIsolation)
        {
            var d = _dir.VersionDir(versionId);
            Directory.CreateDirectory(d);
            return d;
        }
        return _dir.Root;
    }

    /// <summary>启动游戏。</summary>
    public LaunchResult Launch(MinecraftVersion version, LaunchOptions options, JavaInfo java, IProgress<string>? log = null)
    {
        var result = new LaunchResult { Success = false };

        var nativeDir = _dir.NativesDir(version.Id);
        // 1.20+ 版本已移除 natives 机制，目录可能为空；统一确保存在即可
        Directory.CreateDirectory(nativeDir);

        var gameDir = GameDataDir(version.Id);

        // 1. 组装 classpath
        var classpath = new List<string> { _dir.VersionJar(version.Id) };
        foreach (var lib in version.Libraries)
        {
            if (!lib.Allowed || lib.Natives.Count > 0) continue;
            var rel = LibraryResolver.ResolveMainPath(lib);
            var abs = Path.Combine(_dir.Libraries, rel);
            if (File.Exists(abs)) classpath.Add(abs);
        }
        var cp = string.Join(Path.PathSeparator, classpath);

        // 2. 组装 JVM 参数
        var jvm = new List<string>();
        jvm.Add($"-Xmx{options.MaxMemoryMb}M");
        jvm.Add($"-Xms{options.MinMemoryMb}M");

        var dict = BuildTokenMap(version, options, nativeDir, cp, gameDir);
        foreach (var arg in version.JvmArguments)
        {
            if (!arg.Allowed) continue;
            foreach (var v in arg.Values) jvm.Add(ReplaceTokens(v, dict));
        }

        // 确保必要 JVM 参数存在
        EnsureJvmArg(jvm, "-Djava.library.path=", $"-Djava.library.path={nativeDir}");
        EnsureJvmArg(jvm, "-Dminecraft.launcher.brand=", "-Dminecraft.launcher.brand=NovaLauncher");
        EnsureJvmArg(jvm, "-Dminecraft.launcher.version=", "-Dminecraft.launcher.version=1.0.0");

        // 外置登录（authlib-injector）：注入 javaagent 指向第三方认证服务器
        if (string.Equals(options.Account.Type, "external", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(options.Account.ExternalServer))
        {
            var aiJar = Path.Combine(_dir.Root, "authlib-injector.jar");
            if (File.Exists(aiJar))
                jvm.Add($"-javaagent:{aiJar}={options.Account.ExternalServer.TrimEnd('/')}");
        }

        // 3. 额外用户参数
        foreach (var extra in SplitArgs(options.ExtraJvmArgs)) jvm.Add(extra);

        // 4. classpath 与主类
        jvm.Add("-cp");
        jvm.Add(cp);
        jvm.Add(version.MainClass);

        // 5. 游戏参数
        var game = new List<string>();
        foreach (var arg in version.GameArguments)
        {
            if (!arg.Allowed) continue;
            foreach (var v in arg.Values)
            {
                var replaced = ReplaceTokens(v, dict);
                if (replaced.Contains("${")) continue; // 未解析的占位符丢弃
                game.Add(replaced);
            }
        }
        if (!string.IsNullOrEmpty(options.QuickConnectServer))
        {
            game.Add("--quickPlayMultiplayer");
            game.Add(options.QuickConnectServer);
        }

        // 6. 启动进程
        var psi = new ProcessStartInfo
        {
            FileName = java.Path,
            WorkingDirectory = gameDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in jvm) psi.ArgumentList.Add(a);
        foreach (var a in game) psi.ArgumentList.Add(a);

        try
        {
            var proc = Process.Start(psi);
            if (proc == null)
            {
                result.Message = "无法启动 Java 进程。";
                return result;
            }
            result.Success = true;
            result.Message = "游戏已启动";
            result.Process = proc;
            result.Log.Add("启动命令：");
            result.Log.Add(java.Path + " " + string.Join(" ", jvm) + " " + string.Join(" ", game));

            // 后台转发日志
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!proc.HasExited)
                    {
                        var line = await proc.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                        if (line != null) log?.Report(line);
                    }
                    string? err;
                    while ((err = await proc.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
                        log?.Report(err);
                }
                catch { }
            });
            return result;
        }
        catch (Exception ex)
        {
            result.Message = $"启动失败：{ex.Message}";
            return result;
        }
    }

    private Dictionary<string, string> BuildTokenMap(MinecraftVersion version, LaunchOptions options, string nativeDir, string cp, string gameDir)
    {
        var versionId = version.Id;
        var account = options.Account;
        var assetsRoot = _dir.Assets;
        // 资源索引名：必须使用版本 json 的 assetIndex.id（如 "36"），否则游戏找不到索引导致资源缺失。
        // 老版本没有 assetIndex 字段时回退到版本 ID。
        var assetsIndex = !string.IsNullOrEmpty(version.AssetIndex?.Id) ? version.AssetIndex.Id : versionId;
        var map = new Dictionary<string, string>
        {
            ["${auth_player_name}"] = account.Name,
            ["${version_name}"] = versionId,
            ["${game_directory}"] = gameDir,
            ["${game_assets}"] = assetsRoot,
            ["${assets_root}"] = assetsRoot,
            ["${assets_index_name}"] = assetsIndex,
            ["${auth_uuid}"] = account.Uuid.Replace("-", ""),
            ["${auth_access_token}"] = account.AccessToken,
            ["${user_type}"] = account.Type == "microsoft" ? "msa" : "legacy",
            ["${version_type}"] = "NovaLauncher",
            ["${natives_directory}"] = nativeDir,
            ["${launcher_name}"] = "NovaLauncher",
            ["${launcher_version}"] = "1.0.0",
            ["${classpath}"] = cp,
            ["${resolution_width}"] = options.WindowWidth > 0 ? options.WindowWidth.ToString() : "854",
            ["${resolution_height}"] = options.WindowHeight > 0 ? options.WindowHeight.ToString() : "480",
        };
        if (options.QuickConnectServer != null)
        {
            map["${quickPlayMultiplayer}"] = options.QuickConnectServer;
            map["${quickPlaySingleplayer}"] = "";
            map["${quickPlayRealms}"] = "";
        }
        return map;
    }

    private static string ReplaceTokens(string value, Dictionary<string, string> map)
    {
        foreach (var kv in map)
            value = value.Replace(kv.Key, kv.Value);
        return value;
    }

    private static void EnsureJvmArg(List<string> jvm, string startsWith, string defaultVal)
    {
        if (!jvm.Any(a => a.StartsWith(startsWith, StringComparison.OrdinalIgnoreCase)))
            jvm.Add(defaultVal);
    }

    private static IEnumerable<string> SplitArgs(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        foreach (var part in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            yield return part;
    }
}
