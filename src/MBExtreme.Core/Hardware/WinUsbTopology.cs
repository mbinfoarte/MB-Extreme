using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MBExtreme.Core.Hardware;

/// <summary>
/// Lê as portas USB do PC pelo Windows (como o USB Device Viewer da Microsoft): cada controlador USB, o hub raiz,
/// as portas, se são 2.0 ou 3.x, tipo C, o que está ligado e, quando a placa-mãe informa, se a porta é traseira ou frontal.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WinUsbTopology
{
    private static readonly Guid HostControllerGuid = new("3ABF6F2D-71C4-462A-8A92-1E6861E6AF27");

    private const uint IOCTL_USB_GET_ROOT_HUB_NAME = 0x220408, IOCTL_USB_GET_NODE_INFORMATION = 0x220408,
        IOCTL_USB_GET_NODE_CONNECTION_NAME = 0x220414, IOCTL_USB_GET_NODE_CONNECTION_DRIVERKEY_NAME = 0x220420,
        IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX = 0x220448, IOCTL_USB_GET_HUB_INFORMATION_EX = 0x220454,
        IOCTL_USB_GET_PORT_CONNECTOR_PROPERTIES = 0x220458, IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX_V2 = 0x22045C;

    private const int DIGCF_PRESENT = 0x2, DIGCF_ALLCLASSES = 0x4, DIGCF_DEVICEINTERFACE = 0x10;
    private const int SPDRP_DEVICEDESC = 0x0, SPDRP_HARDWAREID = 0x1, SPDRP_CLASS = 0x7, SPDRP_DRIVER = 0x9, SPDRP_FRIENDLYNAME = 0xC;
    private const uint GENERIC_WRITE = 0x40000000, FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public int DevInst; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY { public Guid fmtid; public int pid; }

    [DllImport("setupapi.dll", SetLastError = true)] private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, int flags);
    [DllImport("setupapi.dll", SetLastError = true, EntryPoint = "SetupDiGetClassDevsW")] private static extern IntPtr SetupDiGetAllClassDevs(IntPtr classGuid, IntPtr enumerator, IntPtr parent, int flags);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, int index, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, ref SP_DEVINFO_DATA info);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInfo(IntPtr set, int index, ref SP_DEVINFO_DATA info);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref SP_DEVINFO_DATA info, int property, out int type, byte[]? buffer, int size, out int required);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetDevicePropertyW")]
    private static extern bool SetupDiGetDeviceProperty(IntPtr set, ref SP_DEVINFO_DATA info, ref DEVPROPKEY key, out int type, byte[]? buffer, int size, out int required, int flags);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetDeviceInstanceIdW")]
    private static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref SP_DEVINFO_DATA info, char[] buffer, int size, out int required);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);

    private static readonly Guid DeviceProps = new("540b947e-8b40-45bc-a8a2-6a0b894cbda2");

    private sealed record DeviceNode(string Name, string Class, byte[]? Pld, ushort Vendor, string? InstanceId);

    /// <summary>As portas do PC, já juntas e com nome. Lista vazia se o Windows não deixar ler.</summary>
    public static IReadOnlyList<UsbPortInfo> Read()
    {
        var devices = DeviceNodes();
        var raw = new List<UsbRawPort>();
        int hc = 0;
        foreach (var (path, hardwareId, description) in HostControllers())
        {
            hc++;
            var (source, tenGig) = UsbPorts.ClassifyController(hardwareId, description);
            string controllerId = $"HC{hc}";
            using var handle = Open(path);
            if (handle.IsInvalid) continue;
            string? rootHub = Name(handle, IOCTL_USB_GET_ROOT_HUB_NAME, null, 4);
            if (rootHub is null) continue;
            Walk(rootHub, controllerId, description, source, tenGig, behindHub: false, parentInternal: false, depth: 0, devices, raw);
        }
        return UsbPorts.Build(raw);
    }

    private static void Walk(string hubName, string controllerId, string controllerName, UsbSource source, bool tenGig, bool behindHub, bool parentInternal,
        int depth, Dictionary<string, DeviceNode> devices, List<UsbRawPort> raw)
    {
        if (depth > 4) return;
        using var hub = Open(@"\\.\" + hubName);
        if (hub.IsInvalid) return;
        int ports = PortCount(hub);
        for (int port = 1; port <= ports; port++)
        {
            var conn = Ioctl(hub, IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX, Index(port, 512));
            if (conn is null) continue;
            bool connected = BitConverter.ToInt32(conn, 31) == 1;
            bool isHub = conn[24] != 0;
            ushort vendor = BitConverter.ToUInt16(conn, 12);

            bool user = true, typeC = false;
            int companion = 0;
            var props = Ioctl(hub, IOCTL_USB_GET_PORT_CONNECTOR_PROPERTIES, Index(port, 512));
            if (props is not null)
            {
                int bits = BitConverter.ToInt32(props, 8);
                user = (bits & 1) != 0;
                typeC = (bits & 8) != 0;
                string companionHub = Encoding.Unicode.GetString(props, 16, Math.Max(0, props.Length - 16)).Split('\0')[0];
                if (companionHub.Length == 0 || SameHub(companionHub, hubName)) companion = BitConverter.ToUInt16(props, 14);
            }
            if (behindHub && !parentInternal && !connected) continue; // hub externo: só as portas com algo ligado

            bool usb2 = true, usb3 = false, ssPlus = false;
            var v2in = new byte[16];
            BitConverter.GetBytes(port).CopyTo(v2in, 0);
            BitConverter.GetBytes(16).CopyTo(v2in, 4);
            BitConverter.GetBytes(7).CopyTo(v2in, 8);
            var v2 = Ioctl(hub, IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX_V2, v2in, 16);
            if (v2 is not null)
            {
                int protocols = BitConverter.ToInt32(v2, 8), flags = BitConverter.ToInt32(v2, 12);
                usb2 = (protocols & 3) != 0;
                usb3 = (protocols & 4) != 0;
                ssPlus = (flags & 4) != 0;
            }

            string? device = null, instance = null;
            UsbUse use = UsbUse.None;
            UsbPanel panel = UsbPanel.Unknown;
            if (connected)
            {
                string? key = Name(hub, IOCTL_USB_GET_NODE_CONNECTION_DRIVERKEY_NAME, Index(port, 512), 8);
                if (key is not null && devices.TryGetValue(key, out var node))
                {
                    device = node.Name;
                    instance = node.InstanceId;
                    panel = UsbPorts.PanelFromPld(node.Pld);
                    use = UsbPorts.Classify(node.Name, node.Class, vendor != 0 ? vendor : node.Vendor, isHub);
                }
                else
                {
                    device = isHub ? "Hub USB" : "Aparelho USB";
                    use = isHub ? UsbUse.Hub : UsbUse.Other;
                }
            }
            if (panel == UsbPanel.Unknown && behindHub && parentInternal) panel = UsbPanel.Front; // hub interno costuma ser o painel frontal

            raw.Add(new UsbRawPort(hubName, port, usb2 || !usb3, usb3, user, typeC, companion, controllerId, controllerName, source, tenGig,
                behindHub, panel, device, use, ssPlus, instance));

            if (connected && isHub && Name(hub, IOCTL_USB_GET_NODE_CONNECTION_NAME, Index(port, 512), 8) is { } child)
                Walk(child, controllerId, controllerName, source, tenGig, behindHub: true, parentInternal: !user, depth + 1, devices, raw);
        }
    }

    private static bool SameHub(string a, string b)
    {
        static string Clean(string s) => s.Replace(@"\\?\", "").Replace(@"\??\", "").Trim('\\');
        return string.Equals(Clean(a), Clean(b), StringComparison.OrdinalIgnoreCase);
    }

    private static int PortCount(SafeFileHandle hub)
    {
        var ex = Ioctl(hub, IOCTL_USB_GET_HUB_INFORMATION_EX, new byte[0], 0, 512);
        if (ex is not null)
        {
            int n = BitConverter.ToUInt16(ex, 4);
            if (n > 0 && n < 128) return n;
        }
        var info = Ioctl(hub, IOCTL_USB_GET_NODE_INFORMATION, new byte[0], 0, 512);
        return info is not null ? info[6] : 0;
    }

    private static byte[] Index(int port, int size)
    {
        var buffer = new byte[size];
        BitConverter.GetBytes(port).CopyTo(buffer, 0);
        return buffer;
    }

    private static byte[]? Ioctl(SafeFileHandle h, uint code, byte[] input, int inputSize = -1, int outputSize = 512)
    {
        if (inputSize < 0) inputSize = input.Length;
        var output = new byte[outputSize];
        if (inputSize > 0) Array.Copy(input, output, Math.Min(inputSize, outputSize));
        return DeviceIoControl(h, code, input, inputSize, output, outputSize, out _, IntPtr.Zero) ? output : null;
    }

    private static string? Name(SafeFileHandle h, uint code, byte[]? input, int offset)
    {
        var output = Ioctl(h, code, input ?? new byte[0], input?.Length ?? 0, 1024);
        if (output is null) return null;
        string name = Encoding.Unicode.GetString(output, offset, output.Length - offset).Split('\0')[0];
        return name.Length == 0 ? null : name;
    }

    private static SafeFileHandle Open(string path) =>
        CreateFile(path, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

    private static IEnumerable<(string Path, string HardwareId, string Description)> HostControllers()
    {
        var guid = HostControllerGuid;
        IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == new IntPtr(-1)) yield break;
        try
        {
            var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data); i++)
            {
                var info = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int size, ref info);
                if (size <= 0) continue;
                IntPtr detail = Marshal.AllocHGlobal(size);
                string? path;
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out _, ref info)) continue;
                    path = Marshal.PtrToStringUni(detail + 4);
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
                if (path is null) continue;
                string hardwareId = Text(set, ref info, SPDRP_HARDWAREID) ?? "";
                string description = Text(set, ref info, SPDRP_FRIENDLYNAME) ?? Text(set, ref info, SPDRP_DEVICEDESC) ?? "Controlador USB";
                yield return (path, hardwareId, description);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    /// <summary>Todos os aparelhos presentes, pela chave do driver (é como o hub diz quem está em cada porta).</summary>
    private static Dictionary<string, DeviceNode> DeviceNodes()
    {
        var map = new Dictionary<string, DeviceNode>(StringComparer.OrdinalIgnoreCase);
        IntPtr set = SetupDiGetAllClassDevs(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, DIGCF_ALLCLASSES | DIGCF_PRESENT);
        if (set == new IntPtr(-1)) return map;
        try
        {
            var info = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
            for (int i = 0; SetupDiEnumDeviceInfo(set, i, ref info); i++)
            {
                string? driver = Text(set, ref info, SPDRP_DRIVER);
                if (driver is null) continue;
                string? hardwareId = Text(set, ref info, SPDRP_HARDWAREID);
                if (hardwareId is null || !hardwareId.StartsWith("USB", StringComparison.OrdinalIgnoreCase)) continue;
                string name = PropertyText(set, ref info, 4) // nome que o próprio aparelho informa ("Wireless Controller", "HD60 X")
                    ?? Text(set, ref info, SPDRP_FRIENDLYNAME) ?? Text(set, ref info, SPDRP_DEVICEDESC) ?? "Aparelho USB";
                string cls = Text(set, ref info, SPDRP_CLASS) ?? "";
                ushort vendor = 0;
                int v = hardwareId.IndexOf("VID_", StringComparison.OrdinalIgnoreCase);
                if (v >= 0 && v + 8 <= hardwareId.Length) ushort.TryParse(hardwareId.AsSpan(v + 4, 4), System.Globalization.NumberStyles.HexNumber, null, out vendor);
                map[driver] = new DeviceNode(name, cls, PropertyBytes(set, ref info, 9), vendor, InstanceId(set, ref info));
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
        return map;
    }

    private static string? InstanceId(IntPtr set, ref SP_DEVINFO_DATA info)
    {
        var buffer = new char[512];
        return SetupDiGetDeviceInstanceId(set, ref info, buffer, buffer.Length, out _) ? new string(buffer).Split('\0')[0] : null;
    }

    private static string? Text(IntPtr set, ref SP_DEVINFO_DATA info, int property)
    {
        SetupDiGetDeviceRegistryProperty(set, ref info, property, out _, null, 0, out int size);
        if (size <= 0) return null;
        var buffer = new byte[size];
        if (!SetupDiGetDeviceRegistryProperty(set, ref info, property, out _, buffer, size, out _)) return null;
        string s = Encoding.Unicode.GetString(buffer).Split('\0')[0].Trim();
        return s.Length == 0 ? null : s;
    }

    private static byte[]? PropertyBytes(IntPtr set, ref SP_DEVINFO_DATA info, int pid)
    {
        var key = new DEVPROPKEY { fmtid = DeviceProps, pid = pid };
        SetupDiGetDeviceProperty(set, ref info, ref key, out _, null, 0, out int size, 0);
        if (size <= 0) return null;
        var buffer = new byte[size];
        return SetupDiGetDeviceProperty(set, ref info, ref key, out _, buffer, size, out _, 0) ? buffer : null;
    }

    private static string? PropertyText(IntPtr set, ref SP_DEVINFO_DATA info, int pid)
    {
        var bytes = PropertyBytes(set, ref info, pid);
        if (bytes is null) return null;
        string s = Encoding.Unicode.GetString(bytes).Split('\0')[0].Trim();
        return s.Length == 0 ? null : s;
    }
}
