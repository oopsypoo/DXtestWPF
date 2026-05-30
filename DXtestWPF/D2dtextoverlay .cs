using System.Diagnostics;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace DXtestWPF;

/// <summary>
/// Renders a heads-up text overlay (resolution, refresh rate, FPS) using
/// Direct2D and DirectWrite on top of the D3D11 swap chain back buffer.
///
/// Lifecycle:
///   1. Construct with the DXGI swap chain.
///   2. Call Resize() whenever the swap chain is resized (before D3D11 Present).
///   3. Call Render() each frame after the 3D scene draw but before Present.
///   4. Dispose() when the renderer is torn down.
/// </summary>
internal sealed class D2DTextOverlay : IDisposable
{
    // How often to recalculate the FPS average (seconds).
    private const float FpsUpdateInterval = 0.25f;

    private readonly IDXGISwapChain1 _swapChain;

    private ID2D1Factory1? _d2dFactory;
    private IDWriteFactory? _dwFactory;
    private ID2D1RenderTarget? _renderTarget;
    private ID2D1SolidColorBrush? _textBrush;
    private ID2D1SolidColorBrush? _shadowBrush;
    private IDWriteTextFormat? _textFormat;

    // FPS tracking
    private readonly Stopwatch _fpsClock = Stopwatch.StartNew();
    private int _frameCount;
    private float _currentFps;

    // Cached overlay values — text is rebuilt only when these change.
    private string _overlayText = string.Empty;
    private int _cachedWidth;
    private int _cachedHeight;
    private uint _cachedRefreshRate;

    public D2DTextOverlay(IDXGISwapChain1 swapChain)
    {
        _swapChain = swapChain;
        InitializeFactories();
        CreateRenderTarget();
        QueryRefreshRate();
    }

    /// <summary>
    /// Call this BEFORE ResizeBuffers to release the D2D render target's
    /// reference to the swap chain back buffer.
    /// </summary>
    public void ReleaseRenderTarget()
    {
        DisposeRenderTarget();
    }

    /// <summary>
    /// Call this AFTER ResizeBuffers to recreate the render target against
    /// the new back buffer surface.
    /// </summary>
    public void RecreateRenderTarget()
    {
        CreateRenderTarget();
        QueryRefreshRate();
    }

    /// <summary>
    /// Draw the overlay. Call this after DrawIndexed, before Present.
    /// </summary>
    public void Render()
    {
        if (_renderTarget is null || _textBrush is null || _shadowBrush is null || _textFormat is null)
        {
            return;
        }

        UpdateFps();

        _renderTarget.BeginDraw();

        // Drop shadow: draw offset by 1px in a dark colour first, then white text on top.
        var shadowRect = new Rect(13f, 13f, _cachedWidth - 13f, 200f);
        var textRect = new Rect(12f, 12f, _cachedWidth - 12f, 200f);

        _shadowBrush.Color = new Vortice.Mathematics.Color4(0f, 0f, 0f, 0.75f);
        _renderTarget.DrawText(_overlayText, _textFormat, shadowRect, _shadowBrush);

        _textBrush.Color = new Vortice.Mathematics.Color4(1f, 1f, 1f, 1f);
        _renderTarget.DrawText(_overlayText, _textFormat, textRect, _textBrush);

        _renderTarget.EndDraw();
    }

    public void Dispose()
    {
        DisposeRenderTarget();

        _textFormat?.Dispose();
        _dwFactory?.Dispose();
        _d2dFactory?.Dispose();

        _textFormat = null;
        _dwFactory = null;
        _d2dFactory = null;
    }

    // -------------------------------------------------------------------------

    private void InitializeFactories()
    {
        // D2D factory — single-threaded since all D2D calls happen on the render thread.
        D2D1.D2D1CreateFactory(Vortice.Direct2D1.FactoryType.SingleThreaded, out _d2dFactory).CheckError();

        // DirectWrite factory — shared is the standard choice.
        DWrite.DWriteCreateFactory(Vortice.DirectWrite.FactoryType.Shared, out _dwFactory).CheckError();

        // Text format: Segoe UI, 16pt, normal weight.
        _textFormat = _dwFactory!.CreateTextFormat(
            "Segoe UI",
            null,
            FontWeight.Normal,
            Vortice.DirectWrite.FontStyle.Normal,
            FontStretch.Normal,
            16.0f,
            "en-us");

        _textFormat.TextAlignment = TextAlignment.Leading;
        _textFormat.ParagraphAlignment = ParagraphAlignment.Near;
    }

    private void CreateRenderTarget()
    {
        if (_d2dFactory is null)
        {
            return;
        }

        // Wrap the swap chain back buffer as a DXGI surface for D2D.
        using IDXGISurface dxgiSurface = _swapChain.GetBuffer<IDXGISurface>(0);

        var props = new RenderTargetProperties(
            new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied));

        _renderTarget = _d2dFactory.CreateDxgiSurfaceRenderTarget(dxgiSurface, props);

        // Brushes are tied to the render target — recreate them alongside it.
        _textBrush = _renderTarget.CreateSolidColorBrush(new Vortice.Mathematics.Color4(1f, 1f, 1f, 1f));
        _shadowBrush = _renderTarget.CreateSolidColorBrush(new Vortice.Mathematics.Color4(0f, 0f, 0f, 0.75f));

        // Cache surface dimensions for text layout rectangles.
        SwapChainDescription1 desc = _swapChain.Description1;
        _cachedWidth = (int)desc.Width;
        _cachedHeight = (int)desc.Height;
    }

    private void DisposeRenderTarget()
    {
        _shadowBrush?.Dispose();
        _textBrush?.Dispose();
        _renderTarget?.Dispose();
        _shadowBrush = null;
        _textBrush = null;
        _renderTarget = null;
    }

    private void QueryRefreshRate()
    {
        // Walk DXGI: SwapChain → Output → enumerate display modes for our
        // format, then pick the highest refresh rate among them.
        try
        {
            using IDXGIOutput output = _swapChain.GetContainingOutput();
            ModeDescription[] modes = output.GetDisplayModeList(
                _swapChain.Description1.Format,
                Vortice.DXGI.DisplayModeEnumerationFlags.Interlaced);

            // Find the highest refresh rate using cross-multiplication
            // to avoid floating point division.
            uint bestNumerator = 0;
            uint bestDenominator = 1;
            foreach (ModeDescription mode in modes)
            {
                uint denom = Math.Max(1, mode.RefreshRate.Denominator);
                uint num = mode.RefreshRate.Numerator;
                if (num * bestDenominator > bestNumerator * denom)
                {
                    bestNumerator = num;
                    bestDenominator = denom;
                }
            }

            _cachedRefreshRate = bestDenominator > 0
                ? bestNumerator / bestDenominator
                : 0;
        }
        catch
        {
            // WARP or headless — report 0.
            _cachedRefreshRate = 0;
        }

        RebuildOverlayText();
    }

    private void UpdateFps()
    {
        _frameCount++;

        double elapsed = _fpsClock.Elapsed.TotalSeconds;
        if (elapsed >= FpsUpdateInterval)
        {
            _currentFps = (float)(_frameCount / elapsed);
            _frameCount = 0;
            _fpsClock.Restart();
            RebuildOverlayText();
        }
    }

    private void RebuildOverlayText()
    {
        string refreshRate = _cachedRefreshRate > 0
            ? $"{_cachedRefreshRate} Hz"
            : "N/A";

        _overlayText =
            $"Resolution:   {_cachedWidth} \u00d7 {_cachedHeight}\n" +
            $"Refresh rate: {refreshRate}\n" +
            $"FPS:          {_currentFps:F0}";
    }
}