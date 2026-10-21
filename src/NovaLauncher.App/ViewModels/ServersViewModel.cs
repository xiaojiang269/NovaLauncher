using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLauncher.Core.Models;
using NovaLauncher.Core.Services;

namespace NovaLauncher.App.ViewModels;

public partial class ServersViewModel : ViewModelBase
{
    private readonly ServerService _servers = AppServices.Servers;

    public ObservableCollection<ServerEntry> Servers { get; } = new();

    [ObservableProperty] private ServerEntry? selected;
    [ObservableProperty] private string editName = "";
    [ObservableProperty] private string editAddress = "";
    [ObservableProperty] private string editPort = "25565";
    [ObservableProperty] private string editDescription = "";
    [ObservableProperty] private string status = "就绪";
    [ObservableProperty] private bool isEditing;

    public ServersViewModel()
    {
        foreach (var s in _servers.Load()) Servers.Add(s);
    }

    private void Save() => _servers.Save(Servers.ToList());

    [RelayCommand]
    private void New()
    {
        IsEditing = true;
        EditName = ""; EditAddress = ""; EditPort = "25565"; EditDescription = "";
        Status = "填写服务器信息后点击保存。";
    }

    [RelayCommand]
    private void EditSelected()
    {
        if (Selected == null) return;
        IsEditing = true;
        EditName = Selected.Name;
        EditAddress = Selected.Address;
        EditPort = Selected.Port.ToString();
        EditDescription = Selected.Description;
    }

    [RelayCommand]
    private void SaveEntry()
    {
        if (string.IsNullOrWhiteSpace(EditName) || string.IsNullOrWhiteSpace(EditAddress))
        {
            Status = "名称和地址不能为空";
            return;
        }
        if (!int.TryParse(EditPort, out var port) || port is < 1 or > 65535)
        {
            Status = "端口无效（1-65535）";
            return;
        }
        var entry = Selected;
        if (entry == null || !Servers.Contains(entry))
        {
            entry = new ServerEntry();
            Servers.Add(entry);
        }
        entry.Name = EditName.Trim();
        entry.Address = EditAddress.Trim();
        entry.Port = port;
        entry.Description = EditDescription;
        Save();
        IsEditing = false;
        Selected = entry;
        Status = $"已保存服务器「{entry.Name}」";
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (Selected == null) return;
        Servers.Remove(Selected);
        Selected = null;
        Save();
        Status = "已删除服务器";
    }
}
