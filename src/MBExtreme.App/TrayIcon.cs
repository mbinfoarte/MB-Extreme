using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace MBExtreme.UI;

/// <summary>
/// Ícone do MB Extreme na bandeja (ao lado do relógio), com menu: Abrir, Mostrar/ocultar o widget e Sair.
/// Usa as chamadas do Windows (Shell_NotifyIcon) para não depender de biblioteca externa. Precisa da janela principal já criada.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const int WmTray = 0x0400 + 1;   // WM_APP+1
    private const int NimAdd = 0, NimModify = 1, NimDelete = 2;
    private const int NifMessage = 0x01, NifIcon = 0x02, NifTip = 0x04;
    private const int WmLButtonUp = 0x0202, WmLButtonDblClk = 0x0203, WmRButtonUp = 0x0205, WmContextMenu = 0x007B;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int message, ref NotifyIconData data);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly IntPtr _hwnd;
    private readonly HwndSource? _source;
    private NotifyIconData _data;
    private bool _added;
    private readonly Action _onOpen, _onToggleWidget, _onExit;
    private readonly Func<bool> _widgetOn;

    public TrayIcon(Window owner, Action onOpen, Action onToggleWidget, Func<bool> widgetOn, Action onExit)
    {
        _onOpen = onOpen; _onToggleWidget = onToggleWidget; _widgetOn = widgetOn; _onExit = onExit;
        _hwnd = new WindowInteropHelper(owner).EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);

        _data = new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = WmTray,
            hIcon = LoadAppIcon(),
            szTip = "MB Extreme",
        };
        _added = Shell_NotifyIcon(NimAdd, ref _data);
    }

    private static IntPtr LoadAppIcon()
    {
        try
        {
            string exe = Environment.ProcessPath ?? System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "";
            if (exe.Length > 0 && System.Drawing.Icon.ExtractAssociatedIcon(exe) is { } icon) return icon.Handle;
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.FileNotFoundException or System.ComponentModel.Win32Exception) { }
        return IntPtr.Zero;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmTray) return IntPtr.Zero;
        int mouse = lParam.ToInt32();
        if (mouse is WmLButtonUp or WmLButtonDblClk) { _onOpen(); handled = true; }
        else if (mouse is WmRButtonUp or WmContextMenu) { ShowMenu(); handled = true; }
        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        SetForegroundWindow(_hwnd);   // sem isto o menu não fecha ao clicar fora
        var menu = new ContextMenu();
        menu.Items.Add(Item("Abrir o MB Extreme", _onOpen, bold: true));
        menu.Items.Add(Item(_widgetOn() ? "Ocultar o widget" : "Mostrar o widget", _onToggleWidget));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Sair", _onExit));
        menu.Placement = PlacementMode.MousePoint;
        menu.StaysOpen = false;
        menu.IsOpen = true;
    }

    private static MenuItem Item(string text, Action click, bool bold = false)
    {
        var item = new MenuItem { Header = text };
        if (bold) item.FontWeight = FontWeights.SemiBold;
        item.Click += (_, _) => click();
        return item;
    }

    public void Dispose()
    {
        if (_added) { Shell_NotifyIcon(NimDelete, ref _data); _added = false; }
        _source?.RemoveHook(WndProc);
    }
}
