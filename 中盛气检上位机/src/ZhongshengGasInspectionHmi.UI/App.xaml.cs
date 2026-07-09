using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ZhongshengGasInspectionHmi.UI.Models;
using ZhongshengGasInspectionHmi.UI.Services;
using ZhongshengGasInspectionHmi.UI.ViewModels;

namespace ZhongshengGasInspectionHmi.UI;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        RegisterGlobalExceptionHandlers();
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder(e.Args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
            })
            .ConfigureServices(RegisterServices)
            .Build();

        await _host.StartAsync();
        ApplySavedConfiguration(_host.Services);

        var window = _host.Services.GetRequiredService<MainWindow>();
        window.DataContext = _host.Services.GetRequiredService<MainWindowViewModel>();
        MainWindow = window;
        window.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(3));
            _host.Dispose();
        }

        base.OnExit(e);
    }

    private static void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<GasInspectionRecipe>();
        services.AddSingleton<HardwareSettings>();
        services.AddSingleton<IAppConfigurationStore, AppConfigurationStore>();
        services.AddSingleton<IModbusCommunicationLog, ModbusCommunicationLog>();
        services.AddSingleton<ZhongshengModbusTcpClient>();
        services.AddSingleton<ZhongshengInspectionHardware>();
        services.AddSingleton<IInspectionHardware>(provider => provider.GetRequiredService<ZhongshengInspectionHardware>());
        services.AddSingleton<IIoMonitorHardware>(provider => provider.GetRequiredService<ZhongshengInspectionHardware>());
        services.AddSingleton<GasInspectionRunner>();
        services.AddSingleton<InspectionRecordStore>();
        services.AddSingleton<RunPageViewModel>();
        services.AddSingleton<SettingsPageViewModel>();
        services.AddSingleton<IoMonitorPageViewModel>();
        services.AddSingleton<HardwarePageViewModel>();
        services.AddSingleton<ModbusLogPageViewModel>();
        services.AddSingleton<RecordsPageViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
    }

    private static void ApplySavedConfiguration(IServiceProvider services)
    {
        var configurationStore = services.GetRequiredService<IAppConfigurationStore>();
        var recipe = services.GetRequiredService<GasInspectionRecipe>();
        var hardwareSettings = services.GetRequiredService<HardwareSettings>();
        configurationStore.Apply(configurationStore.Load(), recipe, hardwareSettings);
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCritical(e.Exception, "UI thread crash");
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        LogCritical(e.ExceptionObject as Exception, "Non-UI thread crash");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCritical(e.Exception, "Unobserved task crash");
        e.SetObserved();
    }

    private void LogCritical(Exception? exception, string message)
    {
        var logger = _host?.Services.GetService<ILogger<App>>();
        if (logger is not null)
        {
            logger.LogCritical(exception, "{Message}", message);
            return;
        }

        File.AppendAllText(
            AppStoragePaths.GetDataFilePath("crash.log"),
            $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}{exception}{Environment.NewLine}");
    }
}
