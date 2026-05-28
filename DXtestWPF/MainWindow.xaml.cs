using System.Windows;

namespace DXtestWPF;

public partial class MainWindow : Window
{
    private Dx11DemoWindow? _demoWindow;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshData();
    }

    private void RefreshData()
    {
        SummaryList.ItemsSource = HardwareInfoService.GetSummaryLines();
        DisplayList.ItemsSource = HardwareInfoService.GetDisplayModeLines();
        if (FindName("Direct3DList") is System.Windows.Controls.ListBox direct3DList)
        {
            direct3DList.ItemsSource = HardwareInfoService.GetDirect3DSummaryLines();
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshData();
    }

    private void StartDemo_Click(object sender, RoutedEventArgs e)
    {
        if (_demoWindow is null || !_demoWindow.IsVisible)
        {
            _demoWindow = new Dx11DemoWindow
            {
                Owner = this
            };
            _demoWindow.Closed += (_, _) => _demoWindow = null;
            _demoWindow.Show();
            return;
        }

        _demoWindow.Activate();
    }
}