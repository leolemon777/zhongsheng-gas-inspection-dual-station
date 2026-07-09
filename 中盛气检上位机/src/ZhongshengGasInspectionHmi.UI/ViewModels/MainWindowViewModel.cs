using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZhongshengGasInspectionHmi.UI.Models;
using ZhongshengGasInspectionHmi.UI.Services;

namespace ZhongshengGasInspectionHmi.UI.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IAppConfigurationStore _configurationStore;
    private readonly HardwareSettings _hardwareSettings;
    private readonly GasInspectionRecipe _recipe;

    public MainWindowViewModel(
        RunPageViewModel runPage,
        SettingsPageViewModel settingsPage,
        IoMonitorPageViewModel ioMonitorPage,
        HardwarePageViewModel hardwarePage,
        ModbusLogPageViewModel modbusLogPage,
        RecordsPageViewModel recordsPage,
        IAppConfigurationStore configurationStore,
        HardwareSettings hardwareSettings,
        GasInspectionRecipe recipe)
    {
        RunPage = runPage;
        _configurationStore = configurationStore;
        _hardwareSettings = hardwareSettings;
        _recipe = recipe;
        NavigationItems =
        [
            new NavigationItemViewModel("生产主页", "RUN", runPage),
            new NavigationItemViewModel("参数设置", "SET", settingsPage),
            new NavigationItemViewModel("IO监控", "IO", ioMonitorPage),
            new NavigationItemViewModel("硬件连接", "NET", hardwarePage),
            new NavigationItemViewModel("通信日志", "COM", modbusLogPage),
            new NavigationItemViewModel("检测记录", "LOG", recordsPage)
        ];
        SelectPage(NavigationItems[0]);
    }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

    public RunPageViewModel RunPage { get; }

    public HardwareSettings Hardware => _hardwareSettings;

    [ObservableProperty]
    private object? _currentPage;

    [RelayCommand]
    private void SelectPage(NavigationItemViewModel item)
    {
        foreach (var navigationItem in NavigationItems)
        {
            navigationItem.IsSelected = navigationItem == item;
        }

        CurrentPage = item.Page;
    }

    [RelayCommand]
    private void SwitchStation(string stationId)
    {
        if (!int.TryParse(stationId, out var id) || id <= 0)
        {
            return;
        }

        _configurationStore.ApplyStation(id, _recipe, _hardwareSettings);
        _configurationStore.Save(_recipe, _hardwareSettings);
    }
}
