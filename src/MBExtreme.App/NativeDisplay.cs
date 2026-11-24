using System.Runtime.InteropServices;

namespace MBExtreme.UI;

/// <summary>Leituras do Windows: resoluções e taxas (Hz) dos monitores, troca de Hz e se o PC está na bateria.</summary>
internal static class NativeDisplay
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string? deviceName, ref DEVMODE devMode, IntPtr hwnd, int flags, IntPtr param);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? device, int devNum, ref DISPLAY_DEVICE displayDevice, int flags);

    private const int AttachedToDesktop = 0x1, PrimaryDevice = 0x4;
    private const int DmDisplayFrequency = 0x400000;
    private const int CdsUpdateRegistry = 0x1, CdsTest = 0x2;

    /// <summary>Monitores ligados, com a taxa atual e as que aceitam na resolução de agora.</summary>
    public static IReadOnlyList<MBExtreme.Core.Display.MonitorRates> Monitors()
    {
        var list = new List<MBExtreme.Core.Display.MonitorRates>();
        try
        {
            for (int i = 0; i < 16; i++)
            {
                var dev = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
                if (!EnumDisplayDevices(null, i, ref dev, 0)) break;
                if ((dev.StateFlags & AttachedToDesktop) == 0) continue;
                var cur = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                if (!EnumDisplaySettings(dev.DeviceName, CurrentSettings, ref cur)) continue;
                var modes = new List<MBExtreme.Core.Display.DisplayMode>();
                for (int m = 0; m < 4000; m++)
                {
                    var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                    if (!EnumDisplaySettings(dev.DeviceName, m, ref mode)) break;
                    modes.Add(new(mode.dmPelsWidth, mode.dmPelsHeight, mode.dmDisplayFrequency));
                }
                // nome amigável do monitor (o do adaptador é a placa de vídeo)
                var mon = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
                string name = EnumDisplayDevices(dev.DeviceName, 0, ref mon, 0) && !string.IsNullOrWhiteSpace(mon.DeviceString) ? mon.DeviceString : dev.DeviceName;
                list.Add(new(dev.DeviceName, name, (dev.StateFlags & PrimaryDevice) != 0, cur.dmPelsWidth, cur.dmPelsHeight, cur.dmDisplayFrequency,
                    MBExtreme.Core.Display.RefreshRates.For(modes, cur.dmPelsWidth, cur.dmPelsHeight)));
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        return list.OrderByDescending(m => m.Primary).ToList();
    }

    /// <summary>Taxa do monitor principal (60 se não der para ler).</summary>
    public static int PrimaryHz() => Monitors().FirstOrDefault(m => m.Primary)?.CurrentHz is int hz and > 0 ? hz : 60;

    /// <summary>Troca a taxa de atualização de um monitor (mesma resolução). Devolve o código do Windows (0 = deu certo).</summary>
    public static int SetHz(string deviceName, int hz)
    {
        var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettings(deviceName, CurrentSettings, ref mode)) return -1;
        mode.dmDisplayFrequency = hz;
        mode.dmFields = DmDisplayFrequency;
        int test = ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, CdsTest, IntPtr.Zero);
        if (test != 0) return test;
        return ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, CdsUpdateRegistry, IntPtr.Zero);
    }

    private const int CurrentSettings = -1;

    /// <summary>A maior resolução da lista de modos do monitor principal (o que o driver do monitor informa) e a resolução atual.</summary>
    public static (int Width, int Height, int CurrentWidth, int CurrentHeight) PrimaryMonitor()
    {
        int maxW = 0, maxH = 0, curW = 0, curH = 0;
        try
        {
            var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettings(null, CurrentSettings, ref mode)) { curW = mode.dmPelsWidth; curH = mode.dmPelsHeight; }
            for (int i = 0; i < 2000; i++)
            {
                mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                if (!EnumDisplaySettings(null, i, ref mode)) break;
                if ((long)mode.dmPelsWidth * mode.dmPelsHeight > (long)maxW * maxH) { maxW = mode.dmPelsWidth; maxH = mode.dmPelsHeight; }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }

        if (maxW == 0) { maxW = curW; maxH = curH; }
        return (maxW, maxH, curW, curH);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    /// <summary>true na bateria, false na tomada, null quando o Windows não sabe (ou é um PC sem bateria).</summary>
    public static bool? OnBattery()
    {
        try
        {
            if (!GetSystemPowerStatus(out var s)) return null;
            if (s.BatteryFlag == 128) return false; // sem bateria
            return s.ACLineStatus switch { 0 => true, 1 => false, _ => null };
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }
}
