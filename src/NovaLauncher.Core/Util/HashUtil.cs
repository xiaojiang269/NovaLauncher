using System.Security.Cryptography;
using System.Text;

namespace NovaLauncher.Core.Util;

public static class HashUtil
{
    /// <summary>计算字符串的 SHA-1（小写十六进制）。</summary>
    public static string Sha1(string text)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>计算文件 SHA-1（小写十六进制）。</summary>
    public static string Sha1OfFile(string path)
    {
        using var stream = File.OpenRead(path);
        var bytes = SHA1.HashData(stream);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>根据昵称生成离线 UUID（与 HMCL 一致的 v3 风格，固定版本）。</summary>
    public static Guid OfflineUuid(string name)
    {
        // 使用与官方离线模式一致的 "OfflinePlayer:<name>" 命名空间 UUID v3。
        var ns = new Guid("fb4ae37b-250c-33c5-9e0f-123456789abc"); // 占位命名空间，实际用官方值
        // 官方离线 UUID = UUID.nameUUIDFromBytes("OfflinePlayer:" + name)
        var source = Encoding.UTF8.GetBytes("OfflinePlayer:" + name);
        return CreateVersion3Uuid(source);
    }

    private static Guid CreateVersion3Uuid(byte[] data)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(data);
        // 设为版本 3
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        // 设为 RFC 4122 变体
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        // 交换字节序以匹配 Java UUID
        Swap(ref hash[0], ref hash[3]);
        Swap(ref hash[1], ref hash[2]);
        Swap(ref hash[4], ref hash[5]);
        Swap(ref hash[6], ref hash[7]);
        return new Guid(hash);
    }

    private static void Swap(ref byte a, ref byte b) => (a, b) = (b, a);
}
