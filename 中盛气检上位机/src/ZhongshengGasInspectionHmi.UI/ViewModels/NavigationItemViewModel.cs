using CommunityToolkit.Mvvm.ComponentModel;

namespace ZhongshengGasInspectionHmi.UI.ViewModels;

public sealed partial class NavigationItemViewModel : ObservableObject
{
    public NavigationItemViewModel(string title, string key, object page)
    {
        Title = title;
        Key = key;
        Page = page;
    }

    public string Title { get; }

    public string Key { get; }

    public object Page { get; }

    [ObservableProperty]
    private bool _isSelected;
}
