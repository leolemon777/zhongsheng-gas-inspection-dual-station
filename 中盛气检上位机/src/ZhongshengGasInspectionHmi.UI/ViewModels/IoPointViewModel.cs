using CommunityToolkit.Mvvm.ComponentModel;

namespace ZhongshengGasInspectionHmi.UI.ViewModels;

public sealed partial class IoPointViewModel : ObservableObject
{
    public IoPointViewModel(int index, string prefix, string description, bool canOperate)
    {
        Index = index;
        Address = $"{prefix}{index + 1}";
        ProtocolAddress = $"{index:X4}H";
        Description = description;
        CanOperate = canOperate;
    }

    public int Index { get; }

    public string Address { get; }

    public string ProtocolAddress { get; }

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private bool _isOn;

    [ObservableProperty]
    private bool _canOperate;

    public string StateText => IsOn ? "ON" : "OFF";
}
