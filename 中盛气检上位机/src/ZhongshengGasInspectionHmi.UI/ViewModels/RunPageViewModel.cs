using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZhongshengGasInspectionHmi.UI.Models;
using ZhongshengGasInspectionHmi.UI.Services;

namespace ZhongshengGasInspectionHmi.UI.ViewModels;

public sealed partial class RunPageViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan SampleStepDisplayDuration = TimeSpan.FromSeconds(1);
    private const int LivePressureSmoothingSampleCount = 5;
    private readonly GasInspectionRecipe _recipe;
    private readonly HardwareSettings _settings;
    private readonly IInspectionHardware _hardware;
    private readonly InspectionRecordStore _recordStore;
    private readonly GasInspectionRunner _runner;
    private readonly DispatcherTimer _idlePressureTimer;
    private readonly Queue<decimal> _livePressureWindow = new();
    private CancellationTokenSource? _runCancellation;
    private bool _ngWarningQueued;
    private bool _isIdlePressureReadInProgress;

    public RunPageViewModel(
        GasInspectionRecipe recipe,
        HardwareSettings settings,
        IInspectionHardware hardware,
        InspectionRecordStore recordStore,
        GasInspectionRunner runner)
    {
        _recipe = recipe;
        _settings = settings;
        _hardware = hardware;
        _recordStore = recordStore;
        _runner = runner;
        _runner.SnapshotChanged += OnSnapshotChanged;
        _runner.InspectionCompleted += OnInspectionCompleted;
        _recipe.PropertyChanged += OnRecipeChanged;
        _settings.PropertyChanged += OnHardwareSettingsChanged;
        _idlePressureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _idlePressureTimer.Tick += OnIdlePressureTimerTick;
        _idlePressureTimer.Start();
        Steps =
        [
            new ProcessStepViewModel(1, "充气", "进气阀打开，按时充气"),
            new ProcessStepViewModel(2, "稳压", "进气阀关闭，等待稳定"),
            new ProcessStepViewModel(3, "记录P1", "保存保压前压力"),
            new ProcessStepViewModel(4, "保压", "计时监控压力变化"),
            new ProcessStepViewModel(5, "记录P2", "计算漏率，输出判定")
        ];
        ResetStepTimes();
    }

    public ObservableCollection<ProcessStepViewModel> Steps { get; }

    [ObservableProperty]
    private string _productCode = string.Empty;

    [ObservableProperty]
    private string _hardwareStatus = "真实硬件未连接";

    [ObservableProperty]
    private string _operatorMessage = "确认参数和硬件连接后，点击启动开始检测。";

    [ObservableProperty]
    private string _stageText = "待机";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PressureValueText))]
    private decimal _currentPressure;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentMilliampText))]
    private decimal _currentMilliamp = 4.000m;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(P1Text))]
    private decimal? _p1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(P2Text))]
    private decimal? _p2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeakRateText))]
    private decimal? _leakRate;

    [ObservableProperty]
    private string _resultText = "--";

    [ObservableProperty]
    private string _stageRemainingText = "--";

    [ObservableProperty]
    private string _stageTimeText = "-- / --";

    [ObservableProperty]
    private string _totalRunTimeText = "00:00";

    [ObservableProperty]
    private double _stageProgressPercent;

    [ObservableProperty]
    private bool _isNgWarningVisible;

    [ObservableProperty]
    private string _ngWarningMessage = "本台检测结果 NG，请确认后处理。";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExhaustCommand))]
    private bool _isBusy;

    public string PressureValueText => PressureFormatter.FormatKilopascal(CurrentPressure);

    public string CurrentMilliampText => $"{CurrentMilliamp:0.0000} mA";

    public string P1Text => P1 is null ? "--" : PressureFormatter.FormatKilopascal(P1.Value);

    public string P2Text => P2 is null ? "--" : PressureFormatter.FormatKilopascal(P2.Value);

    public string LeakRateText => LeakRate is null ? "--" : LeakRateFormatter.FormatRatio(LeakRate.Value);

    public string FillTimeText => $"{_recipe.FillSeconds:0.#} s";

    public string StabilizeTimeText => $"{_recipe.StabilizeSeconds:0.#} s";

    public string HoldTimeText => $"{_recipe.HoldSeconds:0.#} s";

    public string LeakLimitText => LeakRateFormatter.FormatRatio(_recipe.MaxLeakRate);

    public string StationText => _settings.StationText;

    public string StationMappingText => _settings.StationMappingText;

    public string PressurePointText => $"AI{_settings.PressureRegister + 1} / {30001 + _settings.PressureRegister} · 4~20mA压力表 · 0~1MPa量程";

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        _runCancellation?.Dispose();
        _runCancellation = new CancellationTokenSource();
        try
        {
            IsBusy = true;
            HardwareStatus = _hardware.StatusText;
            ResetRunValues();
            await _runner.StartAsync(ProductCode, _runCancellation.Token);
            HardwareStatus = _hardware.StatusText;
        }
        catch (OperationCanceledException)
        {
            OperatorMessage = "流程已停止。";
        }
        catch (Exception ex)
        {
            StageText = "故障";
            ResultText = "ERR";
            OperatorMessage = ex.Message;
            HardwareStatus = _hardware.StatusText;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private Task StopAsync()
    {
        _runCancellation?.Cancel();

        OperatorMessage = "停止请求已发送，流程正在终止。";
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanExhaust))]
    private async Task ExhaustAsync()
    {
        try
        {
            IsBusy = true;
            await _runner.ExhaustAsync(CancellationToken.None);
            HardwareStatus = _hardware.StatusText;
        }
        catch (Exception ex)
        {
            StageText = "排气故障";
            OperatorMessage = ex.Message;
            ResultText = "ERR";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanStart() => !IsBusy;

    private bool CanStop() => IsBusy;

    private bool CanExhaust() => !IsBusy;

    private void OnSnapshotChanged(object? sender, InspectionSnapshot snapshot)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplySnapshot(snapshot);
            return;
        }

        _ = dispatcher.InvokeAsync(() => ApplySnapshot(snapshot));
    }

    private async void OnInspectionCompleted(object? sender, InspectionRecord record)
    {
        try
        {
            await _recordStore.AddAsync(record, CancellationToken.None);
        }
        catch (Exception ex)
        {
            OperatorMessage = $"检测完成，但记录保存失败：{ex.Message}";
        }
    }

    private void ApplySnapshot(InspectionSnapshot snapshot)
    {
        StageText = snapshot.StageText;
        OperatorMessage = snapshot.OperatorMessage;
        if (snapshot.CurrentPressure is not null)
        {
            ApplyLivePressureSample(snapshot.CurrentPressure.Value);
        }

        if (snapshot.CurrentMilliamp is not null)
        {
            CurrentMilliamp = snapshot.CurrentMilliamp.Value;
        }
        P1 = snapshot.P1;
        P2 = snapshot.P2;
        LeakRate = snapshot.LeakRate;
        ResultText = snapshot.ResultText;
        ApplyTiming(snapshot);
        UpdateSteps(snapshot.ActiveStepIndex, snapshot.CompleteAllSteps);
        UpdateStepTiming(snapshot);
        ShowNgWarningIfNeeded(snapshot);
    }

    private void ResetRunValues()
    {
        _ngWarningQueued = false;
        IsNgWarningVisible = false;
        NgWarningMessage = "本台检测结果 NG，请确认后处理。";
        P1 = null;
        P2 = null;
        LeakRate = null;
        ResultText = "--";
        StageRemainingText = "--";
        StageTimeText = "-- / --";
        TotalRunTimeText = "00:00";
        StageProgressPercent = 0;
        ResetStepTimes();
        UpdateSteps(-1, false);
    }

    [RelayCommand]
    private void ConfirmNgWarning()
    {
        IsNgWarningVisible = false;
    }

    private void ShowNgWarningIfNeeded(InspectionSnapshot snapshot)
    {
        if (_ngWarningQueued || snapshot.ResultText != "NG" || !snapshot.CompleteAllSteps)
        {
            return;
        }

        _ngWarningQueued = true;
        var productCode = string.IsNullOrWhiteSpace(ProductCode) ? "未录入" : ProductCode.Trim();
        var actualLeakRate = snapshot.LeakRate is null ? "--" : LeakRateFormatter.FormatRatio(snapshot.LeakRate.Value);
        NgWarningMessage = $"产品条码：{productCode}\n实际漏率：{actualLeakRate}\n允许漏率：{LeakLimitText}\n请确认该产品按 NG 处理后再关闭提示。";
        IsNgWarningVisible = true;
    }

    private void UpdateSteps(int activeIndex, bool completeAll)
    {
        for (var index = 0; index < Steps.Count; index++)
        {
            Steps[index].Status = completeAll
                ? "完成"
                : index == activeIndex
                    ? "进行中"
                    : index < activeIndex
                        ? "完成"
                        : "等待";
        }
    }

    private void OnRecipeChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(FillTimeText));
        OnPropertyChanged(nameof(StabilizeTimeText));
        OnPropertyChanged(nameof(HoldTimeText));
        OnPropertyChanged(nameof(LeakLimitText));
        if (!IsBusy)
        {
            ResetStepTimes();
        }
    }

    private void OnHardwareSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(StationText));
        OnPropertyChanged(nameof(StationMappingText));
        OnPropertyChanged(nameof(PressurePointText));
    }

    private void ApplyTiming(InspectionSnapshot snapshot)
    {
        if (snapshot.TotalElapsed is not null)
        {
            TotalRunTimeText = FormatDuration(snapshot.TotalElapsed.Value);
        }

        if (snapshot.StageDuration is { TotalMilliseconds: > 0 } duration)
        {
            var elapsed = ClampTimeSpan(snapshot.StageElapsed ?? TimeSpan.Zero, duration);
            var remaining = ClampTimeSpan(snapshot.StageRemaining ?? duration - elapsed, duration);
            StageRemainingText = FormatDuration(remaining);
            StageTimeText = $"{FormatDuration(elapsed)} / {FormatDuration(duration)}";
            StageProgressPercent = Math.Clamp(elapsed.TotalMilliseconds / duration.TotalMilliseconds * 100d, 0d, 100d);
            return;
        }

        if (snapshot.CompleteAllSteps)
        {
            StageRemainingText = "00:00";
            StageTimeText = "完成";
            StageProgressPercent = 100;
        }
    }

    private static TimeSpan ClampTimeSpan(TimeSpan value, TimeSpan max)
    {
        if (value < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return value > max ? max : value;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var safeDuration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        return safeDuration.TotalHours >= 1
            ? $"{(int)safeDuration.TotalHours:00}:{safeDuration.Minutes:00}:{safeDuration.Seconds:00}"
            : $"{safeDuration.Minutes:00}:{safeDuration.Seconds:00}";
    }

    private void ResetStepTimes()
    {
        SetStepTime(0, TimeSpan.Zero, TimeSpan.FromSeconds(_recipe.FillSeconds));
        SetStepTime(1, TimeSpan.Zero, TimeSpan.FromSeconds(_recipe.StabilizeSeconds));
        SetStepTime(2, TimeSpan.Zero, SampleStepDisplayDuration);
        SetStepTime(3, TimeSpan.Zero, TimeSpan.FromSeconds(_recipe.HoldSeconds));
        SetStepTime(4, TimeSpan.Zero, SampleStepDisplayDuration);
    }

    private void UpdateStepTiming(InspectionSnapshot snapshot)
    {
        if (snapshot.ActiveStepIndex < 0 || snapshot.ActiveStepIndex >= Steps.Count)
        {
            return;
        }

        if (snapshot.StageDuration is not { TotalMilliseconds: > 0 } duration)
        {
            return;
        }

        var elapsed = ClampTimeSpan(snapshot.StageElapsed ?? TimeSpan.Zero, duration);
        SetStepTime(snapshot.ActiveStepIndex, elapsed, duration);
    }

    private void SetStepTime(int index, TimeSpan elapsed, TimeSpan duration)
    {
        if (index < 0 || index >= Steps.Count)
        {
            return;
        }

        Steps[index].TimeText = $"{FormatDuration(elapsed)} / {FormatDuration(duration)}";
    }

    private void OnIdlePressureTimerTick(object? sender, EventArgs e)
    {
        if (IsBusy || !_hardware.IsConnected || _isIdlePressureReadInProgress)
        {
            return;
        }

        _ = RefreshIdlePressureAsync();
    }

    private async Task RefreshIdlePressureAsync()
    {
        try
        {
            _isIdlePressureReadInProgress = true;
            var sample = await _hardware.ReadPressureAsync(CancellationToken.None);
            ApplyLivePressureSample(sample.PressureMpa);
            CurrentMilliamp = sample.CurrentMilliamp;
            HardwareStatus = _hardware.StatusText;
        }
        catch
        {
            HardwareStatus = "压力自动刷新失败，请检查模块连接。";
        }
        finally
        {
            _isIdlePressureReadInProgress = false;
        }
    }

    private void ApplyLivePressureSample(decimal pressureMpa)
    {
        _livePressureWindow.Enqueue(pressureMpa);
        while (_livePressureWindow.Count > LivePressureSmoothingSampleCount)
        {
            _livePressureWindow.Dequeue();
        }

        var sum = 0m;
        foreach (var value in _livePressureWindow)
        {
            sum += value;
        }

        CurrentPressure = decimal.Round(sum / _livePressureWindow.Count, 6, MidpointRounding.AwayFromZero);
    }

    public void Dispose()
    {
        _idlePressureTimer.Stop();
        _idlePressureTimer.Tick -= OnIdlePressureTimerTick;
        _runner.SnapshotChanged -= OnSnapshotChanged;
        _runner.InspectionCompleted -= OnInspectionCompleted;
        _recipe.PropertyChanged -= OnRecipeChanged;
        _settings.PropertyChanged -= OnHardwareSettingsChanged;
        _runCancellation?.Dispose();
    }
}
