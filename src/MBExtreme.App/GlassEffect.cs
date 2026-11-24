using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace MBExtreme.UI;

/// <summary>
/// Efeito cristal (lembra o Aero do Windows Vista): o que está atrás da janela aparece desfocado através do fundo
/// cinza-escuro levemente transparente. Usa o desfoque do próprio Windows (DWM). Os cantos arredondados são recortados
/// com uma região para o desfoque não sair quadrado. Desliga sozinho quando o Windows está com "Efeitos de transparência"
/// desligado, e pode ser desligado em Acessibilidade.
/// </summary>
internal static class GlassEffect
{
    private enum AccentState { Disabled = 0, BlurBehind = 3 }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy { public AccentState State; public int Flags; public uint GradientColor; public int AnimationId; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData { public int Attribute; public IntPtr Data; public int SizeOfData; }

    private const int WCA_ACCENT_POLICY = 19;

    [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

    /// <summary>O Windows está com "Efeitos de transparência" ligado (Configurações, Personalização, Cores)?</summary>
    public static bool SystemAllows()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is not int value || value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    public static bool Active { get; private set; }

    /// <summary>Liga ou desliga o desfoque e troca o fundo da janela (transparente com desfoque, ou opaco).</summary>
    public static void Apply(Window window, FrameworkElement frame, bool wanted)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        bool on = wanted && SystemAllows() && Environment.OSVersion.Version.Build >= 17134;
        bool ok = SetAccent(handle, on ? AccentState.BlurBehind : AccentState.Disabled);
        Active = on && ok;
        if (frame is System.Windows.Controls.Border border && window.TryFindResource(Active ? "WindowGlassBrush" : "WindowSolidBrush") is Brush brush)
            border.Background = brush;
        UpdateRegion(window, frame);
    }

    /// <summary>Recorta a janela nos cantos arredondados (sem recorte quando maximizada ou sem o desfoque).</summary>
    public static void UpdateRegion(Window window, FrameworkElement frame)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        try
        {
            if (!Active || window.WindowState == WindowState.Maximized)
            {
                SetWindowRgn(handle, IntPtr.Zero, true);
                return;
            }
            var source = PresentationSource.FromVisual(window);
            Matrix toDevice = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            int width = (int)Math.Ceiling(window.ActualWidth * toDevice.M11);
            int height = (int)Math.Ceiling(window.ActualHeight * toDevice.M22);
            if (width < 50 || height < 50) return; // ainda sem tamanho (antes do primeiro layout): o SizeChanged acerta depois
            double radius = frame is System.Windows.Controls.Border b ? b.CornerRadius.TopLeft : 0;
            int diameter = (int)Math.Round(radius * 2 * toDevice.M11);
            var region = diameter > 0 ? CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter) : CreateRectRgn(0, 0, width, height);
            if (region != IntPtr.Zero) SetWindowRgn(handle, region, true); // a janela passa a ser dona da região
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    private static bool SetAccent(IntPtr handle, AccentState state)
    {
        try
        {
            var accent = new AccentPolicy { State = state };
            int size = Marshal.SizeOf<AccentPolicy>();
            IntPtr pointer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, pointer, false);
                var data = new WindowCompositionAttributeData { Attribute = WCA_ACCENT_POLICY, Data = pointer, SizeOfData = size };
                return SetWindowCompositionAttribute(handle, ref data) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }
}
