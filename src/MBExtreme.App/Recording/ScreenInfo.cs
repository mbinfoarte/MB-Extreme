using System.Runtime.InteropServices;

namespace MBExtreme.UI.Recording;

internal static class ScreenInfo
{
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    public static (int Width, int Height) Primary => (GetSystemMetrics(0), GetSystemMetrics(1));
}
