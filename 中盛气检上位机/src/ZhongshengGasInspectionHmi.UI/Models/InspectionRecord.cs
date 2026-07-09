namespace ZhongshengGasInspectionHmi.UI.Models;

public sealed record InspectionRecord(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int StationId,
    string StationName,
    string? ProductCode,
    decimal P1,
    decimal P2,
    decimal LeakRate,
    string Result,
    decimal MaxLeakRate,
    double FillSeconds,
    double StabilizeSeconds,
    double HoldSeconds);
