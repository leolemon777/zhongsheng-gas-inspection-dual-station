using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using ZhongshengGasInspectionHmi.UI.Models;
using ZhongshengGasInspectionHmi.UI.ViewModels;

namespace ZhongshengGasInspectionHmi.UI;

public partial class MainWindow : Window
{
    // 工位 1：绿色主题（原始配色）
    private static readonly (Color Primary, Color PrimaryDark, Color PrimarySoft, Color Text) Station1Palette =
        (Color.FromRgb(0x00, 0x75, 0x4A), Color.FromRgb(0x1E, 0x39, 0x32), Color.FromRgb(0xD4, 0xE9, 0xE2), Color.FromRgb(0x1E, 0x39, 0x32));

    // 工位 2：克莱因蓝主题（International Klein Blue）
    private static readonly (Color Primary, Color PrimaryDark, Color PrimarySoft, Color Text) Station2Palette =
        (Color.FromRgb(0x00, 0x2F, 0xA7), Color.FromRgb(0x00, 0x1E, 0x5C), Color.FromRgb(0xD6, 0xE4, 0xF7), Color.FromRgb(0x00, 0x1E, 0x5C));

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainWindowViewModel oldVm)
        {
            oldVm.Hardware.PropertyChanged -= OnHardwareChanged;
        }

        if (e.NewValue is MainWindowViewModel newVm)
        {
            newVm.Hardware.PropertyChanged += OnHardwareChanged;
            ApplyStationTheme(newVm.Hardware.ActiveStationId);
        }
    }

    private void OnHardwareChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HardwareSettings.ActiveStationId))
        {
            return;
        }

        if (DataContext is MainWindowViewModel vm)
        {
            ApplyStationTheme(vm.Hardware.ActiveStationId);
        }
    }

    private void ApplyStationTheme(int stationId)
    {
        // 资源里的 SolidColorBrush 经 XAML 加载后被冻结（只读），无法改 Color；
        // 这里改为整体替换 Resources 中的画笔对象，配合 XAML 的 DynamicResource 引用，
        // 切换工位时整个界面会随主题色重绘（工位 1=绿、工位 2=克莱因蓝）。
        var palette = stationId == 2 ? Station2Palette : Station1Palette;
        Resources["Brush.Primary"] = new SolidColorBrush(palette.Primary);
        Resources["Brush.PrimaryDark"] = new SolidColorBrush(palette.PrimaryDark);
        Resources["Brush.PrimarySoft"] = new SolidColorBrush(palette.PrimarySoft);
        Resources["Brush.Text"] = new SolidColorBrush(palette.Text);
    }
}
