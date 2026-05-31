using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DXtestWPF;

public sealed class Dx11RenderHost : HwndHost
{
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPSIBLINGS = 0x04000000;
    private const int WS_CLIPCHILDREN = 0x02000000;
    private const int SWP_NOZORDER = 0x0004;
    private const int SWP_NOACTIVATE = 0x0010;
    private const int WM_SETFOCUS = 0x0007;

    private IntPtr _childWindow;
    private IntPtr _parentWindow;
    private D3D11Renderer? _renderer;

    // Render thread and its lifetime controls.
    private Thread? _renderThread;
    private volatile bool _renderThreadRunning;

    // Signals the render thread to pause and perform a resize.
    private volatile bool _resizePending;
    private uint _pendingWidth;
    private uint _pendingHeight;
    private readonly object _resizeLock = new();

    // -----------------------------------------------------------------------
    // Camera
    // -----------------------------------------------------------------------

    /// <summary>
    /// Exposes the renderer's camera (e.g. for reading position in the HUD).
    /// Null until <see cref="BuildWindowCore"/> has run.
    /// </summary>
    public Camera? Camera => _renderer?.Camera;

    /// <summary>
    /// Forward a key-state snapshot to the camera.
    /// Safe to call from the UI thread at any time after construction.
    /// </summary>
    public void SetCameraInput(CameraInput input)
    {
        _renderer?.Camera.ApplyInput(input);
    }

    // -----------------------------------------------------------------------
    // HwndHost implementation
    // -----------------------------------------------------------------------

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _parentWindow = hwndParent.Handle;

        _childWindow = CreateWindowEx(
            0,
            "STATIC",
            string.Empty,
            WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN,
            0, 0, 1, 1,
            _parentWindow,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        if (_childWindow == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to create the DirectX render host window.");
        }

        _renderer = new D3D11Renderer(_childWindow);

        var width = (uint)Math.Max(1, (int)Math.Round(RenderSize.Width));
        var height = (uint)Math.Max(1, (int)Math.Round(RenderSize.Height));
        _renderer.Resize(width, height);

        StartRenderThread();
        return new HandleRef(this, _childWindow);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        StopRenderThread();

        _renderer?.Dispose();
        _renderer = null;

        if (_childWindow != IntPtr.Zero)
        {
            DestroyWindow(_childWindow);
            _childWindow = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Intercept messages sent to the child HWND.
    /// When the child receives WM_SETFOCUS we immediately pass focus back to
    /// the WPF parent so all WPF KeyDown / KeyUp events keep firing normally.
    /// </summary>
    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SETFOCUS && _parentWindow != IntPtr.Zero)
        {
            SetFocus(_parentWindow);
            handled = true;
            return IntPtr.Zero;
        }

        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);

        if (_childWindow == IntPtr.Zero)
        {
            return;
        }

        var width = (uint)Math.Max(1, (int)Math.Round(sizeInfo.NewSize.Width));
        var height = (uint)Math.Max(1, (int)Math.Round(sizeInfo.NewSize.Height));

        SetWindowPos(_childWindow, IntPtr.Zero, 0, 0,
            (int)width, (int)height, SWP_NOZORDER | SWP_NOACTIVATE);

        lock (_resizeLock)
        {
            _pendingWidth = width;
            _pendingHeight = height;
            _resizePending = true;
        }
    }

    // -----------------------------------------------------------------------
    // Render thread
    // -----------------------------------------------------------------------

    private void StartRenderThread()
    {
        _renderThreadRunning = true;
        _renderThread = new Thread(RenderLoop)
        {
            IsBackground = true,
            Name = "D3D11 Render Thread"
        };
        _renderThread.Start();
    }

    private void StopRenderThread()
    {
        _renderThreadRunning = false;
        _renderThread?.Join();
        _renderThread = null;
    }

    private void RenderLoop()
    {
        while (_renderThreadRunning)
        {
            bool doResize;
            uint resizeWidth, resizeHeight;

            lock (_resizeLock)
            {
                doResize = _resizePending;
                resizeWidth = _pendingWidth;
                resizeHeight = _pendingHeight;
                _resizePending = false;
            }

            if (doResize)
            {
                _renderer?.Resize(resizeWidth, resizeHeight);
            }

            _renderer?.Render();
        }
    }

    // -----------------------------------------------------------------------
    // P/Invoke
    // -----------------------------------------------------------------------

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, int uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}