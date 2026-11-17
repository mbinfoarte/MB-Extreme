using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace MBExtreme.Core.Recording;

public sealed record DisplayInfo(int Index, string DeviceName, PixelRect Bounds, bool Primary)
{
    public string Title => $"Tela {Index + 1}{(Primary ? " (principal)" : "")} · {Bounds.Width}×{Bounds.Height}";
}

public sealed record WindowInfo(
    IntPtr Handle, string Title, string ProcessName, string ClassName, PixelRect Bounds,
    bool Visible, bool Cloaked, bool Minimized, bool ToolWindow, bool HasOwner, int ProcessId)
{
    public string DisplayName => string.IsNullOrEmpty(ProcessName) ? Title : $"{Title}  —  {ProcessName}";
}

/// <summary>Regras para decidir quais janelas aparecem na lista de captura e onde elas ficam na tela.</summary>
public static class CaptureRules
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow", "NotifyIconOverflowWindow",
    };

    public static bool IsCapturable(WindowInfo w, int ownProcessId) =>
        w.Visible && !w.Cloaked && !w.Minimized && !w.ToolWindow && !w.HasOwner
        && w.ProcessId != ownProcessId
        && !string.IsNullOrWhiteSpace(w.Title)
        && w.Bounds.Width >= 160 && w.Bounds.Height >= 120
        && !ShellClasses.Contains(w.ClassName);

    /// <summary>A janela cobre (quase) a tela inteira: jogo em tela cheia ou sem borda.</summary>
    public static bool CoversDisplay(PixelRect window, PixelRect display, int tolerance = 8) =>
        Math.Abs(window.X - display.X) <= tolerance && Math.Abs(window.Y - display.Y) <= tolerance
        && Math.Abs(window.Width - display.Width) <= tolerance * 2 && Math.Abs(window.Height - display.Height) <= tolerance * 2;

    /// <summary>
    /// Tela que contém o centro da janela e a área da janela dentro dela (coordenadas relativas à tela, com medidas pares),
    /// recortada para não passar das bordas.
    /// </summary>
    public static (DisplayInfo Display, PixelRect Region)? RegionOnDisplay(PixelRect window, IReadOnlyList<DisplayInfo> displays)
    {
        if (displays.Count == 0) return null;
        int cx = window.X + window.Width / 2, cy = window.Y + window.Height / 2;
        var display = displays.FirstOrDefault(d => cx >= d.Bounds.X && cx < d.Bounds.Right && cy >= d.Bounds.Y && cy < d.Bounds.Bottom)
                      ?? displays.OrderByDescending(d => Overlap(window, d.Bounds)).First();

        int left = Math.Max(window.X, display.Bounds.X), top = Math.Max(window.Y, display.Bounds.Y);
        int right = Math.Min(window.Right, display.Bounds.Right), bottom = Math.Min(window.Bottom, display.Bounds.Bottom);
        int width = right - left, height = bottom - top;
        if (width < 16 || height < 16) return null;

        int x = left - display.Bounds.X, y = top - display.Bounds.Y;
        x -= x % 2;
        y -= y % 2;
        width -= width % 2;
        height -= height % 2;
        return (display, new PixelRect(x, y, width, height));
    }

    private static long Overlap(PixelRect a, PixelRect b)
    {
        long w = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
        long h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
        return w > 0 && h > 0 ? w * h : 0;
    }

    /// <summary>Monta a origem de captura para uma tela inteira.</summary>
    public static CaptureSource ForDisplay(DisplayInfo d) =>
        new(SourceKind.Monitor, d.Index, new PixelRect(0, 0, d.Bounds.Width, d.Bounds.Height), d.Title);

    /// <summary>Monta a origem de captura para uma janela, escolhendo o melhor jeito.</summary>
    public static CaptureSource? ForWindow(WindowInfo w, IReadOnlyList<DisplayInfo> displays, bool gdi)
    {
        if (gdi)
            return new CaptureSource(SourceKind.WindowGdi, 0, new PixelRect(0, 0, w.Bounds.Width - w.Bounds.Width % 2, w.Bounds.Height - w.Bounds.Height % 2),
                "Janela (GDI): " + w.Title, w.Title);

        var placed = RegionOnDisplay(w.Bounds, displays);
        if (placed is null) return null;
        var (display, region) = placed.Value;
        return CoversDisplay(w.Bounds, display.Bounds)
            ? new CaptureSource(SourceKind.Monitor, display.Index, new PixelRect(0, 0, display.Bounds.Width, display.Bounds.Height), "Janela em tela cheia: " + w.Title, w.Title)
            : new CaptureSource(SourceKind.WindowRegion, display.Index, region, "Janela: " + w.Title, w.Title);
    }
}

/// <summary>Lista as telas na mesma ordem que o FFmpeg (DXGI, primeira placa de vídeo) e as janelas abertas.</summary>
[SupportedOSPlatform("windows")]
public static class CaptureCatalog
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetWindowTextLengthW(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    private const int GwOwner = 4, GwlExStyle = -20, WsExToolWindow = 0x80;
    private const int DwmaCloaked = 14, DwmaExtendedFrameBounds = 9;

    public static IReadOnlyList<DisplayInfo> Displays()
    {
        try
        {
            var list = new List<DisplayInfo>();
            Vortice.DXGI.DXGI.CreateDXGIFactory1(out Vortice.DXGI.IDXGIFactory1? factory);
            using (factory)
            {
                if (factory is null || factory.EnumAdapters1(0, out Vortice.DXGI.IDXGIAdapter1? adapter).Failure || adapter is null)
                    return Array.Empty<DisplayInfo>();
                using (adapter)
                {
                    for (uint i = 0; adapter.EnumOutputs(i, out Vortice.DXGI.IDXGIOutput? output).Success && output is not null; i++)
                    {
                        using (output)
                        {
                            var d = output.Description;
                            var r = d.DesktopCoordinates;
                            var bounds = new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                            list.Add(new DisplayInfo((int)i, d.DeviceName, bounds, bounds.X == 0 && bounds.Y == 0));
                        }
                    }
                }
            }
            return list;
        }
        catch (Exception ex) when (ex is DllNotFoundException or InvalidOperationException or COMException or SEHException)
        {
            return Array.Empty<DisplayInfo>();
        }
    }

    public static IReadOnlyList<WindowInfo> Windows()
    {
        var found = new List<WindowInfo>();
        int own = Environment.ProcessId;
        EnumWindows((hwnd, _) =>
        {
            try
            {
                if (!IsWindowVisible(hwnd) || GetWindowTextLengthW(hwnd) == 0) return true;
                var title = new StringBuilder(GetWindowTextLengthW(hwnd) + 1);
                GetWindowTextW(hwnd, title, title.Capacity);
                var cls = new StringBuilder(256);
                GetClassNameW(hwnd, cls, cls.Capacity);
                GetWindowThreadProcessId(hwnd, out uint pid);

                DwmGetWindowAttribute(hwnd, DwmaCloaked, out int cloaked, sizeof(int));
                Rect rect;
                if (DwmGetWindowAttribute(hwnd, DwmaExtendedFrameBounds, out rect, Marshal.SizeOf<Rect>()) != 0) GetWindowRect(hwnd, out rect);

                string process = "";
                try { process = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch (ArgumentException) { }

                var info = new WindowInfo(hwnd, title.ToString(), process, cls.ToString(),
                    new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top),
                    true, cloaked != 0, IsIconic(hwnd), (GetWindowLong(hwnd, GwlExStyle) & WsExToolWindow) != 0,
                    GetWindow(hwnd, GwOwner) != IntPtr.Zero, (int)pid);
                if (CaptureRules.IsCapturable(info, own)) found.Add(info);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            return true;
        }, IntPtr.Zero);
        return found.OrderBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase).ThenBy(w => w.Title).ToList();
    }
}
