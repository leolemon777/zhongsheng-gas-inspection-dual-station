using ZhongshengGasInspectionHmi.UI.Models;

namespace ZhongshengGasInspectionHmi.UI.Services;

public interface IInspectionHardware
{
    bool IsConnected { get; }

    string StatusText { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);

    Task<PressureSample> ReadPressureAsync(CancellationToken cancellationToken);

    Task OpenInletValveAsync(CancellationToken cancellationToken);

    Task CloseInletValveAsync(CancellationToken cancellationToken);

    async Task CloseValvesAsync(CancellationToken cancellationToken)
    {
        await CloseInletValveAsync(cancellationToken);
    }
}
