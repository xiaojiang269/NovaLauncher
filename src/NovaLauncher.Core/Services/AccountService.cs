using NovaLauncher.Core.Models;
using NovaLauncher.Core.Util;

namespace NovaLauncher.Core.Services;

/// <summary>账号管理：离线登录与微软登录入口。</summary>
public sealed class AccountService
{
    private readonly MicrosoftLoginService _ms = new();

    /// <summary>离线登录：根据昵称生成 UUID。</summary>
    public AccountInfo OfflineLogin(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("昵称不能为空");
        return new AccountInfo
        {
            Name = name.Trim(),
            Uuid = HashUtil.OfflineUuid(name.Trim()).ToString(),
            Type = "offline",
            AccessToken = "0",
        };
    }

    /// <summary>微软登录（Device Code 流程）。</summary>
    public Task<AccountInfo?> MicrosoftLoginAsync(string clientId, IProgress<(string userCode, string verifyUri)>? report, CancellationToken ct = default)
        => _ms.LoginAsync(clientId, report, ct);
}
