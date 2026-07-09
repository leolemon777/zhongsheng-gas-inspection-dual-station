namespace ZhongshengGasInspectionHmi.UI.Services;

public sealed record ModbusCommunicationLogEntry(
    DateTimeOffset Time,
    string Direction,
    string Transport,
    string Endpoint,
    string FunctionCode,
    string AddressText,
    string Detail,
    string HexFrame);
