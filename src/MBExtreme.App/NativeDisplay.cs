using System.Runtime.InteropServices;

namespace MBExtreme.UI;

/// <summary>Leituras do Windows para o benchmark: maior resolução que o monitor principal aceita e se o PC está na bateria.</summary>
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
