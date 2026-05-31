using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace DXtestWPF;

public partial class Dx11DemoWindow : Window
{
    private bool _isFullscreen = false;

    // Held-key state — set on KeyDown, cleared on KeyUp.
    private bool _camForward;   // W / Arrow-Up
    private bool _camBackward;  // S / Arrow-Down
    private bool _camLeft;      // A / Arrow-Left
    private bool _camRight;     // D / Arrow-Right
    private bool _camUp;        // E
    private bool _camDown;      // Q

    public Dx11DemoWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Focus();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); return;
            case Key.F11: ToggleFullscreen(); return;

            case Key.W: case Key.Up: _camForward = true; break;
            case Key.S: case Key.Down: _camBackward = true; break;
            case Key.A: case Key.Left: _camLeft = true; break;
            case Key.D: case Key.Right: _camRight = true; break;
            case Key.E: _camUp = true; break;
            case Key.Q: _camDown = true; break;
            default: return;
        }

        PushCameraInput();
    }

    private void Window_KeyUp(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.W: case Key.Up: _camForward = false; break;
            case Key.S: case Key.Down: _camBackward = false; break;
            case Key.A: case Key.Left: _camLeft = false; break;
            case Key.D: case Key.Right: _camRight = false; break;
            case Key.E: _camUp = false; break;
            case Key.Q: _camDown = false; break;
            default: return;
        }

        PushCameraInput();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        RenderHost.SetCameraInput(default);
        RenderHost.Dispose();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void PushCameraInput()
    {
        RenderHost.SetCameraInput(new CameraInput(
            MoveForward: _camForward,
            MoveBackward: _camBackward,
            StrafeLeft: _camLeft,
            StrafeRight: _camRight,
            MoveUp: _camUp,
            MoveDown: _camDown));
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