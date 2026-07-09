using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZhongshengGasInspectionHmi.UI.Models;
using ZhongshengGasInspectionHmi.UI.Services;

namespace ZhongshengGasInspectionHmi.UI.ViewModels;

public sealed partial class RecordsPageViewModel : ObservableObject, IDisposable
{
    private readonly InspectionRecordStore _recordStore;
    private readonly HardwareSettings _hardwareSettings;

    public RecordsPageViewModel(InspectionRecordStore recordStore, HardwareSettings hardwareSettings)
    {
        _recordStore = recordStore;
        _hardwareSettings = hardwareSettings;
        _recordStore.RecordsChanged += OnRecordsChanged;
        _hardwareSettings.PropertyChanged += OnHardwareSettingsChanged;
        Records = [];
        _ = RefreshAsync();
    }

    public ObservableCollection<RecordRowViewModel> Records { get; }

    [ObservableProperty]
    private string _statusText = "正在加载检测记录。";

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            // 只查本机当前工位的记录，确保工位 1/工位 2 的检测记录完全独立。
            var records = await _recordStore.GetLatestAsync(200, _hardwareSettings.ActiveStationId, CancellationToken.None);
            ApplyRecords(records);
            StatusText = records.Count == 0
                ? $"{_hardwareSettings.StationName} 暂无检测记录。"
                : $"已加载 {_hardwareSettings.StationName} 最近 {records.Count} 条检测记录。";
        }
        catch (Exception ex)
        {
            StatusText = $"加载检测记录失败：{ex.Message}";
        }
    }

    private void OnRecordsChanged(object? sender, EventArgs e)
    {
        RefreshOnUi();
    }

    private void OnHardwareSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 切换工位时只显示新工位的记录。
        if (e.PropertyName == nameof(HardwareSettings.ActiveStationId))
        {
            RefreshOnUi();
        }
    }

    private void RefreshOnUi()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            _ = RefreshAsync();
            return;
        }

        dispatcher.Invoke(() => _ = RefreshAsync());
    }

    private void ApplyRecords(IReadOnlyList<InspectionRecord> records)
    {
        Records.Clear();
        foreach (var record in records)
        {
            Records.Add(new RecordRowViewModel(
                record.EndedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                string.IsNullOrWhiteSpace(record.StationName) ? $"工位{record.StationId}" : record.StationName,
                string.IsNullOrWhiteSpace(record.ProductCode) ? "--" : record.ProductCode,
                PressureFormatter.FormatKilopascal(record.P1),
                PressureFormatter.FormatKilopascal(record.P2),
                LeakRateFormatter.FormatRatio(record.LeakRate),
                record.Result));
        }
    }

    public void Dispose()
    {
        _recordStore.RecordsChanged -= OnRecordsChanged;
        _hardwareSettings.PropertyChanged -= OnHardwareSettingsChanged;
    }
}
