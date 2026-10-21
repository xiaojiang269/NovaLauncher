using System.Runtime.InteropServices;

namespace NovaLauncher.Core.Util;

/// <summary>操作系统与架构检测。</summary>
public static class Platform
{
    public enum OsType { Windows, Linux, MacOs, Other }

    public static OsType CurrentOs =>
        OperatingSystem.IsWindows() ? OsType.Windows :
        OperatingSystem.IsLinux() ? OsType.Linux :
        OperatingSystem.IsMacOS() ? OsType.MacOs : OsType.Other;

    /// <summary>Minecraft 库规则中使用的 OS 名。</summary>
    public static string McOs =>
        CurrentOs switch
        {
            OsType.Windows => "windows",
            OsType.Linux => "linux",
            OsType.MacOs => "osx",
            _ => "unknown",
        };

    /// <summary>Minecraft natives 后缀（如 natives-windows）。</summary>
    public static string NativesSuffix => $"natives-{McOs}";

    /// <summary>Java 可执行文件名。</summary>
    public static string JavaExecutableName =>
        CurrentOs == OsType.Windows ? "javaw.exe" : "java";

    /// <summary>是否 64 位架构。</summary>
    public static bool Is64Bit => RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;

    /// <summary>当前架构的 MC 表示。</summary>
    public static string McArch => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "64",
        Architecture.X86 => "32",
        Architecture.Arm64 => "arm64",
        _ => "64",
    };
}
