using System.Windows;
using System.Windows.Input;

namespace DXtestWPF;

public partial class Dx11DemoWindow : Window
{
    private bool _isFullscreen = false;

    public Dx11DemoWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Focus();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            return;
        }

        if (e.Key == Key.F11)
        {
            ToggleFullscreen();
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        RenderHost.Dispose();
    }

    private void ToggleFullscreen()
    {
        if (_isFullscreen)
        {
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            Topmost = false;
            WindowState = WindowState.Maximized;
            _isFullscreen = false;
            return;
        }

        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        Topmost = true;
        _isFullscreen = true;
    }
}
