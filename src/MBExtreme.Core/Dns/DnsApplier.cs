using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;

namespace MBExtreme.Core.Dns;

/// <summary>
/// Aplica e restaura o DNS dos adaptadores de rede ativos. Requer administrador.
/// "Restaurar" volta o DNS para automático (DHCP).
/// </summary>
[SupportedOSPlatform("windows")]
public static class DnsApplier
{
    public static IReadOnlyList<NetworkInterface> ActiveAdapters() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                        && n.Supports(NetworkInterfaceComponent.IPv4)
                        && n.GetIPProperties().GatewayAddresses.Any(g =>
                            g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
            .ToList();

    public static string DescribeCurrent()
    {
        var parts = ActiveAdapters().Select(a =>
            $"{a.Name}: {string.Join(", ", a.GetIPProperties().DnsAddresses.Select(d => d.ToString()))}");
        var text = string.Join("  |  ", parts);
        return text.Length > 0 ? text : "Nenhum adaptador de rede ativo encontrado.";
    }

    public static Task ApplyAsync(DnsProvider provider, CancellationToken ct = default)
    {
        if (!IPAddress.TryParse(provider.Primary, out _) || !IPAddress.TryParse(provider.Secondary, out _))
            throw new ArgumentException("Endereço DNS inválido.", nameof(provider));

        return ForEachAdapterAsync(
            index => $"Set-DnsClientServerAddress -InterfaceIndex {index} -ServerAddresses ('{provider.Primary}','{provider.Secondary}'); Clear-DnsClientCache",
            ct);
    }

    public static Task RestoreAsync(CancellationToken ct = default) =>
        ForEachAdapterAsync(
            index => $"Set-DnsClientServerAddress -InterfaceIndex {index} -ResetServerAddresses; Clear-DnsClientCache",
            ct);

    private static async Task ForEachAdapterAsync(Func<int, string> command, CancellationToken ct)
    {
        var adapters = ActiveAdapters();
        if (adapters.Count == 0)
            throw new InvalidOperationException("Nenhum adaptador de rede ativo foi encontrado.");

        foreach (var adapter in adapters)
        {
            int index = adapter.GetIPProperties().GetIPv4Properties().Index;
            await RunPowerShellAsync(command(index), ct);
        }
    }

    private static async Task RunPowerShellAsync(string command, CancellationToken ct)
    {
        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", command })
            info.ArgumentList.Add(argument);

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("Não foi possível iniciar o PowerShell.");
        string error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? "O Windows recusou a alteração de DNS. Execute o programa como administrador."
                : error.Trim());
    }
}
