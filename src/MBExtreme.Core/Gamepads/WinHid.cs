using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace MBExtreme.Core.Gamepads;

/// <summary>Um controle encontrado: caminho do dispositivo, modelo e se está no cabo (USB) ou no Bluetooth.</summary>
public sealed record HidPadInfo(string Path, PadModel Model, ushort Product, int InputLength, int FeatureLength)
{
    /// <summary>Pelo cabo os relatórios de entrada têm 64 bytes; pelo Bluetooth são maiores.</summary>
    public bool IsUsb => InputLength == 64;
}

/// <summary>Acesso HID do Windows (hid.dll e setupapi.dll), sem bibliotecas externas.</summary>
[SupportedOSPlatform("windows")]
public static class WinHid
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
            NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices, NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetAttributes(SafeFileHandle device, ref HIDD_ATTRIBUTES attributes);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr data);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, ref HIDP_CAPS caps);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetFeature(SafeFileHandle device, byte[] buffer, int length);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_SetFeature(SafeFileHandle device, byte[] buffer, int length);
    [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_SetNumInputBuffers(SafeFileHandle device, int count);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, int flags);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, int index, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr info);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    private const int DIGCF_PRESENT = 0x2, DIGCF_DEVICEINTERFACE = 0x10;
    private const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;

    private static IEnumerable<string> DevicePaths()
    {
        HidD_GetHidGuid(out Guid guid);
        IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == new IntPtr(-1)) yield break;
        try
        {
            var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data); i++)
            {
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int size, IntPtr.Zero);
                if (size <= 0) continue;
                IntPtr detail = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out _, IntPtr.Zero)) continue;
                    string? path = Marshal.PtrToStringUni(detail + 4);
                    if (path is not null) yield return path;
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static SafeFileHandle Open(string path, bool readWrite, bool overlapped) =>
        CreateFile(path, readWrite ? GENERIC_READ | GENERIC_WRITE : 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, overlapped ? FILE_FLAG_OVERLAPPED : 0, IntPtr.Zero);

    /// <summary>Controles PlayStation ligados (USB ou Bluetooth).</summary>
    public static IReadOnlyList<HidPadInfo> FindPads()
    {
        var list = new List<HidPadInfo>();
        foreach (string path in DevicePaths())
        {
            using var h = Open(path, false, false);
            if (h.IsInvalid) continue;
            var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
            if (!HidD_GetAttributes(h, ref attr)) continue;
            if (PadIds.Identify(attr.VendorID, attr.ProductID) is not { } model) continue;
            if (!HidD_GetPreparsedData(h, out IntPtr pre)) continue;
            try
            {
                var caps = new HIDP_CAPS { Reserved = new ushort[17] };
                HidP_GetCaps(pre, ref caps);
                if (caps.UsagePage != 0x01 || caps.Usage != 0x05) continue; // só o "gamepad" (o DualSense tem outras interfaces, como áudio)
                list.Add(new HidPadInfo(path, model, attr.ProductID, caps.InputReportByteLength, caps.FeatureReportByteLength));
            }
            finally
            {
                HidD_FreePreparsedData(pre);
            }
        }
        return list;
    }

    /// <summary>Abre o controle para ler e mandar comandos.</summary>
    public static IHidChannel OpenPad(HidPadInfo pad) => new Channel(pad);

    private sealed class Channel : IHidChannel
    {
        private readonly SafeFileHandle _handle;
        private readonly FileStream _input;
        private readonly int _featureLength;
        private readonly object _lock = new();

        public int InputLength { get; }

        public Channel(HidPadInfo pad)
        {
            _handle = Open(pad.Path, true, true);
            if (_handle.IsInvalid)
                throw new IOException("Não foi possível abrir o controle (outro programa como Steam, DS4Windows ou HidHide pode estar usando-o com exclusividade).",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            HidD_SetNumInputBuffers(_handle, 64);
            InputLength = pad.InputLength;
            _featureLength = Math.Max(64, (int)pad.FeatureLength);
            _input = new FileStream(_handle, FileAccess.Read, 0, true);
        }

        public byte[] GetFeature(byte reportId)
        {
            var buffer = new byte[_featureLength];
            buffer[0] = reportId;
            lock (_lock)
            {
                if (!HidD_GetFeature(_handle, buffer, buffer.Length))
                    throw new IOException($"O controle não respondeu ao relatório 0x{reportId:X2}.", new Win32Exception(Marshal.GetLastWin32Error()));
            }
            return buffer;
        }

        public void SetFeature(byte reportId, ReadOnlySpan<byte> payload)
        {
            var buffer = new byte[_featureLength];
            buffer[0] = reportId;
            payload[..Math.Min(payload.Length, buffer.Length - 1)].CopyTo(buffer.AsSpan(1));
            lock (_lock)
            {
                if (!HidD_SetFeature(_handle, buffer, buffer.Length))
                    throw new IOException($"O controle recusou o comando 0x{reportId:X2}.", new Win32Exception(Marshal.GetLastWin32Error()));
            }
        }

        public byte[]? ReadInput(int timeoutMs)
        {
            var buffer = new byte[Math.Max(64, InputLength)];
            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                int n = _input.ReadAsync(buffer, 0, buffer.Length, cts.Token).GetAwaiter().GetResult();
                return n > 0 ? buffer[..n] : null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        public void Dispose()
        {
            _input.Dispose();
            _handle.Dispose();
        }
    }
}
