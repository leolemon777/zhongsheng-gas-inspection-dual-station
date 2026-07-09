namespace ZhongshengGasInspectionHmi.UI.Models;

public sealed record InspectionSnapshot
{
    public string StageText { get; init; } = "待机";

    public string OperatorMessage { get; init; } = "待机";

    public decimal? CurrentPressure { get; init; }

    public decimal? CurrentMilliamp { get; init; }

    public decimal? P1 { get; init; }

    public decimal? P2 { get; init; }

    public decimal? LeakRate { get; init; }

    public string ResultText { get; init; } = "--";

    public int ActiveStepIndex { get; init; } = -1;

    public bool CompleteAllSteps { get; init; }

    public TimeSpan? StageElapsed { get; init; }

    public TimeSpan? StageRemaining { get; init; }

    public TimeSpan? StageDuration { get; init; }

    public TimeSpan? TotalElapsed { get; init; }
}
