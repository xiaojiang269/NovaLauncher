using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLauncher.Core.Models;
using NovaLauncher.Core.Services;

namespace NovaLauncher.App.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    private readonly AccountService _accounts = AppServices.Accounts;
    private readonly VersionService _versions = AppServices.Versions;
    private readonly DownloadService _downloads = AppServices.Downloads;
    private readonly LaunchService _launch = AppServices.Launch;
    private readonly ExternalLoginService _external = new();
    private readonly AdoptiumApi _adoptium = new();

    // 账号
    [ObservableProperty] private string accountName = AppServices.Config.AccountName;
    [ObservableProperty] private string accountType;
    [ObservableProperty] private bool isLoggedIn;

    // 外置登录面板
    [ObservableProperty] private bool showExternalPanel;
    [ObservableProperty] private string externalApi = AppServices.Config.ExternalAuthServer;
    [ObservableProperty] private string externalUser = "";
    [ObservableProperty] private string externalPassword = "";

    // 版本
    public ObservableCollection<string> Versions { get; } = new();
    [ObservableProperty] private string? selectedVersion;

    // Java
    public ObservableCollection<JavaInfo> JavaList { get; } = new();
    [ObservableProperty] private JavaInfo? selectedJava;

    // 内存
    [ObservableProperty] private int minMemoryMb = AppServices.Config.MinMemoryMb;
    [ObservableProperty] private int maxMemoryMb = AppServices.Config.MaxMemoryMb;

    // 状态
    [ObservableProperty] private string status = "就绪";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool showProgress;
    [ObservableProperty] private int progressValue;
    [ObservableProperty] private int progressMax = 1;
    [ObservableProperty] private string progressText = "";
    [ObservableProperty] private string gameLog = "";
    public bool ShowGameLog => AppServices.Config.ShowGameLog;

    public HomeViewModel()
    {
        AccountType = AppServices.Config.AccountType switch
        {
            "microsoft" => "正版",
            "external" => "外置",
            _ => "离线",
        };
        IsLoggedIn = !string.IsNullOrWhiteSpace(AppServices.Config.AccountName);
        RefreshVersions();
        RefreshJava();
        if (!string.IsNullOrEmpty(AppServices.Config.SelectedVersion))
            SelectedVersion = Versions.FirstOrDefault(v => v == AppServices.Config.SelectedVersion) ?? Versions.FirstOrDefault();
        else
            SelectedVersion = Versions.FirstOrDefault();
    }

    private void RefreshVersions()
    {
        Versions.Clear();
        foreach (var v in AppServices.Dir.ListInstalledVersions()) Versions.Add(v);
    }

    private void RefreshJava()
    {
        JavaList.Clear();
        foreach (var j in JavaService.FindSystemJava().OrderByDescending(j => j.MajorVersion))
            JavaList.Add(j);
        foreach (var j in JavaService.FindBundled(AppServices.Dir.Java))
            if (!JavaList.Any(x => x.Path == j.Path)) JavaList.Add(j);

        SelectedJava = null;
        if (!string.IsNullOrEmpty(AppServices.Config.JavaPath))
            SelectedJava = JavaList.FirstOrDefault(j => j.Path == AppServices.Config.JavaPath);
        SelectedJava ??= JavaList.FirstOrDefault();
    }

    /// <summary>手动下载当前所选版本所需的 Java（Adoptium Temurin）。</summary>
    [RelayCommand]
    private async Task DownloadJavaAsync()
    {
        var major = 17;
        if (!string.IsNullOrEmpty(SelectedVersion))
        {
            var v = _versions.LoadLocal(SelectedVersion);
            if (v is { JavaMajorVersion: > 0 }) major = v.JavaMajorVersion;
        }
        Status = $"正在下载 Java {major}（Adoptium Temurin）…";
        var ok = await DownloadJavaForAsync(major);
        RefreshJava();
        Status = ok ? $"Java {major} 已就绪：{SelectedJava?.Path}" : "Java 下载失败，请检查网络";
    }

    private async Task<bool> DownloadJavaForAsync(int major)
    {
        IsBusy = true;
        ShowProgress = true;
        ProgressValue = 0;
        try
        {
            var p = new Progress<(long done, long total)>(x =>
                ProgressValue = x.total > 0 ? (int)(x.done / (double)x.total * 100) : 0);
            var path = await _adoptium.InstallAsync(major, AppServices.Dir.Java, p);
            return path != null;
        }
        catch { return false; }
        finally { IsBusy = false; ShowProgress = false; }
    }

    [RelayCommand]
    private void LoginOffline()
    {
        if (string.IsNullOrWhiteSpace(AccountName))
        {
            Status = "请先输入昵称";
            return;
        }
        var acc = _accounts.OfflineLogin(AccountName);
        AppServices.Config.AccountName = acc.Name;
        AppServices.Config.AccountUuid = acc.Uuid;
        AppServices.Config.AccountType = "offline";
        AppServices.SaveConfig();
        AccountName = acc.Name;
        AccountType = "离线";
        IsLoggedIn = true;
        Status = $"已以离线账号「{acc.Name}」登录";
    }

    [RelayCommand]
    private async Task LoginMicrosoftAsync()
    {
        if (string.IsNullOrWhiteSpace(AppServices.Config.MicrosoftClientId))
        {
            Status = "未配置 MicrosoftClientId，无法微软登录。可在设置页填写（需自行注册 Azure 应用）。";
            return;
        }
        IsBusy = true;
        Status = "微软登录：请在浏览器打开验证链接并输入代码…";
        try
        {
            var acc = await _accounts.MicrosoftLoginAsync(AppServices.Config.MicrosoftClientId,
                new Progress<(string code, string uri)>(r => Status = $"浏览器打开 {r.uri}，输入代码：{r.code}"));
            if (acc == null)
            {
                Status = "该微软账号未购买 Minecraft Java 版。";
                return;
            }
            AppServices.Config.AccountName = acc.Name;
            AppServices.Config.AccountUuid = acc.Uuid;
            AppServices.Config.AccountType = "microsoft";
            AppServices.SaveConfig();
            AccountName = acc.Name;
            AccountType = "正版";
            IsLoggedIn = true;
            Status = $"已以正版账号「{acc.Name}」登录";
        }
        catch (Exception ex)
        {
            Status = "登录失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ToggleExternalPanel() => ShowExternalPanel = !ShowExternalPanel;

    [RelayCommand]
    private async Task ExternalLoginAsync()
    {
        if (string.IsNullOrWhiteSpace(ExternalApi) || string.IsNullOrWhiteSpace(ExternalUser) || string.IsNullOrWhiteSpace(ExternalPassword))
        {
            Status = "请填写认证服务器地址、账号和密码";
            return;
        }
        IsBusy = true;
        try
        {
            Status = "正在连接第三方认证服务器…";
            var acc = await _external.LoginAsync(ExternalApi.Trim(), ExternalUser.Trim(), ExternalPassword);
            Status = "正在下载 authlib-injector…";
            await AuthlibInjectorService.EnsureDownloadedAsync(Path.Combine(AppServices.Dir.Root, "authlib-injector.jar"));

            AppServices.Config.AccountName = acc.Name;
            AppServices.Config.AccountUuid = acc.Uuid;
            AppServices.Config.AccountType = "external";
            AppServices.Config.ExternalAuthServer = acc.ExternalServer;
            AppServices.Config.ExternalAccessToken = acc.AccessToken;
            AppServices.SaveConfig();

            AccountName = acc.Name;
            AccountType = "外置";
            IsLoggedIn = true;
            ShowExternalPanel = false;
            Status = $"已通过第三方服务器登录为「{acc.Name}」";
        }
        catch (Exception ex)
        {
            Status = "外置登录失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            ExternalPassword = "";
        }
    }

    [RelayCommand]
    private async Task DownloadVersionAsync()
    {
        if (SelectedVersion == null) { Status = "请先选择版本"; return; }
        await RunDownloadAsync(SelectedVersion, downloadClient: true);
    }

    [RelayCommand]
    private async Task LaunchAsync()
    {
        if (!IsLoggedIn) { Status = "请先登录账号"; return; }
        if (SelectedVersion == null) { Status = "请先选择版本"; return; }
        if (SelectedJava == null) { Status = "未找到 Java 运行时，请安装 Java 或在设置中指定路径"; return; }

        var versionId = SelectedVersion;
        IsBusy = true;
        try
        {
            // 1. 确保版本完整（下载补全，幂等）
            var complete = await RunDownloadAsync(versionId, downloadClient: true);
            if (!complete) { return; }

            // 2. 加载版本
            var version = _versions.LoadLocal(versionId);
            if (version == null) { Status = "版本数据读取失败"; return; }

            // 3. Java 版本校验：不足则自动下载所需 Java
            if (version.JavaMajorVersion > SelectedJava.MajorVersion)
            {
                Status = $"版本 {versionId} 需要 Java {version.JavaMajorVersion}+，当前为 {SelectedJava.MajorVersion}，正在自动下载…";
                var ok = await DownloadJavaForAsync(version.JavaMajorVersion);
                if (!ok) { Status = $"需要 Java {version.JavaMajorVersion}+，自动下载失败，请检查网络或手动安装"; return; }
                RefreshJava();
                if (SelectedJava == null || SelectedJava.MajorVersion < version.JavaMajorVersion)
                {
                    Status = $"需要 Java {version.JavaMajorVersion}+，仍未满足，请在设置中指定 Java 路径";
                    return;
                }
            }

            // 4. 启动
            var isExternal = AppServices.Config.AccountType == "external";
            var options = new LaunchOptions
            {
                VersionId = versionId,
                Account = new AccountInfo
                {
                    Name = AppServices.Config.AccountName,
                    Uuid = AppServices.Config.AccountUuid,
                    Type = AppServices.Config.AccountType,
                    AccessToken = isExternal ? AppServices.Config.ExternalAccessToken : "0",
                    ExternalServer = AppServices.Config.ExternalAuthServer,
                },
                MinMemoryMb = MinMemoryMb,
                MaxMemoryMb = MaxMemoryMb,
                WindowWidth = AppServices.Config.WindowWidth,
                WindowHeight = AppServices.Config.WindowHeight,
                Fullscreen = AppServices.Config.Fullscreen,
                ExtraJvmArgs = AppServices.Config.ExtraJvmArgs,
            };

            Status = "正在启动游戏…";
            GameLog = "";
            var result = _launch.Launch(version, options, SelectedJava, new Progress<string>(l =>
            {
                GameLog += l + "\n";
                if (GameLog.Length > 8000) GameLog = GameLog[^8000..];
            }));
            if (result.Success)
            {
                Status = "游戏已启动，可以在日志页查看输出。";
            }
            else
            {
                Status = result.Message;
                GameLog = string.Join("\n", result.Log);
            }
        }
        catch (Exception ex)
        {
            Status = "启动失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            ShowProgress = false;
        }
    }

    /// <summary>下载补全版本。返回是否成功。</summary>
    private async Task<bool> RunDownloadAsync(string versionId, bool downloadClient)
    {
        var mirror = AppServices.MirrorName;
        MinecraftVersion? version;
        try
        {
            version = _versions.LoadLocal(versionId);
            if (version == null)
                version = await _versions.FetchRemoteAsync(versionId, null);
        }
        catch (Exception ex)
        {
            Status = "获取版本信息失败：" + ex.Message;
            return false;
        }

        ShowProgress = true;
        ProgressValue = 0;
        ProgressMax = 1;
        ProgressText = "准备下载…";
        Status = "正在下载游戏文件…";

        try
        {
            var progress = new Progress<DownloadProgress>(p =>
            {
                if (p.TotalItems > 0)
                {
                    ProgressMax = Math.Max(p.TotalItems, 1);
                    ProgressValue = Math.Min(p.DoneItems, ProgressMax);
                }
                ProgressText = $"{p.Phase}：{p.CurrentFile}  ({p.DoneItems}/{p.TotalItems})";
            });

            var summary = await _downloads.DownloadVersionAsync(version, versionId, progress, CancellationToken.None);
            if (summary.Success)
            {
                Status = $"下载完成：新增 {summary.Downloaded} 项，跳过 {summary.Skipped} 项";
                RefreshVersions();
                return true;
            }
            Status = $"下载存在失败项（{summary.Failed}），可重试：\n{string.Join("；", summary.Errors.Take(3))}";
            return false;
        }
        catch (Exception ex)
        {
            Status = "下载失败：" + ex.Message;
            return false;
        }
    }
}
