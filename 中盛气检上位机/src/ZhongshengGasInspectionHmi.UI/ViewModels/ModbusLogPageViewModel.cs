using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZhongshengGasInspectionHmi.UI.Services;

namespace ZhongshengGasInspectionHmi.UI.ViewModels;

public sealed partial class ModbusLogPageViewModel : ObservableObject, IDisposable
{
    private readonly IModbusCommunicationLog _communicationLog;

    public ModbusLogPageViewModel(IModbusCommunicationLog communicationLog)
    {
        _communicationLog = communicationLog;
        CommunicationLogs = new ObservableCollection<ModbusCommunicationLogEntry>(communicationLog.Entries);
        _communicationLog.EntryAdded += OnCommunicationLogEntryAdded;
        _communicationLog.Cleared += OnCommunicationLogCleared;
    }

    public ObservableCollection<ModbusCommunicationLogEntry> CommunicationLogs { get; }

    [ObservableProperty]
    private string _statusText = "TX/RX 十六进制帧用于核对功能码、站号、协议模式和地址。";

    [RelayCommand]
    private void ClearLogs()
    {
        _communicationLog.Clear();
        StatusText = "通信日志已清空。";
    }

    private void OnCommunicationLogEntryAdded(object? sender, ModbusCommunicationLogEntry entry)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            AddCommunicationLogEntry(entry);
            return;
        }

        _ = dispatcher.InvokeAsync(() => AddCommunicationLogEntry(entry));
    }

    private void OnCommunicationLogCleared(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            CommunicationLogs.Clear();
            return;
        }

        _ = dispatcher.InvokeAsync(CommunicationLogs.Clear);
    }

    private void AddCommunicationLogEntry(ModbusCommunicationLogEntry entry)
    {
        CommunicationLogs.Add(entry);
        while (CommunicationLogs.Count > 120)
        {
            CommunicationLogs.RemoveAt(0);
        }
    }

    public void Dispose()
    {
        _communicationLog.EntryAdded -= OnCommunicationLogEntryAdded;
        _communicationLog.Cleared -= OnCommunicationLogCleared;
    }
}
