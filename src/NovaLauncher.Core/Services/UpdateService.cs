using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using NovaLauncher.Core.Util;

namespace NovaLauncher.Core.Services;

/// <summary>当前启动器版本。</summary>
public static class AppVersion
{
    /// <summary>当前版本号（与 GitHub Release tag 对应，不带 v 前缀）。</summary>
    public const string Current = "3.1.5";

    public static string Tag => "v" + Current;
}

/// <summary>自动更新：查询 GitHub Release、下载当前平台安装包、替换自身并重启。</summary>
public sealed class UpdateService
{
    private const string Repo = "xiaojiang269/NovaLauncher";
    private const string ReleasesApi = $"https://api.github.com/repos/{Repo}/releases/latest";

    /// <summary>
    /// GitHub 加速镜像前缀（中国大陆可访问）。空串=直连优先；
    /// 直连失败时依次尝试国内镜像，解决 github.com:443 连接超时导致的更新失败。
    /// </summary>
    private static readonly string[] DownloadMirrors =
    {
        "",                          // 直连（优先，海外/代理环境最快）
        "https://ghfast.top/",
        "https://gh-proxy.com/",
        "https://ghproxy.net/",
    };

    /// <summary>最新发布信息。</summary>
    public sealed record UpdateInfo(string Version, string Tag, string? AssetUrl, string? AssetName, string? Body, bool IsCurrent);
    public sealed record UpdateResult(bool Success, string Message, bool Restarting = false);

    /// <summary>查询最新 Release。IsCurrent=true 表示已是最新。</summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var http = Http.Get();
            string? json = null;
            foreach (var m in DownloadMirrors)
            {
                try
                {
                    json = await http.GetStringAsync(m + ReleasesApi, ct).ConfigureAwait(false);
                    break;
                }
                catch
                {
                    // 直连或该镜像不可达，尝试下一个
                }
            }
            if (json == null) return null;
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var tag = (r.TryGetProperty("tag_name", out var t) ? t.GetString() : null) ?? "";
            var body = (r.TryGetProperty("body", out var b) ? b.GetString() : null) ?? "";
            var assets = r.TryGetProperty("assets", out var a) && a.ValueKind == JsonValueKind.Array ? a : default;

            var version = tag.TrimStart('v', 'V');
            var assetUrl = "";
            var assetName = "";
            var target = TargetAssetSuffix();
            if (assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in assets.EnumerateArray())
                {
                    var name = item.TryGetProperty("name", out var n) ? n.GetString() : "";
                    if (string.IsNullOrEmpty(name) || !name.Contains(target, StringComparison.OrdinalIgnoreCase)) continue;
                    if (item.TryGetProperty("browser_download_url", out var u)) assetUrl = u.GetString() ?? "";
                    assetName = name;
                    break;
                }
            }

            return new UpdateInfo(version, tag, string.IsNullOrEmpty(assetUrl) ? null : assetUrl, assetName, body,
                IsCurrent: string.Equals(version, AppVersion.Current, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null; // 网络/解析失败视为不可达
        }
    }

    /// <summary>下载并替换自身。下载到临时目录，用独立脚本等待本进程退出后覆盖再重启。</summary>
    public async Task<UpdateResult> DownloadAndRestartAsync(string assetUrl, IProgress<(long done, long total)>? progress, CancellationToken ct = default)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            return new UpdateResult(false, "无法定位当前程序路径。");

        var tmpDir = Path.Combine(Path.GetTempPath(), "novaupdater_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var zipPath = Path.Combine(tmpDir, "update.zip");
        try
        {
            // 依次尝试直连与国内镜像，解决 github.com:443 超时
            Exception? lastEx = null;
            bool downloaded = false;
            foreach (var m in DownloadMirrors)
            {
                try
                {
                    await Http.DownloadAsync(m + assetUrl, zipPath, progress, ct).ConfigureAwait(false);
                    downloaded = true;
                    break;
                }
                catch (Exception ex) { lastEx = ex; }
            }
            if (!downloaded)
                return new UpdateResult(false, "更新下载失败：" + (lastEx?.Message ?? "网络不可达，请稍后重试或手动下载。"));
            var extractDir = Path.Combine(tmpDir, "pkg");
            ZipFile.ExtractToDirectory(zipPath, extractDir);
            var packageRoot = FindPackageRoot(extractDir, exePath);
            if (packageRoot == null)
                return new UpdateResult(false, "更新包内容无法识别（缺少可执行文件）。");

            // 生成替换脚本并启动，脚本等待后覆盖 app 目录并重启
            var script = WriteRestartScript(tmpDir, packageRoot, exePath);
            if (script == null)
                return new UpdateResult(false, "无法生成更新脚本（当前平台不支持自动更新）。");

            StartDetached(script);
            return new UpdateResult(true, "更新已下载，正在替换并重启…", Restarting: true);
        }
        catch (Exception ex)
        {
            return new UpdateResult(false, "更新失败：" + ex.Message);
        }
        finally
        {
            try { File.Delete(zipPath); } catch { }
        }
    }

    /// <summary>在解压目录中找到包含目标可执行文件的根目录。</summary>
    private static string? FindPackageRoot(string extractDir, string exePath)
    {
        var exeName = Path.GetFileName(exePath);
        foreach (var d in new[] { extractDir })
        {
            if (File.Exists(Path.Combine(d, exeName))) return d;
        }
        // 可能在子目录（mac 常见 .app/Contents/MacOS）
        foreach (var sub in Directory.GetDirectories(extractDir, "*", SearchOption.AllDirectories))
        {
            if (File.Exists(Path.Combine(sub, exeName))) return sub;
        }
        return null;
    }

    /// <summary>目标平台在 asset 文件名中的后缀片段。</summary>
    private static string TargetAssetSuffix()
    {
        var os = Platform.CurrentOs;
        var arch = RuntimeInformation.ProcessArchitecture;
        if (os == Platform.OsType.Windows) return "Windows-x64";
        if (os == Platform.OsType.Linux) return "Linux-x64";
        if (os == Platform.OsType.MacOs)
            return arch == Architecture.Arm64 ? "macOS-arm64" : "macOS-x64";
        return "Linux-x64";
    }

    /// <summary>写替换脚本：等待原进程退出，把包内文件覆盖到程序目录，再重启。返回脚本路径。</summary>
    private static string? WriteRestartScript(string tmpDir, string packageRoot, string exePath)
    {
        var appDir = Path.GetDirectoryName(exePath)!;
        var pkg = packageRoot;
        var exeName = Path.GetFileName(exePath);

        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(tmpDir, "update.bat");
            // 使用 xcopy 覆盖目录内容（跳过无法复制的自身）
            var content =
                "@echo off\r\n" +
                "timeout /t 2 /nobreak >nul\r\n" +
                $"xcopy \"{pkg}\\*\" \"{appDir}\" /E /Y /Q >nul 2>nul\r\n" +
                $"start \"\" \"{exePath}\"\r\n" +
                $"rmdir /s /q \"{tmpDir}\" >nul 2>nul\r\n";
            File.WriteAllText(script, content);
            return script;
        }

        // Linux / macOS：shell 脚本
        var sh = Path.Combine(tmpDir, "update.sh");
        // macOS 上 .app 是目录，需先移除旧内容再拷贝；Linux 直接覆盖
        var content2 =
            "#!/bin/sh\n" +
            "sleep 2\n" +
            $"rm -f \"{appDir}/\"*.dll \"{appDir}/{exeName}\" 2>/dev/null\n" +
            $"cp -Rf \"{pkg}/.\" \"{appDir}/\" 2>/dev/null\n" +
            $"chmod +x \"{exePath}\" 2>/dev/null\n" +
            $"nohup \"{exePath}\" >/dev/null 2>&1 &\n" +
            $"rm -rf \"{tmpDir}\"\n";
        File.WriteAllText(sh, content2);
        // 赋可执行权限
        File.SetUnixFileMode(sh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                  UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                  UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        return sh;
    }

    private static void StartDetached(string script)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            UseShellExecute = true,
            CreateNoWindow = true,
        };
        if (OperatingSystem.IsWindows())
        {
            psi.FileName = "cmd.exe";
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add("start");
            psi.ArgumentList.Add("\"\"");
            psi.ArgumentList.Add(script);
        }
        else
        {
            psi.FileName = "sh";
            psi.ArgumentList.Add(script);
        }
        System.Diagnostics.Process.Start(psi);
    }
}
