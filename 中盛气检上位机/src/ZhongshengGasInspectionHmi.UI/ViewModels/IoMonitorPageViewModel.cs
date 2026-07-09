using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZhongshengGasInspectionHmi.UI.Models;
using ZhongshengGasInspectionHmi.UI.Services;

namespace ZhongshengGasInspectionHmi.UI.ViewModels;

public sealed partial class IoMonitorPageViewModel : ObservableObject, IDisposable
{
    private const int DigitalPointCount = 4;
    private readonly IIoMonitorHardware _ioHardware;
    private readonly IInspectionHardware _inspectionHardware;
    private readonly HardwareSettings _settings;
    private int _manualValveActionVersion;

    public IoMonitorPageViewModel(
        IIoMonitorHardware ioHardware,
        IInspectionHardware inspectionHardware,
        HardwareSettings settings)
    {
        _ioHardware = ioHardware;
        _inspectionHardware = inspectionHardware;
        _settings = settings;
        _settings.PropertyChanged += OnHardwareSettingsChanged;
        Inputs = [];
        Outputs = [];
        for (var index = 0; index < DigitalPointCount; index++)
        {
            Inputs.Add(new IoPointViewModel(index, "DI", index switch
            {
                0 => "备用输入1",
                1 => "备用输入2",
                2 => "备用输入3",
                _ => "备用输入4"
            }, false));
        }
        RebuildOutputs();
    }

    public ObservableCollection<IoPointViewModel> Inputs { get; }

    public ObservableCollection<IoPointViewModel> Outputs { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeText))]
    [NotifyPropertyChangedFor(nameof(ModeDetailText))]
    private bool _isManualMode;

    [ObservableProperty]
    private string _statusText = "RJ45 4IO：DI 使用 02H 读取 4 点，DO 使用 01H/05H 读取；阀门 DO 最长按设定时间通电，过程中可直接切换方向。仅显示本工位阀门 DO。";

    public string ModeText => IsManualMode ? "手动模式" : "自动模式";

    public string ModeDetailText => IsManualMode
        ? $"{_settings.StationName}：DO{_settings.InletOpenCoil + 1} / DO{_settings.InletCloseCoil + 1} 可手动点动"
        : "检测流程自动控制 DO 输出";

    public string OutputMappingText =>
        $"{_settings.StationName}：DO{_settings.InletOpenCoil + 1} 打开进气阀，DO{_settings.InletCloseCoil + 1} 关闭进气阀；仅显示本工位阀门 DO。";

    [RelayCommand]
    private async Task RefreshAsync()
    {
        List<string> messages = [];
        try
        {
            var inputs = await _ioHardware.ReadDigitalInputsAsync(CancellationToken.None);
            ApplyStates(Inputs, inputs);
            messages.Add("DI 已刷新");
        }
        catch (Exception ex)
        {
            messages.Add($"DI 读取失败：{ex.Message}");
        }

        try
        {
            var outputs = await _ioHardware.ReadDigitalOutputsAsync(CancellationToken.None);
            ApplyStates(Outputs, outputs);
            messages.Add("DO 已刷新");
        }
        catch (Exception ex)
        {
            messages.Add($"DO 读取失败：{ex.Message}");
        }

        StatusText = string.Join("；", messages);
    }

    [RelayCommand]
    private void SetAutoMode()
    {
        IsManualMode = false;
        StatusText = "已切换到自动模式，输出由检测流程控制。";
        UpdateOutputPermission();
    }

    [RelayCommand]
    private void SetManualMode()
    {
        IsManualMode = true;
        StatusText = $"已切换到手动模式，{_settings.StationName} 的 DO{_settings.InletOpenCoil + 1} 打开线和 DO{_settings.InletCloseCoil + 1} 关闭线可随时互相切换。";
        UpdateOutputPermission();
    }

    [RelayCommand]
    private Task ToggleOutputAsync(IoPointViewModel? point)
    {
        if (point is null || !IsManualMode)
        {
            StatusText = "当前为自动模式，禁止手动写 DO。";
            return Task.CompletedTask;
        }

        // Outputs 只含本工位阀门 DO，点击即可，无需再做越界/越工位判断。
        _ = RunManualValveActionAsync(point);
        return Task.CompletedTask;
    }

    private async Task RunManualValveActionAsync(IoPointViewModel point)
    {
        var version = Interlocked.Increment(ref _manualValveActionVersion);
        try
        {
            SetValveOutputDisplay(point.Index);
            StatusText = $"{point.Address} 正在给{GetValveActionName(point.Index)}线通电，最长 {GetValveActionSecondsText()}；可直接点击另一方向切换。";
            if (point.Index == _settings.InletOpenCoil)
            {
                await _inspectionHardware.OpenInletValveAsync(CancellationToken.None);
                if (version == _manualValveActionVersion)
                {
                    StatusText = $"{point.Address} 已完成进气阀打开动作通电。";
                }
            }
            else
            {
                await _inspectionHardware.CloseInletValveAsync(CancellationToken.None);
                if (version == _manualValveActionVersion)
                {
                    StatusText = $"{point.Address} 已完成进气阀关闭动作通电。";
                }
            }

            if (version == _manualValveActionVersion)
            {
                ClearOutputDisplay();
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            if (version == _manualValveActionVersion)
            {
                ClearOutputDisplay();
                StatusText = $"{point.Address} 写入失败：{ex.Message}";
            }
        }
    }

    [RelayCommand]
    private Task ResetOutputsAsync()
    {
        _ = RunResetOutputsAsync();
        return Task.CompletedTask;
    }

    private async Task RunResetOutputsAsync()
    {
        var version = Interlocked.Increment(ref _manualValveActionVersion);
        try
        {
            SetValveOutputDisplay(_settings.InletCloseCoil);
            StatusText = $"正在给进气阀关闭线通电，最长 {GetValveActionSecondsText()}；可直接点击打开线切换。";
            await _ioHardware.ResetKnownOutputsAsync(CancellationToken.None);

            if (version == _manualValveActionVersion)
            {
                ClearOutputDisplay();
                StatusText = $"已复位{_settings.StationName}输出：仅执行 DO{_settings.InletCloseCoil + 1} 进气阀关闭动作；其他工位 DO 不写入。";
            }
        }
        catch (Exception ex)
        {
            if (version == _manualValveActionVersion)
            {
                ClearOutputDisplay();
                StatusText = $"复位输出失败：{ex.Message}";
            }
        }
    }

    private static void ApplyStates(IReadOnlyList<IoPointViewModel> points, IReadOnlyList<bool> states)
    {
        // 用每个点的真实 DO/DI 地址（point.Index）去读对应通道状态，
        // 这样 Outputs 即使只含本工位两个 DO，也能正确反映其物理状态。
        foreach (var point in points)
        {
            if (point.Index >= 0 && point.Index < states.Count)
            {
                point.IsOn = states[point.Index];
            }
        }
    }

    private void RebuildOutputs()
    {
        // 只显示本工位的两个阀门 DO，保证工位 1/工位 2 的 IO 监控完全独立、互不可见。
        Outputs.Clear();
        Outputs.Add(new IoPointViewModel(_settings.InletOpenCoil, "DO", "进气阀打开线", true)
        {
            CanOperate = IsManualMode
        });
        Outputs.Add(new IoPointViewModel(_settings.InletCloseCoil, "DO", "进气阀关闭线", true)
        {
            CanOperate = IsManualMode
        });
        OnPropertyChanged(nameof(ModeDetailText));
        OnPropertyChanged(nameof(OutputMappingText));
    }

    private void UpdateOutputPermission()
    {
        foreach (var output in Outputs)
        {
            output.CanOperate = IsManualMode;
        }
    }

    private string GetValveActionName(int outputIndex)
    {
        return outputIndex == _settings.InletOpenCoil ? "进气阀打开" : "进气阀关闭";
    }

    private string GetValveActionSecondsText()
    {
        return $"{_settings.ValvePulseMilliseconds / 1000m:0.#}s";
    }

    private void SetValveOutputDisplay(int activeOutputIndex)
    {
        foreach (var output in Outputs)
        {
            output.IsOn = output.Index == activeOutputIndex;
        }
    }

    private void ClearOutputDisplay()
    {
        foreach (var output in Outputs)
        {
            output.IsOn = false;
        }
    }

    private void OnHardwareSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 切换工位时重建阀门 DO 列表（只显示新工位的两个 DO）并重新读取状态。
        RebuildOutputs();
        _ = RefreshAsync();
    }

    public void Dispose()
    {
        _settings.PropertyChanged -= OnHardwareSettingsChanged;
    }
}
