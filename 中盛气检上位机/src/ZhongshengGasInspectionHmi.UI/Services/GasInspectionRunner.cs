using ZhongshengGasInspectionHmi.UI.Models;

namespace ZhongshengGasInspectionHmi.UI.Services;

public sealed class GasInspectionRunner
{
    private static readonly TimeSpan SampleStepDisplayDuration = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RecordedPressureSampleInterval = TimeSpan.FromMilliseconds(100);
    private const int RecordedPressureSampleCount = 5;
    private readonly GasInspectionRecipe _recipe;
    private readonly HardwareSettings _settings;
    private readonly IInspectionHardware _hardware;

    public GasInspectionRunner(
        GasInspectionRecipe recipe,
        HardwareSettings settings,
        IInspectionHardware hardware)
    {
        _recipe = recipe;
        _settings = settings;
        _hardware = hardware;
    }

    public event EventHandler<InspectionSnapshot>? SnapshotChanged;

    public event EventHandler<InspectionRecord>? InspectionCompleted;

    public async Task StartAsync(string? productCode, CancellationToken cancellationToken)
    {
        var validation = _recipe.Validate();
        if (!string.IsNullOrEmpty(validation))
        {
            throw new InvalidOperationException(validation);
        }

        if (!_hardware.IsConnected)
        {
            await _hardware.ConnectAsync(cancellationToken);
        }

        decimal? p1 = null;
        decimal? p2 = null;
        var startedAt = DateTimeOffset.Now;

        try
        {
            await RunTimedStageAsync(
                "充气中",
                "充气计时",
                0,
                TimeSpan.FromSeconds(_recipe.FillSeconds),
                null,
                null,
                startedAt,
                _hardware.OpenInletValveAsync,
                cancellationToken);
            await RunTimedStageAsync(
                "稳压中",
                "稳压计时",
                1,
                TimeSpan.FromSeconds(_recipe.StabilizeSeconds),
                null,
                null,
                startedAt,
                _hardware.CloseInletValveAsync,
                cancellationToken);

            var sample1 = await ReadAveragedPressureAsync(cancellationToken);
            p1 = sample1.PressureMpa;
            await RunSampleStepAsync(
                "采集 P1",
                $"已采集保压前压力 P1={PressureFormatter.FormatKilopascalWithUnit(p1.Value)}（{RecordedPressureSampleCount}次平均）。",
                2,
                sample1,
                p1,
                null,
                startedAt,
                cancellationToken);

            Publish(new InspectionSnapshot
            {
                StageText = "保压中",
                OperatorMessage = "保压中：进气阀保持关闭，排气由现场手动处理。",
                CurrentPressure = sample1.PressureMpa,
                CurrentMilliamp = sample1.CurrentMilliamp,
                P1 = p1,
                ActiveStepIndex = 3,
                TotalElapsed = DateTimeOffset.Now - startedAt
            });
            await RunTimedStageAsync("保压中", "保压计时", 3, TimeSpan.FromSeconds(_recipe.HoldSeconds), p1, null, startedAt, null, cancellationToken);

            var sample2 = await ReadAveragedPressureAsync(cancellationToken);
            p2 = sample2.PressureMpa;
            await RunSampleStepAsync(
                "采集 P2",
                $"已采集保压后压力 P2={PressureFormatter.FormatKilopascalWithUnit(p2.Value)}（{RecordedPressureSampleCount}次平均）。",
                4,
                sample2,
                p1,
                p2,
                startedAt,
                cancellationToken);

            var leakRate = CalculateLeakRate(p1.Value, p2.Value);
            var result = leakRate > _recipe.MaxLeakRate ? "NG" : "OK";
            var record = new InspectionRecord(
                Guid.NewGuid(),
                startedAt,
                DateTimeOffset.Now,
                _settings.ActiveStationId,
                _settings.StationName,
                string.IsNullOrWhiteSpace(productCode) ? null : productCode.Trim(),
                p1.Value,
                p2.Value,
                leakRate,
                result,
                _recipe.MaxLeakRate,
                _recipe.FillSeconds,
                _recipe.StabilizeSeconds,
                _recipe.HoldSeconds);
            Publish(new InspectionSnapshot
            {
                StageText = "完成",
                OperatorMessage = BuildCompletionMessage(result),
                CurrentPressure = sample2.PressureMpa,
                CurrentMilliamp = sample2.CurrentMilliamp,
                P1 = p1,
                P2 = p2,
                LeakRate = leakRate,
                ResultText = result,
                ActiveStepIndex = 4,
                CompleteAllSteps = true,
                TotalElapsed = DateTimeOffset.Now - startedAt
            });
            InspectionCompleted?.Invoke(this, record);

            await _hardware.CloseValvesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            CloseValvesInBackground();
            Publish(new InspectionSnapshot
            {
                StageText = "已停止",
                OperatorMessage = "流程已停止，进气阀已关闭；请现场手动排气。",
                P1 = p1,
                P2 = p2,
                ResultText = "STOP",
                TotalElapsed = DateTimeOffset.Now - startedAt
            });
        }
        catch
        {
            await _hardware.CloseValvesAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task ExhaustAsync(CancellationToken cancellationToken)
    {
        if (!_hardware.IsConnected)
        {
            await _hardware.ConnectAsync(cancellationToken);
        }

        await _hardware.CloseInletValveAsync(cancellationToken);
        var sample = await ReadProtectedPressureAsync(cancellationToken);
        Publish(new InspectionSnapshot
        {
            StageText = "手动排气",
            OperatorMessage = "进气阀已关闭，请现场手动排气。",
            CurrentPressure = sample.PressureMpa,
            CurrentMilliamp = sample.CurrentMilliamp,
            ResultText = "--"
        });
    }

    private async Task RunTimedStageAsync(
        string stageText,
        string messagePrefix,
        int activeStep,
        TimeSpan duration,
        decimal? p1,
        decimal? p2,
        DateTimeOffset runStartedAt,
        Func<CancellationToken, Task>? stageAction,
        CancellationToken cancellationToken)
    {
        var actionTask = stageAction?.Invoke(cancellationToken);
        var startedAt = DateTimeOffset.Now;
        PressureSample? lastSample = null;
        try
        {
            while (DateTimeOffset.Now - startedAt < duration)
            {
                var sample = await ReadProtectedPressureAsync(cancellationToken);
                lastSample = sample;
                var now = DateTimeOffset.Now;
                var elapsed = now - startedAt;
                var remaining = duration - elapsed;
                Publish(new InspectionSnapshot
                {
                    StageText = stageText,
                    OperatorMessage = $"{messagePrefix}：剩余 {Math.Max(0, remaining.TotalSeconds):0.0}s。",
                    CurrentPressure = sample.PressureMpa,
                    CurrentMilliamp = sample.CurrentMilliamp,
                    P1 = p1,
                    P2 = p2,
                    ActiveStepIndex = activeStep,
                    StageElapsed = elapsed,
                    StageRemaining = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero,
                    StageDuration = duration,
                    TotalElapsed = now - runStartedAt
                });

                if (actionTask?.IsFaulted == true)
                {
                    await actionTask;
                }

                await Task.Delay(200, cancellationToken);
            }

            lastSample ??= await ReadProtectedPressureAsync(cancellationToken);
            Publish(new InspectionSnapshot
            {
                StageText = stageText,
                OperatorMessage = $"{messagePrefix}：剩余 0.0s。",
                CurrentPressure = lastSample.PressureMpa,
                CurrentMilliamp = lastSample.CurrentMilliamp,
                P1 = p1,
                P2 = p2,
                ActiveStepIndex = activeStep,
                StageElapsed = duration,
                StageRemaining = TimeSpan.Zero,
                StageDuration = duration,
                TotalElapsed = DateTimeOffset.Now - runStartedAt
            });

            if (actionTask is not null)
            {
                if (actionTask.IsCompleted)
                {
                    await actionTask;
                }
                else
                {
                    ObserveBackgroundAction(actionTask);
                }
            }
        }
        catch
        {
            if (actionTask is not null && !actionTask.IsCompleted)
            {
                try
                {
                    await actionTask;
                }
                catch
                {
                    // Preserve the original failure/cancellation while still observing the hardware action.
                }
            }

            throw;
        }
    }

    private async Task RunSampleStepAsync(
        string stageText,
        string operatorMessage,
        int activeStep,
        PressureSample sample,
        decimal? p1,
        decimal? p2,
        DateTimeOffset runStartedAt,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;
        while (DateTimeOffset.Now - startedAt < SampleStepDisplayDuration)
        {
            var now = DateTimeOffset.Now;
            var elapsed = now - startedAt;
            var remaining = SampleStepDisplayDuration - elapsed;
            Publish(new InspectionSnapshot
            {
                StageText = stageText,
                OperatorMessage = operatorMessage,
                CurrentPressure = sample.PressureMpa,
                CurrentMilliamp = sample.CurrentMilliamp,
                P1 = p1,
                P2 = p2,
                ActiveStepIndex = activeStep,
                StageElapsed = elapsed,
                StageRemaining = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero,
                StageDuration = SampleStepDisplayDuration,
                TotalElapsed = now - runStartedAt
            });

            await Task.Delay(200, cancellationToken);
        }

        Publish(new InspectionSnapshot
        {
            StageText = stageText,
            OperatorMessage = operatorMessage,
            CurrentPressure = sample.PressureMpa,
            CurrentMilliamp = sample.CurrentMilliamp,
            P1 = p1,
            P2 = p2,
            ActiveStepIndex = activeStep,
            StageElapsed = SampleStepDisplayDuration,
            StageRemaining = TimeSpan.Zero,
            StageDuration = SampleStepDisplayDuration,
            TotalElapsed = DateTimeOffset.Now - runStartedAt
        });
    }

    private static void ObserveBackgroundAction(Task actionTask)
    {
        _ = ObserveBackgroundActionAsync(actionTask);
    }

    private void CloseValvesInBackground()
    {
        ObserveBackgroundAction(_hardware.CloseValvesAsync(CancellationToken.None));
    }

    private static async Task ObserveBackgroundActionAsync(Task actionTask)
    {
        try
        {
            await actionTask;
        }
        catch
        {
        }
    }

    private void Publish(InspectionSnapshot snapshot)
    {
        SnapshotChanged?.Invoke(this, snapshot);
    }

    private string BuildCompletionMessage(string result)
    {
        var message = result == "OK"
            ? "判定完成：实际漏率小于等于允许漏率。"
            : "判定完成：实际漏率大于允许漏率。";
        return $"{message} 请现场手动排气。";
    }

    private async Task<PressureSample> ReadProtectedPressureAsync(CancellationToken cancellationToken)
    {
        return await _hardware.ReadPressureAsync(cancellationToken);
    }

    private async Task<PressureSample> ReadAveragedPressureAsync(CancellationToken cancellationToken)
    {
        decimal pressureSum = 0m;
        decimal currentSum = 0m;
        decimal voltageSum = 0m;
        ushort rawRegister = 0;

        for (var index = 0; index < RecordedPressureSampleCount; index++)
        {
            var sample = await ReadProtectedPressureAsync(cancellationToken);
            pressureSum += sample.PressureMpa;
            currentSum += sample.CurrentMilliamp;
            voltageSum += sample.VoltageVolt;
            rawRegister = sample.RawRegister;

            if (index < RecordedPressureSampleCount - 1)
            {
                await Task.Delay(RecordedPressureSampleInterval, cancellationToken);
            }
        }

        return new PressureSample(
            decimal.Round(pressureSum / RecordedPressureSampleCount, 6, MidpointRounding.AwayFromZero),
            decimal.Round(currentSum / RecordedPressureSampleCount, 4, MidpointRounding.AwayFromZero),
            rawRegister,
            decimal.Round(voltageSum / RecordedPressureSampleCount, 4, MidpointRounding.AwayFromZero));
    }

    private static decimal CalculateLeakRate(decimal p1, decimal p2)
    {
        if (p1 == 0)
        {
            return 0m;
        }

        return decimal.Round((p1 - p2) / p1, 6, MidpointRounding.AwayFromZero);
    }
}
