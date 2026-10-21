using System.Diagnostics;
using NovaLauncher.Core.Util;

namespace NovaLauncher.Core.Services;

/// <summary>
/// 联机服务（主流启动器做法：虚拟局域网 + 房间码 + MC「对局域网开放」）。
/// 底层支持 EasyTier（陶瓦联机同款开源组网）与陶瓦联机（Terracotta）两种后端；
/// 组件需在本机存在，启动器自动检测并拉起，未安装时引导用户安装。
/// </summary>
public sealed class LanPlayService
{
    private static readonly HttpClient Client = Http.Get();
    private Process? _process;
    private string? _networkName;

    /// <summary>后端候选可执行文件名。</summary>
    private static readonly string[] BackendCandidates =
    {
        "terracotta", "terracotta.exe",       // 陶瓦联机
        "easytier-core", "easytier-core.exe", // EasyTier（陶瓦联机底层）
    };

    /// <summary>联机后端信息。</summary>
    public sealed record Backend(string Name, string Path);

    /// <summary>查找本机已安装的联机后端。</summary>
    public static Backend? FindBackend(string toolsDir)
    {
        // 1. 工具目录（递归，兼容 zip 内含一层目录）
        if (!string.IsNullOrEmpty(toolsDir) && Directory.Exists(toolsDir))
        {
            var found = SearchRecursive(toolsDir);
            if (found != null) return found;
        }

        // 2. PATH
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in BackendCandidates)
            {
                var p = Path.Combine(dir, name);
                if (File.Exists(p)) return new Backend(name, p);
            }
        }

        // 3. 当前目录 / 启动器目录
        foreach (var name in BackendCandidates)
        {
            var p = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(p)) return new Backend(name, p);
        }
        return null;
    }

    /// <summary>在目录树下递归查找联机后端可执行文件。</summary>
    private static Backend? SearchRecursive(string root)
    {
        if (!Directory.Exists(root)) return null;
        foreach (var name in BackendCandidates)
        {
            var p = Path.Combine(root, name);
            if (File.Exists(p)) return new Backend(Path.GetFileNameWithoutExtension(p), p);
        }
        foreach (var dir in Directory.GetDirectories(root))
        {
            var found = SearchRecursive(dir);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>生成房间码（形如 A4K9-3T8X，网络名+密钥）。</summary>
    public static string GenerateRoomCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var rnd = new Random();
        static string seg(Random r, string c) => new string(Enumerable.Range(0, 4).Select(_ => c[r.Next(c.Length)]).ToArray());
        return $"{seg(rnd, chars)}-{seg(rnd, chars)}";
    }

    /// <summary>房间码校验（A4K9-3T8X）。</summary>
    public static bool IsValidRoomCode(string? code) =>
        !string.IsNullOrEmpty(code) && System.Text.RegularExpressions.Regex.IsMatch(code.Trim(), @"^[A-Z0-9]{4}-[A-Z0-9]{4}$");

    /// <summary>启动组网（房主与加入者使用同一房间码即可进入同一虚拟网）。</summary>
    /// <returns>成功返回 true；out 为虚拟 IP（可解析时）或说明。</returns>
    public bool Start(string backendPath, string roomCode, out string? message)
    {
        Stop();
        if (!IsValidRoomCode(roomCode)) { message = "房间码格式不正确（应为 ABCD-EFGH）。"; return false; }

        var name = roomCode.Trim();
        _networkName = name;

        // 统一参数：网络名 + 密钥。陶瓦联机与 EasyTier 都基于相同组网语义。
        var args = BuildArgs(backendPath, name);
        try
        {
            var psi = new ProcessStartInfo(backendPath, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            _process = Process.Start(psi);
            if (_process == null) { message = "联机进程启动失败。"; return false; }

            // 异步读取日志，尝试解析虚拟 IP
            var _ = Task.Run(async () =>
            {
                try
                {
                    var buf = new System.Text.StringBuilder();
                    while (_process != null && !_process.HasExited)
                    {
                        var line = await _process.StandardOutput.ReadLineAsync();
                        if (line == null) break;
                        buf.AppendLine(line);
                        if (VirtualIp is null) ParseVirtualIp(line);
                    }
                }
                catch { }
            });

            message = $"已加入联机房间 {roomCode}。进入游戏后点「对局域网开放」，好友用虚拟 IP:端口 连接。";
            return true;
        }
        catch (Exception ex)
        {
            _process = null;
            message = "启动联机失败：" + ex.Message;
            return false;
        }
    }

    /// <summary>解析到的虚拟 IP（EasyTier 默认 10.144.x.x）。</summary>
    public string? VirtualIp { get; private set; }

    private void ParseVirtualIp(string line)
    {
        var m = System.Text.RegularExpressions.Regex.Match(line, @"\b(10\.144\.\d{1,3}\.\d{1,3})\b");
        if (m.Success) VirtualIp = m.Groups[1].Value;
    }

    /// <summary>根据后端类型构造启动参数。</summary>
    private static string BuildArgs(string backendPath, string roomCode)
    {
        var name = Path.GetFileNameWithoutExtension(backendPath).ToLowerInvariant();
        // EasyTier：-n 网络名 -k 密钥
        if (name.StartsWith("easytier"))
            return $"--network-name {roomCode} --network-secret {roomCode}";
        // 陶瓦联机（terracotta）：
        return $"--room {roomCode}";
    }

    /// <summary>停止组网。</summary>
    public void Stop()
    {
        try { _process?.Kill(); } catch { }
        _process?.Dispose();
        _process = null;
        VirtualIp = null;
        _networkName = null;
    }

    /// <summary>是否正在组网。</summary>
    public bool IsRunning => _process is { HasExited: false };

    /// <summary>当前房间码。</summary>
    public string? RoomCode => _networkName;
}
