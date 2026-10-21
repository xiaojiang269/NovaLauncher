using NovaLauncher.Core.Models;

namespace NovaLauncher.Core.Services;

/// <summary>把库解析为具体的下载项（路径 + URL）。</summary>
public static class LibraryResolver
{
    /// <summary>根据 name 推导默认 Maven 路径：com/example/artifact/ver/artifact-ver.jar。</summary>
    public static string DefaultPathFromName(string[] parts)
    {
        if (parts.Length < 3) return parts[0].Replace('.', '/') + "/" + parts[^1] + ".jar";
        var group = string.Join('/', parts[..^2].Select(p => p.Replace('.', '/')));
        var artifact = parts[^2];
        var version = parts[^1];
        return $"{group}/{artifact}/{version}/{artifact}-{version}.jar";
    }

    /// <summary>计算某个库主 jar 的本地相对路径。</summary>
    public static string ResolveMainPath(LibraryItem lib)
    {
        if (!string.IsNullOrEmpty(lib.Artifact?.Path)) return lib.Artifact.Path;
        return DefaultPathFromName(lib.NameParts ?? lib.Name.Split(':'));
    }

    /// <summary>计算某个库 native 分类文件的本地相对路径。</summary>
    public static string? ResolveNativePath(LibraryItem lib)
    {
        if (!lib.Natives.TryGetValue(Util.Platform.McOs, out var key)) return null;
        if (lib.Classifiers.TryGetValue(key, out var dl) && !string.IsNullOrEmpty(dl.Path))
            return dl.Path;
        // 用 name 推导 native 路径
        var parts = lib.NameParts ?? lib.Name.Split(':');
        if (parts.Length < 3) return null;
        var group = string.Join('/', parts[..^2].Select(p => p.Replace('.', '/')));
        var artifact = parts[^2];
        var version = parts[^1];
        return $"{group}/{artifact}/{version}/{artifact}-{version}-{key}.jar";
    }

    /// <summary>当前平台实际需要的库（主 jar 或 natives）。</summary>
    public static IEnumerable<(LibraryItem Lib, DownloadInfo? Dl, bool IsNative)> ResolveNeeded(LibraryItem lib)
    {
        if (!lib.Allowed) yield break;
        if (lib.Natives.Count > 0)
        {
            // native 库：需要对应分类文件
            if (lib.Natives.TryGetValue(Util.Platform.McOs, out var key) &&
                lib.Classifiers.TryGetValue(key, out var nativeDl))
                yield return (lib, nativeDl, true);
        }
        else
        {
            yield return (lib, lib.Artifact, false);
        }
    }
}
