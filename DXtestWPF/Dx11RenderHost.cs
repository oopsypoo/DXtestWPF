using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DXtestWPF;

public sealed class Dx11RenderHost : HwndHost, IDisposable
{
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPSIBLINGS = 0x04000000;
    private const int WS_CLIPCHILDREN = 0x02000000;
    private const int SWP_NOZORDER = 0x0004;
    private const int SWP_NOACTIVATE = 0x0010;

    private IntPtr _childWindow;
    private D3D11Renderer? _renderer;
    private bool _disposed;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _childWindow = CreateWindowEx(
            0,
            "STATIC",
            string.Empty,
            WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN,
            0,
            0,
            1,
            1,
            hwndParent.Handle,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        if (_childWindow == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to create the DirectX render host window.");
        }

        _renderer = new D3D11Renderer(_childWindow);
        CompositionTarget.Rendering += OnCompositionTargetRendering;
        return new HandleRef(this, _childWindow);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        CompositionTarget.Rendering -= OnCompositionTargetRendering;
        _renderer?.Dispose();
        _renderer = null;

        if (_childWindow != IntPtr.Zero)
        {
            DestroyWindow(_childWindow);
            _childWindow = IntPtr.Zero;
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);

        if (_childWindow == IntPtr.Zero)
        {
            return;
        }

        var width = Math.Max(1, (int)Math.Round(sizeInfo.NewSize.Width));
        var height = Math.Max(1, (int)Math.Round(sizeInfo.NewSize.Height));

        SetWindowPos(_childWindow, IntPtr.Zero, 0, 0, width, height, SWP_NOZORDER | SWP_NOACTIVATE);
        _renderer?.Resize((uint)width, (uint)height);
    }

    private void OnCompositionTargetRendering(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _renderer?.Render();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CompositionTarget.Rendering -= OnCompositionTargetRendering;
        _renderer?.Dispose();
        _renderer = null;

        if (_childWindow != IntPtr.Zero)
        {
            DestroyWindow(_childWindow);
            _childWindow = IntPtr.Zero;
        }

        GC.SuppressFinalize(this);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, int uFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
