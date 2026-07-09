using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZhongshengGasInspectionHmi.UI.Models;
using ZhongshengGasInspectionHmi.UI.Services;

namespace ZhongshengGasInspectionHmi.UI.ViewModels;

public sealed partial class SettingsPageViewModel : ObservableObject, IDisposable
{
    private readonly GasInspectionRecipe _recipe;
    private readonly HardwareSettings _hardwareSettings;
    private readonly IAppConfigurationStore _configurationStore;

    public SettingsPageViewModel(
        GasInspectionRecipe recipe,
        HardwareSettings hardwareSettings,
        IAppConfigurationStore configurationStore)
    {
        _recipe = recipe;
        _hardwareSettings = hardwareSettings;
        _configurationStore = configurationStore;
        _configurationStore.ActiveStationChanged += OnActiveStationChanged;
        _hardwareSettings.PropertyChanged += OnHardwareSettingsChanged;
        RefreshFromRecipe();
        RefreshExhaustMode();
    }

    [ObservableProperty]
    private double _fillSeconds;

    [ObservableProperty]
    private double _stabilizeSeconds;

    [ObservableProperty]
    private double _holdSeconds;

    [ObservableProperty]
    private decimal _maxLeakRate;

    [ObservableProperty]
    private decimal _pressureAt4Milliamp;

    [ObservableProperty]
    private decimal _pressureAt20Milliamp;

    [ObservableProperty]
    private decimal _safeExhaustPressure;

    [ObservableProperty]
    private bool _autoExhaust;

    [ObservableProperty]
    private double _exhaustSeconds;

    [ObservableProperty]
    private decimal _minimumFillPressureRise;

    [ObservableProperty]
    private decimal _maximumAllowedPressure;

    [ObservableProperty]
    private string _exhaustMode = "手动排气";

    [ObservableProperty]
    private string _saveMessage = "参数修改后点击保存，下一次启动流程生效。";

    public string StationText => _hardwareSettings.StationText;

    [RelayCommand]
    private void Save()
    {
        _recipe.FillSeconds = FillSeconds;
        _recipe.StabilizeSeconds = StabilizeSeconds;
        _recipe.HoldSeconds = HoldSeconds;
        _recipe.MaxLeakRate = MaxLeakRate;
        _recipe.PressureAt4Milliamp = PressureAt4Milliamp;
        _recipe.PressureAt20Milliamp = PressureAt20Milliamp;
        _recipe.SafeExhaustPressure = SafeExhaustPressure;
        _recipe.AutoExhaust = false;
        _recipe.ExhaustSeconds = ExhaustSeconds;
        _recipe.MinimumFillPressureRise = MinimumFillPressureRise;
        _recipe.MaximumAllowedPressure = MaximumAllowedPressure;

        var validation = _recipe.Validate();
        if (!string.IsNullOrEmpty(validation))
        {
            SaveMessage = validation;
            return;
        }

        try
        {
            _configurationStore.Save(_recipe, _hardwareSettings);
        }
        catch (Exception ex)
        {
            SaveMessage = $"参数已应用，但保存配置文件失败：{ex.Message}";
            return;
        }

        RefreshExhaustMode();
        SaveMessage = $"{_hardwareSettings.StationName} 参数已保存。排气由现场手动处理。配置文件：{_configurationStore.ConfigurationPath}";
    }

    partial void OnAutoExhaustChanged(bool value)
    {
        RefreshExhaustMode();
    }

    private void RefreshExhaustMode()
    {
        AutoExhaust = false;
        ExhaustMode = "手动排气（软件不写排气阀）";
    }

    private void OnActiveStationChanged(object? sender, EventArgs e)
    {
        RefreshFromRecipe();
        OnPropertyChanged(nameof(StationText));
        SaveMessage = $"已切换到{_hardwareSettings.StationName}，参数修改后点击保存。";
    }

    private void OnHardwareSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(StationText));
    }

    private void RefreshFromRecipe()
    {
        FillSeconds = _recipe.FillSeconds;
        StabilizeSeconds = _recipe.StabilizeSeconds;
        HoldSeconds = _recipe.HoldSeconds;
        MaxLeakRate = _recipe.MaxLeakRate;
        PressureAt4Milliamp = _recipe.PressureAt4Milliamp;
        PressureAt20Milliamp = _recipe.PressureAt20Milliamp;
        SafeExhaustPressure = _recipe.SafeExhaustPressure;
        AutoExhaust = _recipe.AutoExhaust;
        ExhaustSeconds = _recipe.ExhaustSeconds;
        MinimumFillPressureRise = _recipe.MinimumFillPressureRise;
        MaximumAllowedPressure = _recipe.MaximumAllowedPressure;
    }

    public void Dispose()
    {
        _configurationStore.ActiveStationChanged -= OnActiveStationChanged;
        _hardwareSettings.PropertyChanged -= OnHardwareSettingsChanged;
    }
}
