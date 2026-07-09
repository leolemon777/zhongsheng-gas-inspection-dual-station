namespace ZhongshengGasInspectionHmi.UI.Services;

public static class PressureFormatter
{
    private const decimal KilopascalPerMpa = 1_000m;

    public static decimal ToKilopascal(decimal pressureMpa)
    {
        return decimal.Round(pressureMpa * KilopascalPerMpa, 3, MidpointRounding.AwayFromZero);
    }

    public static string FormatKilopascal(decimal pressureMpa)
    {
        return $"{ToKilopascal(pressureMpa):0.000}";
    }

    public static string FormatKilopascalWithUnit(decimal pressureMpa)
    {
        return $"{FormatKilopascal(pressureMpa)} kPa";
    }
}
