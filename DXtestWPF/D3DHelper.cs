using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace DXtestWPF;

/// <summary>
/// Shared Direct3D utilities used by both D3D11Renderer and HardwareInfoService.
/// </summary>
internal static class D3DHelper
{
    /// <summary>
    /// Feature levels tried in order for both the renderer and the diagnostics probe.
    /// </summary>
    public static readonly FeatureLevel[] FeatureLevels =
    {
        FeatureLevel.Level_11_1,
        FeatureLevel.Level_11_0,
        FeatureLevel.Level_10_1,
        FeatureLevel.Level_10_0,
        FeatureLevel.Level_9_3,
        FeatureLevel.Level_9_2,
        FeatureLevel.Level_9_1,
    };

    /// <summary>
    /// Returns an estimated shader model string for a given feature level.
    /// This is an approximation based on feature level, not a full HLSL compiler test.
    /// </summary>
    public static string EstimatedShaderModel(FeatureLevel featureLevel) => featureLevel switch
    {
        FeatureLevel.Level_11_1 => "5.0",
        FeatureLevel.Level_11_0 => "5.0",
        FeatureLevel.Level_10_1 => "4.1",
        FeatureLevel.Level_10_0 => "4.0",
        FeatureLevel.Level_9_3 => "3.0",
        FeatureLevel.Level_9_2 => "2.0",
        FeatureLevel.Level_9_1 => "2.0",
        _ => "Unknown",
    };

    /// <summary>
    /// Tries to create a D3D11 device with the given driver type.
    /// Returns true and sets featureLevel on success, false otherwise.
    /// </summary>
    public static bool TryCreateDevice(DriverType driverType, out FeatureLevel featureLevel)
    {
        ID3D11Device? device = null;
        ID3D11DeviceContext? context = null;

        try
        {
            D3D11.D3D11CreateDevice(
                null,
                driverType,
                DeviceCreationFlags.BgraSupport,
                FeatureLevels,
                out device,
                out featureLevel,
                out context).CheckError();

            return true;
        }
        catch
        {
            featureLevel = default;
            return false;
        }
        finally
        {
            context?.Dispose();
            device?.Dispose();
        }
    }
}