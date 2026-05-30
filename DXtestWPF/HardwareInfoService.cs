using System.Runtime.InteropServices;
using System.Windows.Forms;
using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace DXtestWPF;

internal static class HardwareInfoService
{
    public static IReadOnlyList<string> GetSummaryLines()
    {
        var lines = new List<string>
        {
            $"Computer name: {Environment.MachineName}",
            $"OS: {Environment.OSVersion}",
            $"Framework: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}",
            $"Process architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}",
            $"OS architecture: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}",
            $"64-bit process: {Environment.Is64BitProcess}",
            $"64-bit OS: {Environment.Is64BitOperatingSystem}",
            $"Logical processors: {Environment.ProcessorCount}",
            $"Memory: {GetMemoryStatus()}",
            $"Monitors detected: {Screen.AllScreens.Length}",
            string.Empty,
            "Display adapters:",
        };

        lines.AddRange(GetDisplayAdapterLines().Select(x => $"  {x}"));
        return lines;
    }

    public static IReadOnlyList<string> GetDisplayModeLines()
    {
        var lines = new List<string>();

        foreach (var screen in Screen.AllScreens)
        {
            lines.Add($"{screen.DeviceName}  current: {screen.Bounds.Width}x{screen.Bounds.Height} @ {screen.BitsPerPixel} bpp  primary={screen.Primary}");
        }

        return lines;
    }

    public static IReadOnlyList<string> GetDirect3DSummaryLines()
    {
        var probe = ProbeDirect3D();

        return new List<string>
        {
            $"Direct3D device path: {(probe.UsedWarpFallback ? "WARP fallback" : "Hardware")}",
            $"Feature level: {probe.FeatureLevel}",
            $"Estimated shader model: {D3DHelper.EstimatedShaderModel(probe.FeatureLevel)}",
            "Shader model estimate is based on feature level, not a full HLSL compiler test.",
        };
    }

    private static IEnumerable<string> GetDisplayAdapterLines()
    {
        for (uint adapterIndex = 0; ; adapterIndex++)
        {
            var device = new DISPLAY_DEVICE
            {
                cb = Marshal.SizeOf<DISPLAY_DEVICE>()
            };

            if (!EnumDisplayDevices(null, adapterIndex, ref device, 0))
            {
                yield break;
            }

            yield return $"{device.DeviceString}  [{device.DeviceName}]  flags={device.StateFlags}";
        }
    }

    private static string GetMemoryStatus()
    {
        var memory = new MEMORYSTATUSEX
        {
            dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
        };

        if (!GlobalMemoryStatusEx(ref memory))
        {
            return "Unavailable";
        }

        var totalGb = memory.ullTotalPhys / 1024d / 1024d / 1024d;
        var availGb = memory.ullAvailPhys / 1024d / 1024d / 1024d;
        return $"{totalGb:0.0} GB total, {availGb:0.0} GB available";
    }

    private static Direct3DProbeResult ProbeDirect3D()
    {
        if (D3DHelper.TryCreateDevice(DriverType.Hardware, out var featureLevel))
        {
            return new Direct3DProbeResult(featureLevel, UsedWarpFallback: false);
        }

        if (D3DHelper.TryCreateDevice(DriverType.Warp, out featureLevel))
        {
            return new Direct3DProbeResult(featureLevel, UsedWarpFallback: true);
        }

        return new Direct3DProbeResult(FeatureLevel.Level_9_1, UsedWarpFallback: true);
    }

    private readonly record struct Direct3DProbeResult(FeatureLevel FeatureLevel, bool UsedWarpFallback);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public DisplayDeviceStateFlags StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [Flags]
    private enum DisplayDeviceStateFlags : int
    {
        None = 0,
        Active = 0x1,
        PrimaryDevice = 0x4,
        MirroringDriver = 0x8,
        ModesPruned = 0x08000000,
        Remote = 0x04000000,
        Disconnected = 0x02000000,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}