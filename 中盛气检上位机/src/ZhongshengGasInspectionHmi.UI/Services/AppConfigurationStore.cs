using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZhongshengGasInspectionHmi.UI.Models;

namespace ZhongshengGasInspectionHmi.UI.Services;

public sealed class AppConfigurationStore : IAppConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly ILogger<AppConfigurationStore> _logger;
    private readonly string _configurationPath;
    private AppConfiguration _currentConfiguration = new();

    public AppConfigurationStore()
        : this(NullLogger<AppConfigurationStore>.Instance)
    {
    }

    public AppConfigurationStore(ILogger<AppConfigurationStore> logger)
    {
        _logger = logger;
        AppStoragePaths.CopyLegacyFileIfNeeded("appsettings.json");
        _configurationPath = AppStoragePaths.GetDataFilePath("appsettings.json");
    }

    public string ConfigurationPath => _configurationPath;

    public event EventHandler? ActiveStationChanged;

    public AppConfiguration Load()
    {
        if (!File.Exists(_configurationPath))
        {
            return new AppConfiguration();
        }

        try
        {
            var json = File.ReadAllText(_configurationPath);
            return Normalize(JsonSerializer.Deserialize<AppConfiguration>(json, JsonOptions) ?? new AppConfiguration());
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "配置文件 JSON 格式错误，已使用默认配置。Path={Path}", _configurationPath);
            return Normalize(new AppConfiguration());
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "读取配置文件失败，已使用默认配置。Path={Path}", _configurationPath);
            return Normalize(new AppConfiguration());
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "读取配置文件权限不足，已使用默认配置。Path={Path}", _configurationPath);
            return Normalize(new AppConfiguration());
        }
    }

    public void Apply(AppConfiguration configuration, GasInspectionRecipe recipe, HardwareSettings hardware)
    {
        _currentConfiguration = Normalize(configuration);
        ApplyActiveStation(recipe, hardware);
    }

    public void ApplyStation(int stationId, GasInspectionRecipe recipe, HardwareSettings hardware)
    {
        var station = FindStation(_currentConfiguration, stationId);
        _currentConfiguration = _currentConfiguration with { ActiveStationId = station.StationId };
        ApplyActiveStation(recipe, hardware);
        ActiveStationChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Save(GasInspectionRecipe recipe, HardwareSettings hardware)
    {
        _currentConfiguration = SaveCurrentStation(_currentConfiguration, recipe, hardware);
        var configuration = new AppConfiguration
        {
            ActiveStationId = _currentConfiguration.ActiveStationId,
            Recipe = ToRecipeConfiguration(recipe),
            Hardware = new HardwareSettingsConfiguration
            {
                IoModuleIp = hardware.IoModuleIp,
                IoModulePort = hardware.IoModulePort,
                AnalogModuleIp = hardware.AnalogModuleIp,
                AnalogModulePort = hardware.AnalogModulePort,
                ModbusUnitId = hardware.ModbusUnitId,
                UseRtuOverTcp = hardware.UseRtuOverTcp
            },
            Stations = _currentConfiguration.Stations
        };
        _currentConfiguration = configuration;
        var json = JsonSerializer.Serialize(configuration, JsonOptions);
        File.WriteAllText(_configurationPath, json);
    }

    private void ApplyActiveStation(GasInspectionRecipe recipe, HardwareSettings hardware)
    {
        var station = FindStation(_currentConfiguration, _currentConfiguration.ActiveStationId);
        ApplyRecipe(station.Recipe, recipe);

        var module = _currentConfiguration.Hardware;
        hardware.ActiveStationId = station.StationId;
        hardware.StationName = station.StationName;
        hardware.IoModuleIp = module.IoModuleIp;
        hardware.IoModulePort = module.IoModulePort;
        hardware.AnalogModuleIp = module.AnalogModuleIp;
        hardware.AnalogModulePort = module.AnalogModulePort;
        hardware.ModbusUnitId = module.ModbusUnitId;
        hardware.UseRtuOverTcp = module.UseRtuOverTcp;
        hardware.InletOpenCoil = station.InletOpenCoil ?? 0;
        hardware.InletCloseCoil = station.InletCloseCoil ?? 1;
        hardware.ValvePulseMilliseconds = station.ValvePulseMilliseconds;
        hardware.PressureRegister = station.PressureRegister;
        hardware.AnalogFixedDecimalPlaces = station.AnalogFixedDecimalPlaces;
    }

    private static AppConfiguration SaveCurrentStation(
        AppConfiguration configuration,
        GasInspectionRecipe recipe,
        HardwareSettings hardware)
    {
        var activeStationId = hardware.ActiveStationId;
        var station = new StationConfiguration
        {
            StationId = activeStationId,
            StationName = string.IsNullOrWhiteSpace(hardware.StationName) ? $"工位{activeStationId}" : hardware.StationName.Trim(),
            Recipe = ToRecipeConfiguration(recipe),
            InletOpenCoil = hardware.InletOpenCoil,
            InletCloseCoil = hardware.InletCloseCoil,
            ValvePulseMilliseconds = hardware.ValvePulseMilliseconds,
            PressureRegister = hardware.PressureRegister,
            AnalogFixedDecimalPlaces = hardware.AnalogFixedDecimalPlaces
        };

        var stations = configuration.Stations.ToList();
        var index = stations.FindIndex(item => item.StationId == activeStationId);
        if (index >= 0)
        {
            stations[index] = station;
        }
        else
        {
            stations.Add(station);
        }

        stations.Sort((left, right) => left.StationId.CompareTo(right.StationId));
        return configuration with
        {
            ActiveStationId = activeStationId,
            Stations = stations
        };
    }

    private static AppConfiguration Normalize(AppConfiguration configuration)
    {
        var legacyStation = new StationConfiguration
        {
            StationId = 1,
            StationName = "工位1",
            Recipe = NormalizeRecipe(configuration.Recipe),
            InletOpenCoil = configuration.Hardware.InletOpenCoil
                ?? configuration.Hardware.InletValveCoil
                ?? 1,
            InletCloseCoil = configuration.Hardware.InletCloseCoil
                ?? configuration.Hardware.ExhaustValveCoil
                ?? 0,
            ValvePulseMilliseconds = configuration.Hardware.ValvePulseMilliseconds,
            PressureRegister = configuration.Hardware.PressureRegister,
            AnalogFixedDecimalPlaces = configuration.Hardware.AnalogFixedDecimalPlaces
        };
        var defaultSecondStation = new StationConfiguration
        {
            StationId = 2,
            StationName = "工位2",
            Recipe = NormalizeRecipe(configuration.Recipe),
            InletOpenCoil = 3,
            InletCloseCoil = 2,
            ValvePulseMilliseconds = configuration.Hardware.ValvePulseMilliseconds,
            PressureRegister = 1,
            AnalogFixedDecimalPlaces = configuration.Hardware.AnalogFixedDecimalPlaces
        };

        var stations = configuration.Stations
            .Select(NormalizeStation)
            .ToList();
        UpsertMissingStation(stations, legacyStation);
        UpsertMissingStation(stations, defaultSecondStation);
        stations.Sort((left, right) => left.StationId.CompareTo(right.StationId));

        var activeStationId = configuration.ActiveStationId <= 0 ? 1 : configuration.ActiveStationId;
        if (stations.All(station => station.StationId != activeStationId))
        {
            activeStationId = 1;
        }

        return configuration with
        {
            ActiveStationId = activeStationId,
            Recipe = NormalizeRecipe(configuration.Recipe),
            Stations = stations
        };
    }

    private static StationConfiguration NormalizeStation(StationConfiguration station)
    {
        var stationId = station.StationId <= 0 ? 1 : station.StationId;
        return station with
        {
            StationId = stationId,
            StationName = string.IsNullOrWhiteSpace(station.StationName) ? $"工位{stationId}" : station.StationName,
            Recipe = NormalizeRecipe(station.Recipe),
            InletOpenCoil = station.InletOpenCoil ?? (stationId == 2 ? 3 : 1),
            InletCloseCoil = station.InletCloseCoil ?? (stationId == 2 ? 2 : 0),
            ValvePulseMilliseconds = station.ValvePulseMilliseconds,
            PressureRegister = station.PressureRegister,
            AnalogFixedDecimalPlaces = station.AnalogFixedDecimalPlaces
        };
    }

    private static void UpsertMissingStation(List<StationConfiguration> stations, StationConfiguration station)
    {
        if (stations.Any(item => item.StationId == station.StationId))
        {
            return;
        }

        stations.Add(station);
    }

    private static StationConfiguration FindStation(AppConfiguration configuration, int stationId)
    {
        return configuration.Stations.FirstOrDefault(station => station.StationId == stationId)
            ?? configuration.Stations.First(station => station.StationId == 1);
    }

    private static GasInspectionRecipeConfiguration NormalizeRecipe(GasInspectionRecipeConfiguration recipe)
    {
        return recipe with
        {
            MaxLeakRate = recipe.MaxLeakRate
                ?? recipe.MaxLeakRatePercent
                ?? GasInspectionRecipe.DefaultMaxLeakRate,
            AutoExhaust = false
        };
    }

    private static void ApplyRecipe(GasInspectionRecipeConfiguration configuration, GasInspectionRecipe recipe)
    {
        recipe.FillSeconds = configuration.FillSeconds;
        recipe.StabilizeSeconds = configuration.StabilizeSeconds;
        recipe.HoldSeconds = configuration.HoldSeconds;
        recipe.MaxLeakRate = configuration.MaxLeakRate
            ?? configuration.MaxLeakRatePercent
            ?? GasInspectionRecipe.DefaultMaxLeakRate;
        recipe.PressureAt4Milliamp = configuration.PressureAt4Milliamp;
        recipe.PressureAt20Milliamp = configuration.PressureAt20Milliamp;
        recipe.SafeExhaustPressure = configuration.SafeExhaustPressure;
        recipe.AutoExhaust = false;
        recipe.ExhaustSeconds = configuration.ExhaustSeconds;
        recipe.MinimumFillPressureRise = configuration.MinimumFillPressureRise;
        recipe.MaximumAllowedPressure = configuration.MaximumAllowedPressure;
    }

    private static GasInspectionRecipeConfiguration ToRecipeConfiguration(GasInspectionRecipe recipe)
    {
        return new GasInspectionRecipeConfiguration
        {
            FillSeconds = recipe.FillSeconds,
            StabilizeSeconds = recipe.StabilizeSeconds,
            HoldSeconds = recipe.HoldSeconds,
            MaxLeakRate = recipe.MaxLeakRate,
            PressureAt4Milliamp = recipe.PressureAt4Milliamp,
            PressureAt20Milliamp = recipe.PressureAt20Milliamp,
            SafeExhaustPressure = recipe.SafeExhaustPressure,
            AutoExhaust = false,
            ExhaustSeconds = recipe.ExhaustSeconds,
            MinimumFillPressureRise = recipe.MinimumFillPressureRise,
            MaximumAllowedPressure = recipe.MaximumAllowedPressure
        };
    }
}
