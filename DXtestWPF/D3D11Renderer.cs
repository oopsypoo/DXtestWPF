using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct2D1.Effects;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using BitmapSource = System.Windows.Media.Imaging.BitmapSource;

namespace DXtestWPF;

internal sealed class D3D11Renderer : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGISwapChain1? _swapChain;
    private ID3D11RenderTargetView? _renderTargetView;
    private ID3D11Texture2D? _depthStencilBuffer;
    private ID3D11DepthStencilView? _depthStencilView;
    private ID3D11DepthStencilState? _depthStencilState;
    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11InputLayout? _inputLayout;
    private ID3D11Buffer? _vertexBuffer;
    private ID3D11Buffer? _indexBuffer;
    private ID3D11Buffer? _floorVertexBuffer;
    private ID3D11Buffer? _floorIndexBuffer;
    private ID3D11Buffer? _constantBuffer;
    private ID3D11SamplerState? _samplerState;
    private ID3D11ShaderResourceView? _cubeTextureView; //texture for the cube
    private ID3D11ShaderResourceView? _floorTextureView; //texture for the floor (if you decide to render it)
    private ID3D11RasterizerState? _rasterizerState;
    private FeatureLevel _featureLevel;
    private string _shaderModel = "Unknown";
    private Viewport _viewport;
    private int _indexCount;
    private D2DTextOverlay? _overlay;

    // -------------------------------------------------------------------
    // Camera
    // -------------------------------------------------------------------

    /// <summary>
    /// The camera that drives the view matrix.
    /// Created here so the renderer owns the object lifetime; the render host
    /// forwards input snapshots to it via <see cref="Camera.ApplyInput"/>.
    /// Initial position matches the old hard-coded value (0, 0, -6).
    /// </summary>
    public Camera Camera { get; } = new Camera(new Vector3(0.0f, 0.0f, -6.0f));

    // Timing for the per-frame delta used by Camera.Update.
    private double _lastFrameTime;
    

    public D3D11Renderer(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _lastFrameTime = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        InitializeDeviceAndSwapChain();
    }

    public FeatureLevel FeatureLevel => _featureLevel;

    public string ShaderModel => _shaderModel;

    public void Render()
    {
        if (_context is null || _swapChain is null || _cubeTextureView is null || _floorTextureView is null
            || _renderTargetView is null || _depthStencilView is null)
        {
            return;
        }

        // --- Delta time --------------------------------------------------
        double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        float deltaTime = (float)(now - _lastFrameTime);
        _lastFrameTime = now;

        // Clamp delta so a stalled frame doesn't teleport the camera.
        deltaTime = Math.Clamp(deltaTime, 0.0f, 0.1f);

        // --- Update camera -----------------------------------------------
        Camera.Update(deltaTime);

        // --- Build matrices ----------------------------------------------
        float timeSeconds = (float)_clock.Elapsed.TotalSeconds;
        Color4 clearColor = new(0.07f, 0.09f, 0.14f, 1.0f);

        Matrix4x4 world = Matrix4x4.CreateRotationY(timeSeconds * 0.85f)
                        * Matrix4x4.CreateRotationX(timeSeconds * 0.55f);

        // Use the camera's live view matrix instead of the hardcoded LookAt.
        Matrix4x4 view = Camera.ViewMatrix;

        float aspectRatio = Math.Max(1.0f, _viewport.Width / Math.Max(1.0f, _viewport.Height));
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4.0f, aspectRatio, 0.1f, 100.0f);

        Matrix4x4 worldViewProjection = Matrix4x4.Transpose(world * view * projection);

        UpdateConstantBuffer(worldViewProjection, Matrix4x4.Transpose(world),0);

        // ---DRAW FLOOR-- -
        Matrix4x4 floorWorld = Matrix4x4.Identity;
        // Pass '1' to turn ON the normal mapping and atlas math
        UpdateConstantBuffer(worldViewProjection, Matrix4x4.Transpose(floorWorld), 1);

        // Bind the FLOOR texture
        _context.PSSetShaderResource(0, _floorTextureView);

        _context.IASetVertexBuffer(0, _floorVertexBuffer!, (uint)Vertex.SizeInBytes);
        _context.IASetIndexBuffer(_floorIndexBuffer, Format.R16_UInt, 0);
        _context.DrawIndexed(6, 0, 0);
        // --- Draw --------------------------------------------------------
        _context.RSSetViewport(_viewport);
        _context.RSSetState(_rasterizerState);
        _context.OMSetDepthStencilState(_depthStencilState);
        _context.OMSetRenderTargets(_renderTargetView, _depthStencilView);
        _context.ClearRenderTargetView(_renderTargetView, clearColor);
        _context.ClearDepthStencilView(_depthStencilView, DepthStencilClearFlags.Depth, 1.0f, 0);

        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.IASetInputLayout(_inputLayout);
        _context.IASetVertexBuffer(0, _vertexBuffer!, (uint)Vertex.SizeInBytes, 0);
        _context.IASetIndexBuffer(_indexBuffer!, Format.R16_UInt, 0);

        _context.VSSetShader(_vertexShader);
        _context.VSSetConstantBuffer(0, _constantBuffer);
        _context.PSSetShader(_pixelShader);
        _context.PSSetConstantBuffer(0, _constantBuffer); 
        _context.PSSetShaderResource(0, _cubeTextureView);
        _context.PSSetSampler(0, _samplerState);

        _context.DrawIndexed((uint)_indexCount, 0, 0);

        // Draw the HUD overlay on top of the 3D scene before presenting.
        _overlay?.Render();

        _swapChain.Present(1, PresentFlags.None);
    }

    public void Resize(uint width, uint height)
    {
        if (_swapChain is null || _device is null || width == 0 || height == 0)
        {
            return;
        }

        // Release ALL back buffer references before ResizeBuffers.
        _overlay?.ReleaseRenderTarget();
        ReleaseTargetResources();

        _swapChain.ResizeBuffers(0, width, height, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();

        CreateTargetResources((int)width, (int)height);
        _overlay?.RecreateRenderTarget();
    }

    private void InitializeDeviceAndSwapChain()
    {
        try
        {
            D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                D3DHelper.FeatureLevels,
                out _device,
                out _featureLevel,
                out _context).CheckError();
        }
        catch
        {
            D3D11.D3D11CreateDevice(
                null,
                DriverType.Warp,
                DeviceCreationFlags.BgraSupport,
                D3DHelper.FeatureLevels,
                out _device,
                out _featureLevel,
                out _context).CheckError();
        }

        _shaderModel = D3DHelper.EstimatedShaderModel(_featureLevel);

        using IDXGIFactory4 factory = DXGI.CreateDXGIFactory2<IDXGIFactory4>(false);

        SwapChainDescription1 swapChainDescription = new()
        {
            Width = 1,
            Height = 1,
            Format = Format.B8G8R8A8_UNorm,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = AlphaMode.Ignore,
            Flags = SwapChainFlags.None
        };

        _swapChain = factory.CreateSwapChainForHwnd(_device, _hwnd, swapChainDescription, null, null);

        CreatePipelineResources();
        _overlay = new D2DTextOverlay(_swapChain);
    }

    private void CreateTargetResources(int width, int height)
    {
        if (_device is null || _swapChain is null)
        {
            return;
        }

        using ID3D11Texture2D backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _renderTargetView = _device.CreateRenderTargetView(backBuffer);

        DepthStencilDescription stencilDescription = new()
        {
            DepthEnable = true,
            DepthWriteMask = DepthWriteMask.All,
            DepthFunc = ComparisonFunction.Less,
            StencilEnable = false,
            StencilReadMask = 0xFF,
            StencilWriteMask = 0xFF,
            FrontFace = new DepthStencilOperationDescription
            {
                StencilFailOp = StencilOperation.Keep,
                StencilDepthFailOp = StencilOperation.Keep,
                StencilPassOp = StencilOperation.Keep,
                StencilFunc = ComparisonFunction.Always
            },
            BackFace = new DepthStencilOperationDescription
            {
                StencilFailOp = StencilOperation.Keep,
                StencilDepthFailOp = StencilOperation.Keep,
                StencilPassOp = StencilOperation.Keep,
                StencilFunc = ComparisonFunction.Always
            }
        };

        Texture2DDescription depthDescription = new()
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.D24_UNorm_S8_UInt,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.DepthStencil,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None
        };

        _depthStencilBuffer = _device.CreateTexture2D(depthDescription);
        _depthStencilView = _device.CreateDepthStencilView(_depthStencilBuffer);
        _depthStencilState = _device.CreateDepthStencilState(stencilDescription);
        _viewport = new Viewport(0, 0, width, height, 0, 1);
    }

    private void CreatePipelineResources()
    {
        if (_device is null)
        {
            return;
        }

        byte[] vertexShaderBytecode = LoadRequiredShaderBytecode("TexturedCubeVS.cso");
        byte[] pixelShaderBytecode = LoadRequiredShaderBytecode("TexturedCubePS.cso");

        _vertexShader = _device.CreateVertexShader(vertexShaderBytecode);
        _pixelShader = _device.CreatePixelShader(pixelShaderBytecode);

        _inputLayout = _device.CreateInputLayout(
            new[]
            {
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElementDescription("NORMAL",   0, Format.R32G32B32_Float, 12, 0), // Normal starts at byte 12
                new InputElementDescription("TANGENT",  0, Format.R32G32B32_Float, 24, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 36, 0),   // TexCoord pushed to byte 36
            },
            vertexShaderBytecode);

        CubeGeometry cube = CreateCubeGeometry();
        _indexCount = cube.Indices.Length;

        _vertexBuffer = _device.CreateBuffer(cube.Vertices, BindFlags.VertexBuffer);
        _indexBuffer = _device.CreateBuffer(cube.Indices, BindFlags.IndexBuffer);

        _constantBuffer = _device.CreateBuffer(
            new BufferDescription(
                (uint)Marshal.SizeOf<TransformConstants>(),
                BindFlags.ConstantBuffer,
                ResourceUsage.Dynamic,
                CpuAccessFlags.Write));

        _samplerState = _device.CreateSamplerState(new SamplerDescription
        {
            Filter = Filter.Anisotropic,
            AddressU = TextureAddressMode.Wrap,
            AddressV = TextureAddressMode.Wrap,
            AddressW = TextureAddressMode.Wrap,
            ComparisonFunc = ComparisonFunction.Never,
            MinLOD = 0,
            MaxLOD = float.MaxValue,
            MaxAnisotropy = 8
        });

        _rasterizerState = _device.CreateRasterizerState(new RasterizerDescription
        {
            CullMode = CullMode.Front,
            FillMode = FillMode.Solid,
            DepthClipEnable = true,
            FrontCounterClockwise = false,
            MultisampleEnable = false,
            ScissorEnable = false,
            AntialiasedLineEnable = false,
            DepthBias = 0,
            DepthBiasClamp = 0,
            SlopeScaledDepthBias = 0
        });
        
        _cubeTextureView = LoadTextureView("world.png");
        _floorTextureView = LoadTextureView("flooring-xz-plane.png");
    }

    private ID3D11ShaderResourceView LoadTextureView(string fileName)
    {
        if (_device is null)
        {
            throw new InvalidOperationException("Direct3D device is not initialized.");
        }

        string? texturePath = ResolveTexturePath(fileName);
        if (texturePath is not null)
        {
            try
            {
                return CreateTextureViewFromFile(texturePath);
            }
            catch
            {
                // Fall back to a generated texture if the file cannot be loaded.
            }
        }

        return CreateFallbackTextureView();
    }

    private ID3D11ShaderResourceView CreateTextureViewFromFile(string texturePath)
    {
        if (_device is null)
        {
            throw new InvalidOperationException("Direct3D device is not initialized.");
        }

        BitmapSource source = LoadBitmapSource(texturePath);
        int width = source.PixelWidth;
        int height = source.PixelHeight;
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        source.CopyPixels(pixels, stride, 0);

        return CreateTextureViewFromPixels(pixels, width, height, stride);
    }

    private static BitmapSource LoadBitmapSource(string texturePath)
    {
        BitmapImage bitmap = new();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(texturePath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();

        if (bitmap.Format == PixelFormats.Bgra32)
        {
            return bitmap;
        }

        FormatConvertedBitmap converted = new(bitmap, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private ID3D11ShaderResourceView CreateFallbackTextureView()
    {
        const int width = 256;
        const int height = 256;
        const int stride = width * 4;
        byte[] pixels = new byte[height * stride];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool checker = ((x / 32) + (y / 32)) % 2 == 0;
                int index = y * stride + x * 4;
                pixels[index + 0] = checker ? (byte)0xEE : (byte)0x44;
                pixels[index + 1] = checker ? (byte)0xAA : (byte)0x66;
                pixels[index + 2] = checker ? (byte)0x22 : (byte)0x99;
                pixels[index + 3] = 0xFF;
            }
        }

        return CreateTextureViewFromPixels(pixels, width, height, stride);
    }

    private ID3D11ShaderResourceView CreateTextureViewFromPixels(
        byte[] pixels, int width, int height, int stride)
    {
        if (_device is null || _context is null)
        {
            throw new InvalidOperationException("Direct3D device is not initialized.");
        }

        Texture2DDescription textureDescription = new()
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 0,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.GenerateMips
        };

        using ID3D11Texture2D texture = _device.CreateTexture2D(textureDescription);
        ID3D11ShaderResourceView view = _device.CreateShaderResourceView(texture);

        unsafe
        {
            fixed (byte* pixelsPointer = pixels)
            {
                _context.UpdateSubresource(texture, 0, null, (IntPtr)pixelsPointer, (uint)stride, 0);
            }
        }

        _context.GenerateMips(view);
        return view;
    }

    private static string? ResolveTexturePath(string fileName)
    {
        string[] candidates =
        {
        Path.Combine(AppContext.BaseDirectory, "Assets", fileName),
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", fileName),
        Path.Combine(Directory.GetCurrentDirectory(), "Assets", fileName),
    };

        return candidates.FirstOrDefault(File.Exists);
    }

    private void UpdateConstantBuffer(Matrix4x4 worldViewProjection, Matrix4x4 world, uint useNormalMap)
    {
        if (_context is null || _constantBuffer is null) return;

        TransformConstants constants = new()
        {
            WorldViewProjection = worldViewProjection,
            World = world,
            LightDirection = new Vector3(1.0f, 1.0f, -1.0f),
            AmbientLight = 0.2f,
            UseNormalMap = useNormalMap, // Pass the toggle here
            Padding = Vector3.Zero       // Just empty bytes to keep the GPU happy
        };

        MappedSubresource mapped = _context.Map(_constantBuffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        Marshal.StructureToPtr(constants, mapped.DataPointer, false);
        _context.Unmap(_constantBuffer, 0);
    }

    private static byte[] LoadRequiredShaderBytecode(string fileName)
    {
        string? shaderPath = ResolveShaderBytecodePath(fileName);
        if (shaderPath is null)
        {
            string expectedPath = Path.Combine(AppContext.BaseDirectory, "Shaders", fileName);
            throw new FileNotFoundException(
                $"Could not locate compiled shader '{fileName}'. Build the project so the " +
                $"precompiled shader is generated at '{expectedPath}'.",
                expectedPath);
        }

        return File.ReadAllBytes(shaderPath);
    }

    private static string? ResolveShaderBytecodePath(string fileName)
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "Shaders", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "Shaders", fileName),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Shaders", fileName),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static CubeGeometry CreateCubeGeometry()
    {
        const float h = 1.6f;

        // Define which way each face points
        Vector3 nFront = new Vector3(0, 0, -1);
        Vector3 nBack = new Vector3(0, 0, 1);
        Vector3 nLeft = new Vector3(-1, 0, 0);
        Vector3 nRight = new Vector3(1, 0, 0);
        Vector3 nTop = new Vector3(0, 1, 0);
        Vector3 nBottom = new Vector3(0, -1, 0);

        // (Keep your nFront, nBack, etc. definitions)
        Vector3 tFront = new Vector3(1, 0, 0);
        Vector3 tBack = new Vector3(-1, 0, 0);
        Vector3 tLeft = new Vector3(0, 0, -1);
        Vector3 tRight = new Vector3(0, 0, 1);
        Vector3 tTop = new Vector3(1, 0, 0);
        Vector3 tBottom = new Vector3(1, 0, 0);

        Vertex[] vertices =
        {
            // Front
            new(new Vector3(-h, -h, -h), nFront, tFront, new Vector2(0, 1)),
            new(new Vector3(-h,  h, -h), nFront, tFront, new Vector2(0, 0)),
            new(new Vector3( h,  h, -h), nFront, tFront, new Vector2(1, 0)),
            new(new Vector3( h, -h, -h), nFront, tFront, new Vector2(1, 1)),
            // Back
            new(new Vector3( h, -h,  h), nBack, tBack, new Vector2(0, 1)),
            new(new Vector3( h,  h,  h), nBack, tBack, new Vector2(0, 0)),
            new(new Vector3(-h,  h,  h), nBack, tBack, new Vector2(1, 0)),
            new(new Vector3(-h, -h,  h), nBack, tBack, new Vector2(1, 1)),
            // Left
            new(new Vector3(-h, -h,  h), nLeft, tLeft, new Vector2(0, 1)),
            new(new Vector3(-h,  h,  h), nLeft, tLeft, new Vector2(0, 0)),
            new(new Vector3(-h,  h, -h), nLeft, tLeft, new Vector2(1, 0)),
            new(new Vector3(-h, -h, -h), nLeft, tLeft, new Vector2(1, 1)),
            // Right
            new(new Vector3( h, -h, -h), nRight, tRight, new Vector2(0, 1)),
            new(new Vector3( h,  h, -h), nRight, tRight, new Vector2(0, 0)),
            new(new Vector3( h,  h,  h), nRight, tRight, new Vector2(1, 0)),
            new(new Vector3( h, -h,  h), nRight, tRight, new Vector2(1, 1)),
            // Top
            new(new Vector3(-h,  h, -h), nTop, tTop, new Vector2(0, 1)),
            new(new Vector3(-h,  h,  h), nTop, tTop, new Vector2(0, 0)),
            new(new Vector3( h,  h,  h), nTop, tTop, new Vector2(1, 0)),
            new(new Vector3( h,  h, -h), nTop, tTop, new Vector2(1, 1)),
            // Bottom
            new(new Vector3(-h, -h,  h), nBottom, tBottom, new Vector2(0, 1)),
            new(new Vector3(-h, -h, -h), nBottom, tBottom, new Vector2(0, 0)),
            new(new Vector3( h, -h, -h), nBottom, tBottom, new Vector2(1, 0)),
            new(new Vector3( h, -h,  h), nBottom, tBottom, new Vector2(1, 1)),
        };

        ushort[] indices =
        {
         0,  1,  2,  0,  2,  3,
         4,  5,  6,  4,  6,  7,
         8,  9, 10,  8, 10, 11,
        12, 13, 14, 12, 14, 15,
        16, 17, 18, 16, 18, 19,
        20, 21, 22, 20, 22, 23,
    };

        return new CubeGeometry(vertices, indices);
    }
    private static (Vertex[] Vertices, ushort[] Indices) CreatePlaneGeometry()
    {
        const float size = 10.0f;
        const float y = -2.0f;

        Vector3 normal = new Vector3(0, 1, 0); // Pointing Up
        Vector3 tangent = new Vector3(1, 0, 0); // Pointing Right (Along the X axis)

        const float t = 5.0f; // Tile 5 times

        Vertex[] vertices =
        {
        new(new Vector3(-size, y,  size), normal, tangent, new Vector2(0, 0)),
        new(new Vector3( size, y,  size), normal, tangent, new Vector2(t, 0)),
        new(new Vector3( size, y, -size), normal, tangent, new Vector2(t, t)),
        new(new Vector3(-size, y, -size), normal, tangent, new Vector2(0, t)),
    };

        ushort[] indices = { 0, 1, 2, 0, 2, 3 };
        return (vertices, indices);
    }
    private void ReleaseTargetResources()
    {
        _depthStencilView?.Dispose();
        _depthStencilBuffer?.Dispose();
        _renderTargetView?.Dispose();
        _depthStencilView = null;
        _depthStencilBuffer = null;
        _renderTargetView = null;
    }

    public void Dispose()
    {
        _overlay?.Dispose();
        _overlay = null;
        _cubeTextureView?.Dispose();
        _floorTextureView?.Dispose();
        _floorVertexBuffer?.Dispose();
        _floorIndexBuffer?.Dispose();
        _rasterizerState?.Dispose();
        _samplerState?.Dispose();
        _constantBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _vertexBuffer?.Dispose();
        _inputLayout?.Dispose();
        _vertexShader?.Dispose();
        _pixelShader?.Dispose();
        ReleaseTargetResources();
        _swapChain?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
        _cubeTextureView = null;
        _floorTextureView = null;
        _floorVertexBuffer = null;
        _floorIndexBuffer = null;
        _rasterizerState = null;
        _samplerState = null;
        _constantBuffer = null;
        _indexBuffer = null;
        _vertexBuffer = null;
        _inputLayout = null;
        _vertexShader = null;
        _pixelShader = null;
        _swapChain = null;
        _context = null;
        _device = null;
        GC.SuppressFinalize(this);
    }

    private readonly record struct CubeGeometry(Vertex[] Vertices, ushort[] Indices);

    [StructLayout(LayoutKind.Sequential)]
    private struct TransformConstants
    {
        public Matrix4x4 WorldViewProjection;
        public Matrix4x4 World;
        public Vector3 LightDirection;
        public float AmbientLight;
        public uint UseNormalMap; // 1 = Floor (Atlas/Normals), 0 = Cube (Standard)
        public Vector3 Padding;   // Required for 16-byte HLSL alignment
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Vertex(Vector3 Position, Vector3 Normal, Vector3 Tangent, Vector2 TexCoord)
    {
        public static readonly int SizeInBytes = Marshal.SizeOf<Vertex>();
    }
}