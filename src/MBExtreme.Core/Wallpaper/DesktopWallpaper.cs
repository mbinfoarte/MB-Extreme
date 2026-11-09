using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MBExtreme.Core.Wallpaper;

/// <summary>
/// Aplica o papel de parede no Windows. Usa a interface COM IDesktopWallpaper (Windows 8+), que permite um papel por monitor
/// e o modo de ajuste; se não existir, cai para o modo antigo (SystemParametersInfo), que aplica um papel igual em tudo.
/// </summary>
[SupportedOSPlatform("windows")]
public static class DesktopWallpaper
{
    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
        uint GetMonitorDevicePathCount();
        Rect GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(int position);
        int GetPosition();
        // os demais métodos da interface não são usados
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    private static IDesktopWallpaper? Create()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"));
            return type is null ? null : Activator.CreateInstance(type) as IDesktopWallpaper;
        }
        catch (COMException) { return null; }
        catch (InvalidCastException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    /// <summary>Monitores ligados, com a resolução atual. Vazio se o Windows não oferecer a interface.</summary>
    public static IReadOnlyList<WallpaperMonitor> Monitors()
    {
        var com = Create();
        if (com is null) return Array.Empty<WallpaperMonitor>();
        var list = new List<WallpaperMonitor>();
        try
        {
            uint count = com.GetMonitorDevicePathCount();
            for (uint i = 0; i < count; i++)
            {
                string id = com.GetMonitorDevicePathAt(i);
                if (string.IsNullOrEmpty(id)) continue;   // monitor desligado
                Rect r;
                try { r = com.GetMonitorRECT(id); }
                catch (COMException) { continue; }
                list.Add(new WallpaperMonitor(id, $"Monitor {list.Count + 1}", r.Right - r.Left, r.Bottom - r.Top, r.Left, r.Top, r.Left == 0 && r.Top == 0));
            }
        }
        catch (COMException) { }
        finally { Marshal.FinalReleaseComObject(com); }
        return list;
    }

    /// <summary>Aplica a imagem num monitor (monitorId nulo = todos). Devolve false se o Windows recusou.</summary>
    public static bool Apply(string imagePath, WallpaperFit fit, string? monitorId = null)
    {
        var com = Create();
        if (com is null) return LegacyApply(imagePath, fit);
        try
        {
            com.SetPosition(WallpaperFitText.Position(fit));
            com.SetWallpaper(monitorId, imagePath);
            return true;
        }
        catch (COMException) { return LegacyApply(imagePath, fit); }
        finally { Marshal.FinalReleaseComObject(com); }
    }

    /// <summary>Modo antigo: um papel igual em todos os monitores (Windows sem a interface COM).</summary>
    private static bool LegacyApply(string imagePath, WallpaperFit fit)
    {
        try
        {
            var (style, tile) = WallpaperFitText.LegacyRegistry(fit);
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true);
            key?.SetValue("WallpaperStyle", style);
            key?.SetValue("TileWallpaper", tile);
            return SystemParametersInfo(SpiSetDeskWallpaper, 0, imagePath, SpifUpdate) != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { return false; }
    }

    private const int SpiSetDeskWallpaper = 0x0014;
    private const int SpifUpdate = 0x01 | 0x02;   // SPIF_UPDATEINIFILE | SPIF_SENDCHANGE

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SystemParametersInfo(int action, int param, string value, int flags);
}
