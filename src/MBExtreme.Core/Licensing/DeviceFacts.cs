using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MBExtreme.Core.Licensing;

/// <summary>
/// Dados deste computador para a tela "Conta e licença" e para a lista "Seus computadores" do site.
/// O endereço MAC aparece só na tela do programa; o que vai para o servidor é o identificador anônimo (hash) e o resumo do hardware.
/// </summary>
public static class DeviceFacts
{
    /// <summary>MAC no formato AA-BB-CC-DD-EE-FF (vazio se não houver).</summary>
    public static string FormatMac(byte[]? bytes) =>
        bytes is { Length: 6 } && bytes.Any(b => b != 0) ? string.Join("-", bytes.Select(b => b.ToString("X2"))) : "";

    /// <summary>Placa de rede principal: a que está ligada, tem gateway e não é virtual nem loopback.</summary>
    public static string PrimaryMac()
    {
        try
        {
            var nics = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .Where(n => !IsVirtual(n.Description))
                .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Any(g => g.Address is { } a && !a.Equals(System.Net.IPAddress.Any)))
                .ThenBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 0 : 1);
            foreach (var nic in nics)
                if (FormatMac(nic.GetPhysicalAddress().GetAddressBytes()) is { Length: > 0 } mac) return mac;
        }
        catch (NetworkInformationException) { }
        return "";
    }

    public static bool IsVirtual(string description) =>
        new[] { "virtual", "vmware", "hyper-v", "vpn", "tap-", "wireguard", "zerotier", "hamachi", "bluetooth", "loopback", "radmin" }
            .Any(w => description.Contains(w, StringComparison.OrdinalIgnoreCase));

    public static int RamGb()
    {
        try
        {
            long bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            return bytes > 0 ? (int)Math.Round(bytes / 1073741824.0) : 0;
        }
        catch (InvalidOperationException) { return 0; }
    }

    /// <summary>Nome da placa de vídeo principal pelo registro do driver (sem WMI). Prefere a dedicada.</summary>
    [SupportedOSPlatform("windows")]
    public static string GpuName()
    {
        const string cls = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        var names = new List<string>();
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(cls);
            foreach (string sub in root?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                if (sub.Length != 4 || !sub.All(char.IsDigit)) continue;
                using var k = root!.OpenSubKey(sub);
                if (k?.GetValue("DriverDesc") is string d && d.Length > 0 && !IsVirtual(d) && !d.Contains("Basic", StringComparison.OrdinalIgnoreCase)) names.Add(d.Trim());
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return PickGpu(names);
    }

    /// <summary>Entre várias placas, a dedicada (NVIDIA/AMD Radeon/Intel Arc) ganha da integrada.</summary>
    public static string PickGpu(IReadOnlyList<string> names)
    {
        static int Score(string n) =>
            n.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? 3
            : n.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase) || n.Contains(" Arc", StringComparison.OrdinalIgnoreCase) ? 2
            : 1;
        return names.OrderByDescending(Score).FirstOrDefault() ?? "";
    }

    /// <summary>Identificador anônimo encurtado para mostrar (o completo fica só com o programa e o servidor).</summary>
    public static string ShortId(string deviceId) => deviceId.Length > 8 ? deviceId[..4].ToUpperInvariant() + "-" + deviceId[4..8].ToUpperInvariant() : deviceId.ToUpperInvariant();

    [SupportedOSPlatform("windows")]
    public static DeviceInfo Collect(string appVersion)
    {
        static string Safe(Func<string> f) { try { return f(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return ""; } }
        return new DeviceInfo(Safe(Windows.SystemSummary.WindowsName), Safe(Windows.SystemSummary.ProcessorName), Safe(GpuName), RamGb(), appVersion);
    }
}
