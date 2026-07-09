namespace ZhongshengGasInspectionHmi.UI.Services;

public interface IIoMonitorHardware
{
    Task<IReadOnlyList<bool>> ReadDigitalInputsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<bool>> ReadDigitalOutputsAsync(CancellationToken cancellationToken);

    Task SetDigitalOutputAsync(int outputIndex, bool isOn, CancellationToken cancellationToken);

    Task ResetKnownOutputsAsync(CancellationToken cancellationToken);
}
