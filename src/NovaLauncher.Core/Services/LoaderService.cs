using System.Diagnostics;
using NovaLauncher.Core.Configuration;
using NovaLauncher.Core.Util;

namespace NovaLauncher.Core.Services;

/// <summary>
/// 加载器安装：Fabric（Meta API 生成版本）与 Forge（Installer 引导）。
/// </summary>
public sealed class LoaderService
{
    private readonly GameDir _dir;
    private readonly string _mirror;

    public LoaderService(LauncherConfig config, string mirror)
    {
        _dir = new GameDir(config);
        _mirror = mirror;
    }

    // ---------- Fabric ----------

    /// <summary>获取某游戏版本可用的 Fabric Loader 版本列表。</summary>
    public async Task<List<string>> GetFabricLoadersAsync(string mcVersion, CancellationToken ct = default)
    {
        var url = DownloadMirror.Rewrite($"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}", _mirror);
        var json = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
        var arr = Newtonsoft.Json.Linq.JArray.Parse(json);
        return arr.Select(x => x["version"]?.ToString() ?? "").Where(v => !string.IsNullOrEmpty(v)).ToList();
    }

    /// <summary>安装 Fabric：获取 profile json 并保存为版本。返回版本 id。</summary>
    public async Task<string> InstallFabricAsync(string mcVersion, string loaderVersion, IProgress<string>? log, CancellationToken ct = default)
    {
        var url = DownloadMirror.Rewrite(
            $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}/{loaderVersion}/profile/json", _mirror);
        log?.Report($"获取 Fabric {loaderVersion} 版本信息…");
        var json = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
        var root = Newtonsoft.Json.Linq.JObject.Parse(json);
        var id = root["id"]?.ToString() ?? $"fabric-loader-{loaderVersion}-{mcVersion}";
        var dir = _dir.VersionDir(id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(_dir.VersionJson(id), json);
        return id;
    }

    // ---------- Forge ----------

    /// <summary>下载 Forge 官方 installer jar。返回本地路径。</summary>
    public async Task<string> DownloadForgeInstallerAsync(string mcVersion, string forgeVersion, IProgress<string>? log, CancellationToken ct = default)
    {
        var destDir = Path.Combine(_dir.Modpacks, "forge");
        Directory.CreateDirectory(destDir);
        var dest = Path.Combine(destDir, $"forge-{mcVersion}-{forgeVersion}-installer.jar");
        var url = DownloadMirror.Rewrite(
            $"https://maven.minecraftforge.net/net/minecraftforge/forge/{mcVersion}-{forgeVersion}/{forgeVersion}-{mcVersion}-{forgeVersion}-installer.jar",
            _mirror);
        log?.Report($"下载 Forge Installer {forgeVersion}…");
        await Http.DownloadAsync(url, dest, null, ct).ConfigureAwait(false);
        return dest;
    }

    /// <summary>运行 Forge installer 安装到客户端。返回是否成功。</summary>
    public bool RunForgeInstaller(string installerPath, string javaExe, IProgress<string>? log)
    {
        if (!File.Exists(javaExe)) { log?.Report("未找到 Java，无法运行 Forge 安装器。"); return false; }
        var psi = new ProcessStartInfo
        {
            FileName = javaExe,
            WorkingDirectory = _dir.Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-jar");
        psi.ArgumentList.Add(installerPath);
        psi.ArgumentList.Add("--installClient");
        psi.ArgumentList.Add(_dir.Root);
        log?.Report("运行 Forge 安装器（可能需要几分钟）…");
        using var p = Process.Start(psi);
        if (p == null) return false;
        p.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) log?.Report(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) log?.Report(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.WaitForExit();
        return p.ExitCode == 0;
    }
}
