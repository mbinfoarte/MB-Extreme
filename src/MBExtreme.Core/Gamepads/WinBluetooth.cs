using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MBExtreme.Core.Gamepads;

/// <summary>Desliga um controle ligado pelo Bluetooth (o controle apaga e pode ser religado pelo botão PS).</summary>
[SupportedOSPlatform("windows")]
public static class WinBluetooth
{
    private const uint IOCTL_BTH_DISCONNECT_DEVICE = 0x41000C;

    [StructLayout(LayoutKind.Sequential)]
    private struct FindRadioParams { public int Size; }

    [DllImport("bthprops.cpl", SetLastError = true)] private static extern IntPtr BluetoothFindFirstRadio(ref FindRadioParams p, out IntPtr radio);
    [DllImport("bthprops.cpl", SetLastError = true)] private static extern bool BluetoothFindNextRadio(IntPtr find, out IntPtr radio);
    [DllImport("bthprops.cpl", SetLastError = true)] private static extern bool BluetoothFindRadioClose(IntPtr find);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr device, uint code, ref ulong input, int inputSize, IntPtr output, int outputSize, out int returned, IntPtr overlapped);

    /// <summary>true se algum rádio Bluetooth do PC aceitou desligar o aparelho com este endereço.</summary>
    public static bool Disconnect(string? mac)
    {
        ulong address = PadDetails.ParseMac(mac);
        if (address == 0) return false;
        var p = new FindRadioParams { Size = Marshal.SizeOf<FindRadioParams>() };
        IntPtr find;
        try { find = BluetoothFindFirstRadio(ref p, out IntPtr radio0); if (find == IntPtr.Zero) return false; if (Try(radio0, address)) { BluetoothFindRadioClose(find); return true; } }
        catch (DllNotFoundException) { return false; }
        try
        {
            while (BluetoothFindNextRadio(find, out IntPtr radio))
                if (Try(radio, address)) return true;
            return false;
        }
        finally
        {
            BluetoothFindRadioClose(find);
        }
    }

    private static bool Try(IntPtr radio, ulong address)
    {
        try { return DeviceIoControl(radio, IOCTL_BTH_DISCONNECT_DEVICE, ref address, sizeof(ulong), IntPtr.Zero, 0, out _, IntPtr.Zero); }
        finally { CloseHandle(radio); }
    }
}
