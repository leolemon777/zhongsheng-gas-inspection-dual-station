using ZhongshengGasInspectionHmi.UI.Models;

namespace ZhongshengGasInspectionHmi.UI.Services;

public interface IAppConfigurationStore
{
    event EventHandler? ActiveStationChanged;

    string ConfigurationPath { get; }

    AppConfiguration Load();

    void Apply(AppConfiguration configuration, GasInspectionRecipe recipe, HardwareSettings hardware);

    void ApplyStation(int stationId, GasInspectionRecipe recipe, HardwareSettings hardware);

    void Save(GasInspectionRecipe recipe, HardwareSettings hardware);
}
