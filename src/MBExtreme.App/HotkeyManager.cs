using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MBExtreme.UI;

/// <summary>Atalhos globais (funcionam com o jogo em primeiro plano). Cada atalho chama uma ação na tela do aplicativo.</summary>
internal sealed class HotkeyManager
{
    private const int WmHotkey = 0x0312;
    private const uint ModControl = 0x2, ModShift = 0x4, ModNoRepeat = 0x4000;
    private const int FirstId = 0x4D60;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    private readonly Dictionary<int, Action> _actions = new();
    private IntPtr _window;
    private HwndSource? _source;
    private int _nextId = FirstId;

    /// <summary>Liga o gerenciador à janela principal (uma vez).</summary>
    public void Attach(Window main)
    {
        if (_window != IntPtr.Zero) return;
        _window = new WindowInteropHelper(main).EnsureHandle();
        _source = HwndSource.FromHwnd(_window);
        _source?.AddHook(Hook);
    }

    /// <summary>Registra Ctrl + Shift + tecla. Retorna o número do atalho, ou 0 se outro programa já usa a combinação.</summary>
    public int RegisterCtrlShift(uint virtualKey, Action action)
    {
        if (_window == IntPtr.Zero) return 0;
        int id = _nextId++;
        if (!RegisterHotKey(_window, id, ModControl | ModShift | ModNoRepeat, virtualKey)) return 0;
        _actions[id] = action;
        return id;
    }

    /// <summary>Registra um atalho do estúdio (Ctrl + Shift ou Ctrl + Alt, com uma tecla F5 a F12). 0 = outro programa já usa a combinação.</summary>
    public int Register(MBExtreme.Core.Recording.HotkeyBinding binding, Action action)
    {
        if (_window == IntPtr.Zero || !binding.IsValid) return 0;
        const uint ModAlt = 0x1;
        uint mods = ModControl | ModNoRepeat | (binding.Modifier == MBExtreme.Core.Recording.HotkeyModifier.CtrlShift ? ModShift : ModAlt);
        int id = _nextId++;
        if (!RegisterHotKey(_window, id, mods, binding.VirtualKey)) return 0;
        _actions[id] = action;
        return id;
    }

    public void Unregister(int id)
    {
        if (id == 0 || _window == IntPtr.Zero) return;
        UnregisterHotKey(_window, id);
        _actions.Remove(id);
    }

    public void Shutdown()
    {
        foreach (int id in _actions.Keys.ToList()) Unregister(id);
        _source?.RemoveHook(Hook);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            action();
        }

        return IntPtr.Zero;
    }
}
