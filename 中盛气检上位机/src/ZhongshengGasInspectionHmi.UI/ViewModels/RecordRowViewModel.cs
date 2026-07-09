namespace ZhongshengGasInspectionHmi.UI.ViewModels;

public sealed record RecordRowViewModel(
    string Time,
    string Station,
    string ProductCode,
    string P1,
    string P2,
    string LeakRate,
    string Result);
