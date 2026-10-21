namespace NovaLauncher.Core.Models;

/// <summary>单个 Minecraft 版本元数据（version json）。</summary>
public sealed class MinecraftVersion
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "release";
    public string MainClass { get; set; } = "net.minecraft.client.main.Main";
    public string Assets { get; set; } = "";
    public AssetIndexInfo? AssetIndex { get; set; }
    public DownloadInfo? Client { get; set; }
    public List<LibraryItem> Libraries { get; set; } = new();
    /// <summary>游戏参数（新版 arguments.game 或旧版 minecraftArguments 解析）。</summary>
    public List<Argument> GameArguments { get; set; } = new();
    /// <summary>JVM 参数。</summary>
    public List<Argument> JvmArguments { get; set; } = new();
    public int JavaMajorVersion { get; set; } = 8;
    /// <summary>是否下载了 jar。</summary>
    public bool ClientDownloaded { get; set; }
}

public sealed class AssetIndexInfo
{
    public string Id { get; set; } = "";
    public string Url { get; set; } = "";
    public long Size { get; set; }
    public long Total { get; set; }
}

public sealed class DownloadInfo
{
    public string Url { get; set; } = "";
    public string Sha1 { get; set; } = "";
    public long Size { get; set; }
    public string? Path { get; set; }
}

/// <summary>库依赖项。</summary>
public sealed class LibraryItem
{
    public string Name { get; set; } = "";
    /// <summary>解析后的坐标（group/artifact/version）。</summary>
    public string[]? NameParts { get; set; }
    public DownloadInfo? Artifact { get; set; }
    /// <summary>分类文件（如 natives-linux）。</summary>
    public Dictionary<string, DownloadInfo> Classifiers { get; set; } = new();
    /// <summary>native 映射（os -> classifier key）。</summary>
    public Dictionary<string, string> Natives { get; set; } = new();
    public List<Rule> Rules { get; set; } = new();

    /// <summary>是否允许在当前平台使用。</summary>
    public bool Allowed => Rule.Allows(Rules);
}

/// <summary>启动参数（可为字符串或带规则的对象）。</summary>
public sealed class Argument
{
    public List<string> Values { get; set; } = new();
    public List<Rule> Rules { get; set; } = new();

    public bool Allowed => Rule.Allows(Rules);
}

public sealed class Rule
{
    public string Action { get; set; } = "allow";
    public OsRule? Os { get; set; }
    public FeaturesRule? Features { get; set; }

    /// <summary>对一组规则求值：默认 allow，任一 disallow 命中则拒绝，allow 命中则允许。</summary>
    public static bool Allows(List<Rule>? rules)
    {
        if (rules == null || rules.Count == 0) return true;
        bool allow = false;
        foreach (var r in rules)
        {
            if (!r.IsApplicable()) continue;
            if (r.Action == "disallow") return false;
            if (r.Action == "allow") allow = true;
        }
        return allow;
    }

    private bool IsApplicable()
    {
        if (Os != null)
        {
            var osName = Util.Platform.McOs;
            if (!string.IsNullOrEmpty(Os.Name) && !string.Equals(Os.Name, osName, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(Os.Arch))
            {
                var arch = Util.Platform.McArch;
                if (string.Equals(Os.Arch, "x86", StringComparison.OrdinalIgnoreCase) && arch != "32") return false;
                if (string.Equals(Os.Arch, "x86_64", StringComparison.OrdinalIgnoreCase) && arch != "64") return false;
            }
        }
        if (Features != null)
        {
            // 高级特性（demo user 等）默认不支持，均视为不满足。
            return false;
        }
        return true;
    }
}

public sealed class OsRule
{
    public string Name { get; set; } = "";
    public string Arch { get; set; } = "";
}

public sealed class FeaturesRule
{
}
