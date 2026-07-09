using System.Text.Json.Serialization;

namespace ZhongshengGasInspectionHmi.UI.Models;

public sealed record AppConfiguration
{
    public int ActiveStationId { get; init; } = 1;

    public GasInspectionRecipeConfiguration Recipe { get; init; } = new();

    public HardwareSettingsConfiguration Hardware { get; init; } = new();

    public IReadOnlyList<StationConfiguration> Stations { get; init; } = [];
}

public sealed record GasInspectionRecipeConfiguration
{
    public double FillSeconds { get; init; } = 8;

    public double StabilizeSeconds { get; init; } = 5;

    public double HoldSeconds { get; init; } = 60;

    public decimal? MaxLeakRate { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? MaxLeakRatePercent { get; init; }

    public decimal PressureAt4Milliamp { get; init; } = 0.000m;

    public decimal PressureAt20Milliamp { get; init; } = 1.000m;

    public decimal SafeExhaustPressure { get; init; } = 0.020m;

    public bool AutoExhaust { get; init; }

    public double ExhaustSeconds { get; init; } = 3;

    public decimal MinimumFillPressureRise { get; init; } = 0.010m;

    public decimal MaximumAllowedPressure { get; init; } = 1.000m;
}

public sealed record HardwareSettingsConfiguration
{
    public string IoModuleIp { get; init; } = "192.168.0.7";

    public int IoModulePort { get; init; } = 8234;

    public string AnalogModuleIp { get; init; } = "192.168.0.7";

    public int AnalogModulePort { get; init; } = 8234;

    public byte ModbusUnitId { get; init; } = 1;

    public int? InletValveCoil { get; init; }

    public int? ExhaustValveCoil { get; init; }

    public int? InletOpenCoil { get; init; }

    public int? InletCloseCoil { get; init; }

    public int ValvePulseMilliseconds { get; init; } = 5000;

    public int PressureRegister { get; init; }

    public int AnalogFixedDecimalPlaces { get; init; } = 3;

    public bool UseRtuOverTcp { get; init; }
}

public sealed record StationConfiguration
{
    public int StationId { get; init; } = 1;

    public string StationName { get; init; } = "工位1";

    public GasInspectionRecipeConfiguration Recipe { get; init; } = new();

    public int? InletOpenCoil { get; init; }

    public int? InletCloseCoil { get; init; }

    public int ValvePulseMilliseconds { get; init; } = 5000;

    public int PressureRegister { get; init; }

    public int AnalogFixedDecimalPlaces { get; init; } = 3;
}
