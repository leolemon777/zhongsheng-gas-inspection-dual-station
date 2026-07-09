using System.Globalization;

namespace ZhongshengGasInspectionHmi.UI.Services;

public static class LeakRateFormatter
{
    public static string FormatRatio(decimal leakRate)
    {
        return leakRate.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
