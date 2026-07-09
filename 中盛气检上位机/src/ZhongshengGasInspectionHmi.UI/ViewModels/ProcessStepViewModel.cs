using CommunityToolkit.Mvvm.ComponentModel;

namespace ZhongshengGasInspectionHmi.UI.ViewModels;

public sealed partial class ProcessStepViewModel : ObservableObject
{
    public ProcessStepViewModel(int order, string name, string description)
    {
        Order = order;
        Name = name;
        Description = description;
    }

    public int Order { get; }

    public string Name { get; }

    public string Description { get; }

    [ObservableProperty]
    private string _status = "等待";

    [ObservableProperty]
    private string _timeText = "--";
}
