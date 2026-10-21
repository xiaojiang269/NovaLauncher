using System.Diagnostics;
using NovaLauncher.Core.Configuration;
using NovaLauncher.Core.Util;

namespace NovaLauncher.Core.Services;

/// <summary>Java 运行时信息。</summary>
public sealed class JavaInfo
{
    public string Path { get; set; } = "";
    public int MajorVersion { get; set; }
    public string Version { get; set; } = "";
    public bool IsValid => File.Exists(Path);

    public override string ToString() => $"{Path} (Java {Version})";
}

/// <summary>Java 运行时检测与校验。</summary>
public sealed class JavaService
{
    /// <summary>探测指定 java 可执行文件。</summary>
    public static JavaInfo? Probe(string javaExe)
    {
        if (string.IsNullOrEmpty(javaExe) || !File.Exists(javaExe)) return null;
        var dir = Path.GetDirectoryName(javaExe);
        var releaseFile = Path.Combine(dir ?? "", "release");

        // 优先读取 release 文件（快速）
        if (File.Exists(releaseFile))
        {
            try
            {
                var text = File.ReadAllText(releaseFile);
                var m = System.Text.RegularExpressions.Regex.Match(text, "JAVA_VERSION=\"([^\"]+)\"");
                if (m.Success)
                {
                    var ver = m.Groups[1].Value;
                    return new JavaInfo { Path = javaExe, Version = ver, MajorVersion = ParseMajor(ver) };
                }
            }
            catch { }
        }

        // 回退：运行 java -version
        try
        {
            var psi = new ProcessStartInfo(javaExe, "-version")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p == null) return null;
            var err = p.StandardError.ReadToEnd();
            var outStr = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            var text = err + outStr;
            var m = System.Text.RegularExpressions.Regex.Match(text, "version \"([^\"]+)\"");
            if (m.Success)
            {
                var ver = m.Groups[1].Value;
                return new JavaInfo { Path = javaExe, Version = ver, MajorVersion = ParseMajor(ver) };
            }
        }
        catch { }

        return new JavaInfo { Path = javaExe, Version = "未知", MajorVersion = 8 };
    }

    /// <summary>自动查找系统 Java（JAVA_HOME 与 PATH）。</summary>
    public static List<JavaInfo> FindSystemJava()
    {
        var results = new List<JavaInfo>();
        var candidates = new List<string>();

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            candidates.Add(Path.Combine(javaHome, "bin", Platform.JavaExecutableName));
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            candidates.Add(Path.Combine(dir, Platform.JavaExecutableName));
            candidates.Add(Path.Combine(dir, "java"));
        }

        foreach (var c in candidates.Distinct())
        {
            var info = Probe(c);
            if (info is { IsValid: true } && !results.Any(r => string.Equals(r.Path, info.Path, StringComparison.OrdinalIgnoreCase)))
                results.Add(info);
        }

        return results;
    }

    /// <summary>查找启动器自带 java 目录下的运行时。</summary>
    public static List<JavaInfo> FindBundled(string javaDir)
    {
        var results = new List<JavaInfo>();
        if (!Directory.Exists(javaDir)) return results;
        foreach (var dir in Directory.GetDirectories(javaDir))
        {
            var exe = Path.Combine(dir, "bin", Platform.JavaExecutableName);
            if (File.Exists(exe))
            {
                var info = Probe(exe);
                if (info != null) results.Add(info);
            }
        }
        return results;
    }

    /// <summary>从版本字符串解析主版本号（1.8 -> 8，17 -> 17）。</summary>
    private static int ParseMajor(string version)
    {
        var m = System.Text.RegularExpressions.Regex.Match(version, @"^(?:1\.)?(\d+)");
        if (!m.Success) return 0;
        return int.Parse(m.Groups[1].Value);
    }

    /// <summary>按所需主版本挑选 Java：优先内置（已下载）满足版本 → 系统满足版本 → 内置任意 → 系统任意。</summary>
    public static JavaInfo? PickJava(string configuredPath, int requiredMajor, string bundledDir)
    {
        // 1. 用户显式配置
        if (!string.IsNullOrEmpty(configuredPath))
        {
            var p = Probe(configuredPath);
            if (p is { IsValid: true } && p.MajorVersion >= requiredMajor) return p;
        }
        // 2. 内置已下载的（满足版本优先）
        var bundled = FindBundled(bundledDir);
        var b = bundled.FirstOrDefault(j => j.MajorVersion >= requiredMajor) ?? bundled.FirstOrDefault();
        if (b != null && b.MajorVersion >= requiredMajor) return b;
        // 3. 系统满足版本
        var sys = FindSystemJava();
        var s = sys.FirstOrDefault(j => j.MajorVersion >= requiredMajor);
        if (s != null) return s;
        // 4. 兜底：内置任意 / 配置 / 系统任意
        return b ?? Probe(configuredPath ?? "") ?? sys.FirstOrDefault();
    }

    /// <summary>判断所需版本是否已被满足（用于决定是否需要自动下载 Java）。</summary>
    public static bool IsSatisfied(int requiredMajor, string bundledDir, JavaInfo? current = null)
    {
        if (current is { MajorVersion: > 0 } && current.MajorVersion >= requiredMajor) return true;
        var bundled = FindBundled(bundledDir);
        return bundled.Any(j => j.MajorVersion >= requiredMajor);
    }
}
