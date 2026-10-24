using System.Text.RegularExpressions;

namespace NovaLauncher.Core.Services;

/// <summary>崩溃分析结果。</summary>
public sealed class CrashAnalysis
{
    public bool HasCrash { get; set; }
    public int ExitCode { get; set; }
    public string Reason { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Suggestion { get; set; } = "";
    public string LogTail { get; set; } = "";
}

/// <summary>游戏崩溃自动分析：扫描启动日志与崩溃报告，定位崩溃原因并给出修复建议（对齐 PCL2 的崩溃分析）。</summary>
public sealed class CrashAnalyzer
{
    private sealed record Rule(Regex Pattern, string Reason, string Suggestion, int Priority);

    // 崩溃特征规则：按优先级从高到低匹配。仅当退出码非 0 时才判定为崩溃。
    private static readonly Rule[] Rules =
    {
        new(new Regex(@"OutOfMemoryError|Java heap space|GC overhead limit exceeded", RegexOptions.IgnoreCase),
            "内存不足（OutOfMemory）",
            "游戏内存被耗尽。请在主页或设置中调大「最大内存」；若已设很大仍崩溃，多为模组过多或某模组内存泄漏，可尝试移除部分模组。", 100),
        new(new Regex(@"Could not create the Java Virtual Machine|Error occurred during initialization of VM|Invalid maximum heap size|Could not reserve enough space", RegexOptions.IgnoreCase),
            "JVM 初始化失败",
            "无法创建 Java 虚拟机，通常是「最大内存」设置超过电脑可用内存，或 Java 为 32 位。请调小内存上限，并确保使用 64 位 Java。", 90),
        new(new Regex(@"UnsupportedClassVersionError|class file version [0-9.]+ is|has been compiled by a more recent version", RegexOptions.IgnoreCase),
            "Java 版本过低",
            "游戏或模组需要更高版本的 Java。请在主页点「下载 Java」获取对应版本，或在设置中指定更高版本的 Java 路径。", 85),
        new(new Regex(@"NoClassDefFoundError|ClassNotFoundException|java\.lang\.NoSuchMethodError|NoSuchFieldError", RegexOptions.IgnoreCase),
            "缺失或冲突的类 / 库",
            "缺失库文件，或模组 / 库之间版本冲突。请回主页点「下载补全」重新获取完整资源，并检查是否安装了冲突模组。", 80),
        new(new Regex(@"ModLoadingException|ModResolutionException|Mod resolution (?:failed|exception)|FormattedException|FabricLoader|quilt_loader.*(?:error|failed)|Error loading mod|Failed to load mod|Failed to find version", RegexOptions.IgnoreCase),
            "模组加载失败",
            "某个模组无法加载（游戏版本或加载器不匹配、依赖缺失）。请在「模组管理」页检查并移除问题模组，或重装对应加载器。", 75),
        new(new Regex(@"A fatal error has been detected by the Java Runtime Environment|SIGSEGV|SIGILL|SIGBUS|hs_err_pid", RegexOptions.IgnoreCase),
            "Java 运行时原生崩溃",
            "Java 进程发生原生级崩溃，多为显卡驱动或 Java 环境问题。请更新显卡驱动，或更换 / 重装 Java。", 70),
        new(new Regex(@"GLFW error|Couldn't initialize|GLException|No OpenGL context|BadMatch|libGL\.so", RegexOptions.IgnoreCase),
            "图形 / OpenGL 初始化失败",
            "显卡或 OpenGL 环境异常。请更新显卡驱动；若在虚拟机 / 远程桌面，需启用软件渲染或检查显卡直通。", 65),
        new(new Regex(@"UnknownHostException|ConnectException|java\.net\.|Connection refused|timed out|Could not resolve host", RegexOptions.IgnoreCase),
            "网络连接问题",
            "无法连接服务器或下载源。请检查网络连接；下载相关报错可回主页点「下载补全」重试。", 60),
        new(new Regex(@"Can't open resource index file|Invalid resource pack|Failed to download file|Unable to find .*\.jar|Error: Could not find or load main class", RegexOptions.IgnoreCase),
            "游戏资源缺失",
            "缺少游戏资源文件或主类。请回主页点「下载补全」重新获取缺失资源后再启动。", 55),
        new(new Regex(@"Exception in thread ""main""|at net\.minecraft|at com\.mojang|at net\.fabricmc|at net\.neoforged", RegexOptions.IgnoreCase),
            "游戏主线程异常",
            "游戏主线程抛出异常导致崩溃。请查看下方「崩溃详情」定位具体错误。", 50),
    };

    /// <summary>分析游戏数据目录下的日志与崩溃报告。exitCode 为 0（正常退出）时不判定为崩溃。</summary>
    public CrashAnalysis Analyze(string gameDataDir, int exitCode)
    {
        var result = new CrashAnalysis { ExitCode = exitCode };

        // 正常退出（exit 0）不视为崩溃
        if (exitCode == 0)
        {
            result.HasCrash = false;
            return result;
        }

        var log = ReadLatestLog(gameDataDir);
        var crashReport = ReadLatestCrashReport(gameDataDir);
        var text = log + "\n" + crashReport;

        // 空日志且退出码非 0：启动阶段即失败（未产出游戏日志）
        if (string.IsNullOrWhiteSpace(text))
        {
            result.HasCrash = true;
            result.Reason = "游戏启动后立即退出";
            result.Detail = $"退出码：{exitCode}，未检测到有效游戏日志。";
            result.Suggestion = "请回主页点「下载补全」确保版本完整后再启动；若仍失败，可能是 Java 版本不匹配，请点「下载 Java」。";
            return result;
        }

        // 按优先级匹配特征
        foreach (var rule in Rules.OrderByDescending(r => r.Priority))
        {
            if (rule.Pattern.IsMatch(text))
            {
                result.HasCrash = true;
                result.Reason = rule.Reason;
                result.Suggestion = rule.Suggestion;
                result.Detail = ExtractDetail(text);
                result.LogTail = Tail(log, 40);
                return result;
            }
        }

        // 未命中规则但退出码非 0
        result.HasCrash = true;
        result.Reason = $"游戏异常退出（退出码 {exitCode}）";
        result.Detail = ExtractDetail(text);
        result.Suggestion = "未能自动定位具体原因，请把下方日志尾部反馈给作者排查。";
        result.LogTail = Tail(log, 40);
        return result;
    }

    private static string ReadLatestLog(string gameDataDir)
    {
        try
        {
            var f = Path.Combine(gameDataDir, "logs", "latest.log");
            if (!File.Exists(f)) return "";
            return File.ReadAllText(f, System.Text.Encoding.UTF8);
        }
        catch { return ""; }
    }

    private static string ReadLatestCrashReport(string gameDataDir)
    {
        try
        {
            var dir = Path.Combine(gameDataDir, "crash-reports");
            if (!Directory.Exists(dir)) return "";
            var file = Directory.GetFiles(dir, "*.txt")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .FirstOrDefault();
            return file == null ? "" : File.ReadAllText(file, System.Text.Encoding.UTF8);
        }
        catch { return ""; }
    }

    private static string ExtractDetail(string text)
    {
        var lines = text.Split('\n');
        foreach (var raw in lines)
        {
            var t = raw.Trim();
            if (t.StartsWith("Exception", StringComparison.Ordinal)
                || t.StartsWith("Caused by", StringComparison.Ordinal)
                || t.StartsWith("Error:", StringComparison.Ordinal)
                || t.StartsWith("--", StringComparison.Ordinal) && t.Contains("Exception", StringComparison.OrdinalIgnoreCase))
                return t.Length > 240 ? t[..240] : t;
        }
        var first = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "";
        return (first.Length > 300 ? first[..300] : first);
    }

    private static string Tail(string text, int lines)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var arr = text.Split('\n');
        var count = Math.Min(lines, arr.Length);
        return string.Join("\n", arr, arr.Length - count, count);
    }
}
