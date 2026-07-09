using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZhongshengGasInspectionHmi.UI.Models;
using ZhongshengGasInspectionHmi.UI.Services;

namespace ZhongshengGasInspectionHmi.UI.ViewModels;

public sealed partial class HardwarePageViewModel : ObservableObject, IDisposable
{
    private readonly HardwareSettings _settings;
    private readonly IInspectionHardware _hardware;
    private readonly GasInspectionRecipe _recipe;
    private readonly IAppConfigurationStore _configurationStore;
    private readonly DispatcherTimer _autoRefreshTimer;
    private bool _isPressureReadInProgress;
    private bool _isRefreshingFromSettings;

    public HardwarePageViewModel(
        HardwareSettings settings,
        IInspectionHardware hardware,
        GasInspectionRecipe recipe,
        IAppConfigurationStore configurationStore)
    {
        _settings = settings;
        _hardware = hardware;
        _recipe = recipe;
        _configurationStore = configurationStore;
        _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _autoRefreshTimer.Tick += OnAutoRefreshTimerTick;
        _configurationStore.ActiveStationChanged += OnActiveStationChanged;
        RefreshFromSettings();
        StatusText = hardware.StatusText;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StationMappingText))]
    private int _activeStationId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StationMappingText))]
    private string _stationName = string.Empty;

    [ObservableProperty]
    private string _ioModuleIp = string.Empty;

    [ObservableProperty]
    private int _ioModulePort;

    [ObservableProperty]
    private string _analogModuleIp = string.Empty;

    [ObservableProperty]
    private int _analogModulePort;

    [ObservableProperty]
    private byte _modbusUnitId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StationMappingText))]
    private int _inletOpenCoil;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StationMappingText))]
    private int _inletCloseCoil;

    [ObservableProperty]
    private int _valvePulseMilliseconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StationMappingText))]
    private int _pressureRegister;

    [ObservableProperty]
    private int _analogFixedDecimalPlaces;

    [ObservableProperty]
    private bool _useRtuOverTcp;

    [ObservableProperty]
    private string _protocolText = "标准 Modbus TCP";

    [ObservableProperty]
    private string _statusText = "未连接";

    [ObservableProperty]
    private string _lastRawRegisterText = "--";

    [ObservableProperty]
    private string _lastVoltageText = "--";

    [ObservableProperty]
    private string _lastCurrentMilliampText = "--";

    [ObservableProperty]
    private string _lastPressureText = "--";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoRefreshText))]
    private bool _isAutoRefreshPressure;

    public string AutoRefreshText => IsAutoRefreshPressure ? "自动刷新中：1s" : "自动刷新关闭";

    public string StationMappingText =>
        $"当前上位机控制：{StationName}，AI{PressureRegister + 1} / DO{InletCloseCoil + 1}关阀 / DO{InletOpenCoil + 1}开阀";

    [RelayCommand]
    private void Save()
    {
        SaveToSettings();
        var validation = _settings.Validate();
        if (!string.IsNullOrEmpty(validation))
        {
            StatusText = validation;
            return;
        }

        try
        {
            _configurationStore.Save(_recipe, _settings);
            StatusText = $"{_settings.StationName} 硬件配置已保存。配置文件：{_configurationStore.ConfigurationPath}";
            ProtocolText = _settings.ProtocolText;
        }
        catch (Exception ex)
        {
            StatusText = $"保存硬件配置失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        SaveToSettings();
        var validation = _settings.Validate();
        if (!string.IsNullOrEmpty(validation))
        {
            StatusText = validation;
            return;
        }

        try
        {
            _configurationStore.Save(_recipe, _settings);
            await _hardware.ConnectAsync(CancellationToken.None);
            StatusText = _hardware.StatusText;
            await ReadPressureCoreAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            StatusText = "连接已取消，请确认网线、模块电源和 IP 地址后重试。";
        }
        catch (Exception ex)
        {
            StatusText = FormatHardwareError("连接失败", ex);
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        IsAutoRefreshPressure = false;
        await _hardware.DisconnectAsync(CancellationToken.None);
        StatusText = _hardware.StatusText;
    }

    [RelayCommand]
    private async Task ReadPressureAsync()
    {
        SaveToSettings();
        var validation = _settings.Validate();
        if (!string.IsNullOrEmpty(validation))
        {
            StatusText = validation;
            return;
        }

        try
        {
            await ReadPressureCoreAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            StatusText = "读取已取消，请确认模块连接状态后重试。";
        }
        catch (Exception ex)
        {
            StatusText = FormatHardwareError("读取压力失败", ex);
        }
    }

    partial void OnIsAutoRefreshPressureChanged(bool value)
    {
        if (value)
        {
            _autoRefreshTimer.Start();
            _ = ReadPressureForAutoRefreshAsync();
        }
        else
        {
            _autoRefreshTimer.Stop();
        }
    }

    private void SaveToSettings()
    {
        _settings.ActiveStationId = ActiveStationId;
        _settings.StationName = string.IsNullOrWhiteSpace(StationName) ? $"工位{ActiveStationId}" : StationName.Trim();
        _settings.IoModuleIp = IoModuleIp;
        _settings.IoModulePort = IoModulePort;
        _settings.AnalogModuleIp = AnalogModuleIp;
        _settings.AnalogModulePort = AnalogModulePort;
        _settings.ModbusUnitId = ModbusUnitId;
        _settings.InletOpenCoil = InletOpenCoil;
        _settings.InletCloseCoil = InletCloseCoil;
        _settings.ValvePulseMilliseconds = ValvePulseMilliseconds;
        _settings.PressureRegister = PressureRegister;
        _settings.AnalogFixedDecimalPlaces = AnalogFixedDecimalPlaces;
        _settings.UseRtuOverTcp = UseRtuOverTcp;
        ProtocolText = _settings.ProtocolText;
        OnPropertyChanged(nameof(StationMappingText));
    }

    partial void OnActiveStationIdChanged(int value)
    {
        if (_isRefreshingFromSettings || value <= 0)
        {
            return;
        }

        _configurationStore.ApplyStation(value, _recipe, _settings);
        RefreshFromSettings();
        StatusText = $"已切换到{_settings.StationName}。请确认 AI/DO 点位后保存或连接。";
    }

    private async Task ReadPressureCoreAsync(CancellationToken cancellationToken)
    {
        if (_isPressureReadInProgress)
        {
            return;
        }

        try
        {
            _isPressureReadInProgress = true;
            var sample = await _hardware.ReadPressureAsync(cancellationToken);
            LastRawRegisterText = $"0x{sample.RawRegister:X4} / {sample.RawRegister}";
            LastVoltageText = $"{sample.VoltageVolt:0.0000} V";
            LastCurrentMilliampText = $"{sample.CurrentMilliamp:0.0000} mA";
            LastPressureText = $"{PressureFormatter.FormatKilopascalWithUnit(sample.PressureMpa)} ({sample.PressureMpa:0.000000} MPa)";
            StatusText = $"压力读取成功：{_settings.StationName}，{_settings.ProtocolText}，AI{PressureRegister + 1} 协议地址 {PressureRegister:0000}H。";
        }
        finally
        {
            _isPressureReadInProgress = false;
        }
    }

    private void OnAutoRefreshTimerTick(object? sender, EventArgs e)
    {
        _ = ReadPressureForAutoRefreshAsync();
    }

    private async Task ReadPressureForAutoRefreshAsync()
    {
        try
        {
            await ReadPressureCoreAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            IsAutoRefreshPressure = false;
            StatusText = FormatHardwareError("自动刷新已停止", ex);
        }
    }

    private static string FormatHardwareError(string prefix, Exception ex)
    {
        if (ex is OperationCanceledException || ex.Message.Contains("operation was canceled", StringComparison.OrdinalIgnoreCase))
        {
            return $"{prefix}：操作已取消，请确认网线、模块电源和 IP 地址后重试。";
        }

        return $"{prefix}：{ex.Message}";
    }

    public void Dispose()
    {
        _autoRefreshTimer.Stop();
        _autoRefreshTimer.Tick -= OnAutoRefreshTimerTick;
        _configurationStore.ActiveStationChanged -= OnActiveStationChanged;
    }

    private void OnActiveStationChanged(object? sender, EventArgs e)
    {
        RefreshFromSettings();
    }

    private void RefreshFromSettings()
    {
        _isRefreshingFromSettings = true;
        try
        {
            ActiveStationId = _settings.ActiveStationId;
            StationName = _settings.StationName;
            IoModuleIp = _settings.IoModuleIp;
            IoModulePort = _settings.IoModulePort;
            AnalogModuleIp = _settings.AnalogModuleIp;
            AnalogModulePort = _settings.AnalogModulePort;
            ModbusUnitId = _settings.ModbusUnitId;
            InletOpenCoil = _settings.InletOpenCoil;
            InletCloseCoil = _settings.InletCloseCoil;
            ValvePulseMilliseconds = _settings.ValvePulseMilliseconds;
            PressureRegister = _settings.PressureRegister;
            AnalogFixedDecimalPlaces = _settings.AnalogFixedDecimalPlaces;
            UseRtuOverTcp = _settings.UseRtuOverTcp;
            ProtocolText = _settings.ProtocolText;
            OnPropertyChanged(nameof(StationMappingText));
        }
        finally
        {
            _isRefreshingFromSettings = false;
        }
    }
}
