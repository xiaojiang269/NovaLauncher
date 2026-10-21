using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLauncher.Core.Services;

namespace NovaLauncher.App.ViewModels;

public partial class LanPlayViewModel : ViewModelBase
{
    private readonly LanPlayService _service = new();
    private readonly EasyTierInstaller _installer = new();

    [ObservableProperty] private string roomCode = "";
    [ObservableProperty] private string status = "";
    [ObservableProperty] private string backendStatus = "";
    [ObservableProperty] private string virtualIp = "";
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private bool showInstallProgress;
    [ObservableProperty] private int installProgressValue;
    [ObservableProperty] private string versionInfo = $"内置 EasyTier {EasyTierInstaller.Version}";

    [RelayCommand]
    private async Task CopyRoomCodeAsync()
    {
        if (string.IsNullOrWhiteSpace(RoomCode))
        {
            Status = "请先创建或输入房间码，再复制。";
            return;
        }
        var ok = await MainWindowRef.CopyToClipboardAsync(RoomCode.Trim());
        Status = ok ? $"已复制房间码 {RoomCode.Trim()}，可发给好友。" : "复制失败。";
    }

    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        Status = "正在检查 EasyTier 最新版本…";
        var latest = await EasyTierInstaller.GetLatestVersionAsync();
        if (latest == null)
        {
            Status = "检查更新失败（网络或限流），当前内置 " + EasyTierInstaller.Version;
            return;
        }
        LatestVersion = latest;
        var cur = EasyTierInstaller.Version;
        if (string.Equals(latest, cur, StringComparison.OrdinalIgnoreCase))
        {
            HasUpdate = false;
            Status = $"EasyTier 已是最新版本（{latest}）。";
        }
        else
        {
            HasUpdate = true;
            Status = $"发现新版本 {latest}（当前内置 {cur}），可点击「升级 EasyTier」更新。";
        }
    }

    [ObservableProperty] private string latestVersion = "";
    [ObservableProperty] private bool hasUpdate;

    [RelayCommand]
    private async Task UpgradeAsync()
    {
        if (string.IsNullOrEmpty(LatestVersion))
        {
            Status = "请先「检查更新」获取最新版本号。";
            return;
        }
        ShowInstallProgress = true;
        InstallProgressValue = 0;
        Status = $"正在升级 EasyTier 到 {LatestVersion}…";
        try
        {
            var p = new Progress<(long done, long total)>(x =>
                InstallProgressValue = x.total > 0 ? (int)(x.done / (double)x.total * 100) : 0);
            var bin = await _installer.InstallAsync(AppServices.Dir.Tools, p, version: LatestVersion);
            VersionInfo = $"EasyTier {LatestVersion}";
            Status = bin == null ? "升级失败，请检查网络后重试。" : $"EasyTier 已升级到 {LatestVersion}：{bin}";
        }
        catch (Exception ex)
        {
            Status = "升级失败：" + ex.Message;
        }
        finally
        {
            ShowInstallProgress = false;
            RefreshBackend();
        }
    }

    public LanPlayViewModel()
    {
        RefreshBackend();
    }

    private void RefreshBackend()
    {
        var b = LanPlayService.FindBackend(AppServices.Dir.Tools);
        BackendStatus = b == null
            ? "未检测到联机组件，可点击下方按钮一键下载安装 EasyTier（陶瓦联机底层）。"
            : $"已检测到联机后端：{b.Name}（{b.Path}）";
    }

    [RelayCommand]
    private async Task InstallBackendAsync()
    {
        if (LanPlayService.FindBackend(AppServices.Dir.Tools) != null)
        {
            Status = "联机组件已安装，可直接开始联机。";
            RefreshBackend();
            return;
        }
        ShowInstallProgress = true;
        InstallProgressValue = 0;
        Status = $"正在下载安装 EasyTier {EasyTierInstaller.Version}（{EasyTierInstaller.AssetName()}）…";
        try
        {
            var p = new Progress<(long done, long total)>(x =>
                InstallProgressValue = x.total > 0 ? (int)(x.done / (double)x.total * 100) : 0);
            var bin = await _installer.InstallAsync(AppServices.Dir.Tools, p);
            Status = bin == null ? "安装失败，请检查网络后重试。" : $"EasyTier 已安装：{bin}";
        }
        catch (Exception ex)
        {
            Status = "安装失败：" + ex.Message;
        }
        finally
        {
            ShowInstallProgress = false;
            RefreshBackend();
        }
    }

    [RelayCommand]
    private void CreateRoom()
    {
        RoomCode = LanPlayService.GenerateRoomCode();
        Status = $"已生成房间码 {RoomCode}，点击「开始联机」并把它发给好友。";
    }

    [RelayCommand]
    private void Start()
    {
        var b = LanPlayService.FindBackend(AppServices.Dir.Tools);
        if (b == null)
        {
            Status = "未找到联机组件。请先安装陶瓦联机或 EasyTier。";
            RefreshBackend();
            return;
        }
        if (!LanPlayService.IsValidRoomCode(RoomCode))
        {
            Status = "房间码格式不正确（应为 ABCD-EFGH）。点「创建房间」自动生成。";
            return;
        }
        var ok = _service.Start(b.Path, RoomCode, out var msg);
        Status = msg ?? "";
        if (ok)
        {
            IsRunning = true;
            RefreshBackend();
        }
        else
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private void Stop()
    {
        _service.Stop();
        IsRunning = false;
        Status = "已停止联机。";
    }

    partial void OnIsRunningChanged(bool value)
    {
        if (!value) VirtualIp = "";
        else if (!string.IsNullOrEmpty(_service.VirtualIp)) VirtualIp = _service.VirtualIp!;
    }
}
