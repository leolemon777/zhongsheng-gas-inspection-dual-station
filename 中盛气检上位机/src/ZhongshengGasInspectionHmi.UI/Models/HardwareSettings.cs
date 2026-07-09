using CommunityToolkit.Mvvm.ComponentModel;

namespace ZhongshengGasInspectionHmi.UI.Models;

public sealed partial class HardwareSettings : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StationText))]
    [NotifyPropertyChangedFor(nameof(StationMappingText))]
    private int _activeStationId = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StationText))]
    [NotifyPropertyChangedFor(nameof(StationMappingText))]
    private string _stationName = "工位1";

    [ObservableProperty]
    private string _ioModuleIp = "192.168.0.7";

    [ObservableProperty]
    private int _ioModulePort = 8234;

    [ObservableProperty]
    private string _analogModuleIp = "192.168.0.7";

    [ObservableProperty]
    private int _analogModulePort = 8234;

    [ObservableProperty]
    private byte _modbusUnitId = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StationMappingText))]
    private int _inletOpenCoil = 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StationMappingText))]
    private int _inletCloseCoil = 1;

    [ObservableProperty]
    private int _valvePulseMilliseconds = 5000;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StationMappingText))]
    private int _pressureRegister = 0;

    [ObservableProperty]
    private int _analogFixedDecimalPlaces = 3;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProtocolText))]
    private bool _useRtuOverTcp;

    public string ProtocolText => UseRtuOverTcp ? "RTU over TCP（带CRC16）" : "标准 Modbus TCP";

    public string StationText => $"当前工位：{StationName}";

    public string StationMappingText =>
        $"AI{PressureRegister + 1} / DO{InletCloseCoil + 1}关阀 / DO{InletOpenCoil + 1}开阀";

    public string Validate()
    {
        if (ActiveStationId <= 0)
        {
            return "当前工位编号必须大于 0。";
        }

        if (string.IsNullOrWhiteSpace(StationName))
        {
            return "当前工位名称不能为空。";
        }

        if (string.IsNullOrWhiteSpace(IoModuleIp))
        {
            return "数字量模块 IP 不能为空。";
        }

        if (string.IsNullOrWhiteSpace(AnalogModuleIp))
        {
            return "模拟量模块 IP 不能为空。";
        }

        if (IoModulePort is <= 0 or > 65535)
        {
            return "数字量模块端口必须在 1~65535。";
        }

        if (AnalogModulePort is <= 0 or > 65535)
        {
            return "模拟量模块端口必须在 1~65535。";
        }

        if (ModbusUnitId == 0)
        {
            return "Modbus 站号必须大于 0。";
        }

        if (InletOpenCoil < 0 || InletCloseCoil < 0 || PressureRegister < 0)
        {
            return "线圈和寄存器地址不能小于 0。";
        }

        if (InletOpenCoil == InletCloseCoil)
        {
            return "进气阀打开线圈和关闭线圈不能相同。";
        }

        if (InletOpenCoil > 3 || InletCloseCoil > 3)
        {
            return "RJ45 4IO 进气阀线圈协议地址必须在 0~3。";
        }

        if (ValvePulseMilliseconds is < 50 or > 30000)
        {
            return "阀门动作通电时间必须在 50~30000 ms。";
        }

        if (AnalogFixedDecimalPlaces is < 0 or > 4)
        {
            return "固定小数位必须在 0~4。";
        }

        return string.Empty;
    }
}
