using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MBExtreme.UI;

/// <summary>
/// Janela sem a moldura do Windows: maximizada, ela ocupava a tela toda e ficava atrás da barra de tarefas (a parte de baixo sumia).
/// Aqui o Windows recebe o tamanho certo para "maximizado": a área de trabalho do monitor onde a janela está, sem a barra de tarefas
/// (em qualquer posição da barra, com escala de tela e com vários monitores).
/// </summary>
internal static class WorkArea
{
    private const int WM_GETMINMAXINFO = 0x0024;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO { public POINT Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            HwndSource.FromHwnd(handle)?.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (msg == WM_GETMINMAXINFO && Fix(hwnd, lParam, window)) handled = true;
                return IntPtr.Zero;
            });
        };
    }

    private static bool Fix(IntPtr hwnd, IntPtr lParam, Window window)
    {
        var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref mi)) return false;
        info.MaxPosition.X = mi.Work.Left - mi.Monitor.Left;
        info.MaxPosition.Y = mi.Work.Top - mi.Monitor.Top;
        info.MaxSize.X = mi.Work.Right - mi.Work.Left;
        info.MaxSize.Y = mi.Work.Bottom - mi.Work.Top;
        // o tamanho mínimo da janela em pixels de verdade (escala da tela)
        var source = PresentationSource.FromVisual(window);
        double sx = source?.CompositionTarget?.TransformToDevice.M11 ?? 1, sy = source?.CompositionTarget?.TransformToDevice.M22 ?? 1;
        info.MinTrackSize.X = (int)(window.MinWidth * sx);
        info.MinTrackSize.Y = (int)(window.MinHeight * sy);
        Marshal.StructureToPtr(info, lParam, true);
        return true;
    }
}
