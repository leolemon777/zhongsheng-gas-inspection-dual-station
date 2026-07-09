using ZhongshengGasInspectionHmi.UI.Models;

namespace ZhongshengGasInspectionHmi.UI.Services;

public sealed class ZhongshengInspectionHardware : IInspectionHardware, IIoMonitorHardware
{
    private const int DigitalPointCount = 4;
    private readonly HardwareSettings _settings;
    private readonly GasInspectionRecipe _recipe;
    private readonly ZhongshengModbusTcpClient _modbus;
    private readonly SemaphoreSlim _pressureReadLock = new(1, 1);
    private readonly SemaphoreSlim _valveActionLock = new(1, 1);
    private CancellationTokenSource? _activeValveActionCancellation;
    private Task? _activeValveActionTask;

    public ZhongshengInspectionHardware(
        HardwareSettings settings,
        GasInspectionRecipe recipe,
        ZhongshengModbusTcpClient modbus)
    {
        _settings = settings;
        _recipe = recipe;
        _modbus = modbus;
    }

    public bool IsConnected { get; private set; }

    public string StatusText { get; private set; } = "真实硬件未连接";

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var error = _settings.Validate();
        if (!string.IsNullOrEmpty(error))
        {
            throw new InvalidOperationException(error);
        }

        _ = await ReadPressureAsync(cancellationToken);
        IsConnected = true;
        StatusText = $"真实硬件已连接（{_settings.ProtocolText}）";
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsConnected = false;
        StatusText = "真实硬件已断开";
        return Task.CompletedTask;
    }

    public async Task<PressureSample> ReadPressureAsync(CancellationToken cancellationToken)
    {
        await _pressureReadLock.WaitAsync(cancellationToken);
        try
        {
            var registers = await _modbus.ReadInputRegistersAsync(
                _settings.AnalogModuleIp,
                _settings.AnalogModulePort,
                _settings.ModbusUnitId,
                (ushort)_settings.PressureRegister,
                1,
                _settings.UseRtuOverTcp,
                cancellationToken);
            var raw = registers[0];
            var voltage = DecodeAnalogVoltage(raw);
            var currentMilliamp = voltage / 249m * 1000m;
            var pressure = ConvertCurrentToPressure(currentMilliamp);
            return new PressureSample(
                decimal.Round(pressure, 6, MidpointRounding.AwayFromZero),
                decimal.Round(currentMilliamp, 4, MidpointRounding.AwayFromZero),
                raw,
                decimal.Round(voltage, 4, MidpointRounding.AwayFromZero));
        }
        finally
        {
            _pressureReadLock.Release();
        }
    }

    public Task OpenInletValveAsync(CancellationToken cancellationToken)
    {
        return PulseInletValveAsync(_settings.InletOpenCoil, _settings.InletCloseCoil, cancellationToken);
    }

    public Task CloseInletValveAsync(CancellationToken cancellationToken)
    {
        return PulseInletValveAsync(_settings.InletCloseCoil, _settings.InletOpenCoil, cancellationToken);
    }

    public async Task<IReadOnlyList<bool>> ReadDigitalInputsAsync(CancellationToken cancellationToken)
    {
        var values = await _modbus.ReadDiscreteInputsAsync(
            _settings.IoModuleIp,
            _settings.IoModulePort,
            _settings.ModbusUnitId,
            0,
            DigitalPointCount,
            _settings.UseRtuOverTcp,
            cancellationToken);
        return values;
    }

    public async Task<IReadOnlyList<bool>> ReadDigitalOutputsAsync(CancellationToken cancellationToken)
    {
        var values = await _modbus.ReadCoilsAsync(
            _settings.IoModuleIp,
            _settings.IoModulePort,
            _settings.ModbusUnitId,
            0,
            DigitalPointCount,
            _settings.UseRtuOverTcp,
            cancellationToken);
        return values;
    }

    public Task SetDigitalOutputAsync(int outputIndex, bool isOn, CancellationToken cancellationToken)
    {
        if (outputIndex is < 0 or >= DigitalPointCount)
        {
            throw new ArgumentOutOfRangeException(nameof(outputIndex), "RJ45 4IO 输出协议地址必须在 0~3。");
        }

        return _modbus.WriteSingleCoilAsync(
            _settings.IoModuleIp,
            _settings.IoModulePort,
            _settings.ModbusUnitId,
            (ushort)outputIndex,
            isOn,
            _settings.UseRtuOverTcp,
            cancellationToken);
    }

    public async Task ResetKnownOutputsAsync(CancellationToken cancellationToken)
    {
        await CloseInletValveAsync(cancellationToken);
    }

    private async Task PulseInletValveAsync(
        int pulseCoil,
        int interlockCoil,
        CancellationToken cancellationToken)
    {
        await CancelActiveValveActionAsync(cancellationToken);
        await WriteOutputAsync(interlockCoil, false, cancellationToken);
        await StartValveActionAsync(
            pulseCoil,
            TimeSpan.FromMilliseconds(_settings.ValvePulseMilliseconds),
            cancellationToken);
    }

    private async Task CancelActiveValveActionAsync(CancellationToken cancellationToken)
    {
        Task? activeTask;
        CancellationTokenSource? activeCancellation;

        await _valveActionLock.WaitAsync(cancellationToken);
        try
        {
            activeTask = _activeValveActionTask;
            activeCancellation = _activeValveActionCancellation;
            activeCancellation?.Cancel();
        }
        finally
        {
            _valveActionLock.Release();
        }

        if (activeTask is null)
        {
            return;
        }

        try
        {
            await activeTask;
        }
        catch (OperationCanceledException) when (activeCancellation?.IsCancellationRequested == true)
        {
        }
    }

    private async Task StartValveActionAsync(
        int outputIndex,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var actionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task actionTask;
        await _valveActionLock.WaitAsync(cancellationToken);
        try
        {
            _activeValveActionCancellation = actionCancellation;
            actionTask = WriteOutputForDurationAsync(outputIndex, duration, actionCancellation.Token);
            _activeValveActionTask = actionTask;
        }
        finally
        {
            _valveActionLock.Release();
        }

        try
        {
            await actionTask;
        }
        catch (OperationCanceledException) when (actionCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            await _valveActionLock.WaitAsync(CancellationToken.None);
            try
            {
                if (ReferenceEquals(_activeValveActionTask, actionTask))
                {
                    _activeValveActionTask = null;
                    _activeValveActionCancellation = null;
                }
            }
            finally
            {
                _valveActionLock.Release();
                actionCancellation.Dispose();
            }
        }
    }

    private Task WriteOutputAsync(int outputIndex, bool isOn, CancellationToken cancellationToken)
    {
        if (outputIndex is < 0 or >= DigitalPointCount)
        {
            throw new ArgumentOutOfRangeException(nameof(outputIndex), "RJ45 4IO 输出协议地址必须在 0~3。");
        }

        return _modbus.WriteSingleCoilAsync(
            _settings.IoModuleIp,
            _settings.IoModulePort,
            _settings.ModbusUnitId,
            (ushort)outputIndex,
            isOn,
            _settings.UseRtuOverTcp,
            cancellationToken);
    }

    private Task WriteOutputForDurationAsync(
        int outputIndex,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        if (outputIndex is < 0 or >= DigitalPointCount)
        {
            throw new ArgumentOutOfRangeException(nameof(outputIndex), "RJ45 4IO 输出协议地址必须在 0~3。");
        }

        return _modbus.WriteSingleCoilForDurationAsync(
            _settings.IoModuleIp,
            _settings.IoModulePort,
            _settings.ModbusUnitId,
            (ushort)outputIndex,
            duration,
            _settings.UseRtuOverTcp,
            cancellationToken);
    }

    private decimal DecodeAnalogVoltage(ushort raw)
    {
        if (raw >= 10000)
        {
            var decimalPlaces = raw / 10000;
            var value = raw % 10000;
            return value / Pow10(decimalPlaces);
        }

        return raw / Pow10(_settings.AnalogFixedDecimalPlaces);
    }

    private decimal ConvertCurrentToPressure(decimal currentMilliamp)
    {
        var normalized = Math.Clamp((currentMilliamp - 4m) / 16m, 0m, 1m);
        var span = _recipe.PressureAt20Milliamp - _recipe.PressureAt4Milliamp;
        return _recipe.PressureAt4Milliamp + normalized * span;
    }

    private static decimal Pow10(int power)
    {
        var value = 1m;
        for (var index = 0; index < power; index++)
        {
            value *= 10m;
        }

        return value;
    }
}
