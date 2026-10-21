namespace NovaLauncher.Core.Models;

/// <summary>账号信息。</summary>
public sealed class AccountInfo
{
    public string Name { get; set; } = "";
    public string Uuid { get; set; } = "";
    /// <summary>offline / microsoft / external。</summary>
    public string Type { get; set; } = "offline";
    public string AccessToken { get; set; } = "";
    /// <summary>微软账号刷新令牌（可选）。</summary>
    public string RefreshToken { get; set; } = "";
    /// <summary>外置登录（authlib-injector）认证服务器 API 地址。</summary>
    public string ExternalServer { get; set; } = "";
}
