using CommunityToolkit.Mvvm.ComponentModel;

namespace ZhongshengGasInspectionHmi.UI.Models;

public sealed partial class GasInspectionRecipe : ObservableObject
{
    public const decimal DefaultMaxLeakRate = 0.020m;

    [ObservableProperty]
    private double _fillSeconds = 8;

    [ObservableProperty]
    private double _stabilizeSeconds = 5;

    [ObservableProperty]
    private double _holdSeconds = 60;

    [ObservableProperty]
    private decimal _maxLeakRate = DefaultMaxLeakRate;

    [ObservableProperty]
    private decimal _pressureAt4Milliamp = 0.000m;

    [ObservableProperty]
    private decimal _pressureAt20Milliamp = 1.000m;

    [ObservableProperty]
    private decimal _safeExhaustPressure = 0.020m;

    [ObservableProperty]
    private bool _autoExhaust;

    [ObservableProperty]
    private double _exhaustSeconds = 3;

    [ObservableProperty]
    private decimal _minimumFillPressureRise = 0.010m;

    [ObservableProperty]
    private decimal _maximumAllowedPressure = 1.000m;

    public string Validate()
    {
        if (FillSeconds <= 0)
        {
            return "充气时间必须大于 0。";
        }

        if (StabilizeSeconds <= 0)
        {
            return "稳压时间必须大于 0。";
        }

        if (HoldSeconds <= 0)
        {
            return "保压时间必须大于 0。";
        }

        if (MaxLeakRate < 0)
        {
            return "允许漏率不能小于 0。";
        }

        if (PressureAt20Milliamp <= PressureAt4Milliamp)
        {
            return "20mA 对应压力必须大于 4mA 对应压力。";
        }

        if (SafeExhaustPressure < 0)
        {
            return "安全排气压力不能小于 0。";
        }

        if (ExhaustSeconds <= 0)
        {
            return "排气时间必须大于 0。";
        }

        if (MinimumFillPressureRise < 0)
        {
            return "最小充气升压不能小于 0。";
        }

        if (MaximumAllowedPressure <= 0)
        {
            return "最大允许压力必须大于 0。";
        }

        if (MaximumAllowedPressure > PressureAt20Milliamp)
        {
            return "最大允许压力不能大于 20mA 对应压力。";
        }

        return string.Empty;
    }
}
