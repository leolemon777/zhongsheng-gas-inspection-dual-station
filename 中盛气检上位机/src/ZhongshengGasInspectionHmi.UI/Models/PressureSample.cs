namespace ZhongshengGasInspectionHmi.UI.Models;

public sealed record PressureSample(
    decimal PressureMpa,
    decimal CurrentMilliamp,
    ushort RawRegister,
    decimal VoltageVolt);
