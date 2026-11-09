using System.Runtime.InteropServices;
using MBExtreme.Core.Remote;

namespace MBExtreme.UI.Remote;

/// <summary>Aplica o mouse e o teclado do técnico com SendInput. Só funciona nas janelas do usuário (não eleva UAC nem controla a tela de login).</summary>
internal sealed class InputInjector : IInputSink
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyInput { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput mi;
        [FieldOffset(0)] public KeyInput ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint type; public InputUnion u; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    private const uint InputMouse = 0, InputKeyboard = 1;
    private const uint MouseMove = 0x1, LeftDown = 0x2, LeftUp = 0x4, RightDown = 0x8, RightUp = 0x10, MiddleDown = 0x20, MiddleUp = 0x40, Wheel = 0x800, Absolute = 0x8000;
    private const uint KeyExtended = 0x1, KeyUp = 0x2;

    public void Mouse(MouseMessage m, int screenWidth, int screenHeight)
    {
        // a posição já vem normalizada de 0 a 65535, o formato que o Windows usa em coordenadas absolutas
        uint flags = Absolute | MouseMove;
        uint data = 0;
        if (m.Action == MouseAction.Down || m.Action == MouseAction.Up)
        {
            bool down = m.Action == MouseAction.Down;
            flags |= m.Button switch
            {
                MouseButton.Left => down ? LeftDown : LeftUp,
                MouseButton.Right => down ? RightDown : RightUp,
                MouseButton.Middle => down ? MiddleDown : MiddleUp,
                _ => 0,
            };
        }
        else if (m.Action == MouseAction.Wheel)
        {
            flags |= Wheel;
            data = (uint)m.Wheel;
        }

        var input = new Input { type = InputMouse, u = new InputUnion { mi = new MouseInput { dx = m.X, dy = m.Y, mouseData = data, dwFlags = flags } } };
        SendInput(1, new[] { input }, Marshal.SizeOf<Input>());
    }

    public void Key(KeyMessage k)
    {
        uint flags = (k.Down ? 0u : KeyUp) | (k.Extended ? KeyExtended : 0u);
        ushort scan = (ushort)MapVirtualKey((uint)k.VirtualKey, 0);
        var input = new Input { type = InputKeyboard, u = new InputUnion { ki = new KeyInput { wVk = (ushort)k.VirtualKey, wScan = scan, dwFlags = flags } } };
        SendInput(1, new[] { input }, Marshal.SizeOf<Input>());
    }
}
