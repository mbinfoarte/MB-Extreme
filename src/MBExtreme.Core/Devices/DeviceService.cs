using System.Management;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Devices;

public sealed record DeviceInfo(string Name, string Class, string Manufacturer, string Status, string InstanceId);

/// <summary>Dispositivos conectados ao PC (USB e outros) e limpeza de dispositivos "fantasmas" que não estão mais conectados.</summary>
[SupportedOSPlatform("windows")]
public static class DeviceService
{
    private static readonly Regex SafeInstanceId = new(@"^[A-Za-z0-9_\\&\-\.\{\}#\(\) ]+$");

    /// <summary>Todos os dispositivos presentes agora (Gerenciador de Dispositivos).</summary>
    public static IReadOnlyList<DeviceInfo> ListPresent()
    {
        var list = new List<DeviceInfo>();
        using var searcher = new ManagementObjectSearcher("SELECT Name, PNPClass, Manufacturer, Status, DeviceID FROM Win32_PnPEntity");
        foreach (ManagementBaseObject item in searcher.Get())
        {
            string? name = item["Name"]?.ToString();
            if (string.IsNullOrWhiteSpace(name)) continue;
            list.Add(new DeviceInfo(
                name,
                item["PNPClass"]?.ToString() ?? "Outros",
                item["Manufacturer"]?.ToString() ?? "",
                item["Status"]?.ToString() ?? "",
                item["DeviceID"]?.ToString() ?? ""));
        }

        return list;
    }

    public static bool IsUsb(DeviceInfo device) =>
        device.InstanceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase)
        || device.InstanceId.StartsWith("USBSTOR\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Ids de fornecedor e produto (VID e PID) de um dispositivo USB, quando houver.</summary>
    public static string UsbIds(DeviceInfo device)
    {
        var match = Regex.Match(device.InstanceId, @"VID_([0-9A-F]{4}).*?PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
        return match.Success ? $"{match.Groups[1].Value}:{match.Groups[2].Value}" : "";
    }

    /// <summary>Dispositivos USB e de armazenamento removível que já não estão conectados, mas continuam registrados.</summary>
    public static IReadOnlyList<DeviceInfo> ListGhostDevices()
    {
        const string script =
            "Get-PnpDevice | Where-Object { $_.Status -eq 'Unknown' -and ($_.InstanceId -like 'USB\\*' -or $_.InstanceId -like 'USBSTOR\\*' -or $_.InstanceId -like 'WPDBUSENUM\\*' -or $_.InstanceId -like 'SWD\\WPDBUSENUM\\*' -or $_.InstanceId -like 'HID\\VID*') } | " +
            "Select-Object InstanceId,FriendlyName,Class,Manufacturer | ConvertTo-Json -Compress";

        var result = Shell.PowerShellUtf8(script);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? "Não foi possível listar os dispositivos." : result.Error.Trim());

        return ParseDevices(result.Output);
    }

    public static IReadOnlyList<DeviceInfo> ParseDevices(string json)
    {
        string text = json.Trim();
        if (text.Length == 0) return Array.Empty<DeviceInfo>();

        using var document = JsonDocument.Parse(text);
        var elements = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().ToList()
            : new List<JsonElement> { document.RootElement };

        string Get(JsonElement e, string name) =>
            e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

        return elements
            .Select(e => new DeviceInfo(
                Get(e, "FriendlyName") is { Length: > 0 } friendly ? friendly : Get(e, "InstanceId"),
                Get(e, "Class"), Get(e, "Manufacturer"), "Desconectado", Get(e, "InstanceId")))
            .Where(d => d.InstanceId.Length > 0)
            .ToList();
    }

    /// <summary>Remove o registro de um dispositivo desconectado. O Windows o recria se você conectar de novo.</summary>
    public static void RemoveGhost(string instanceId)
    {
        if (!SafeInstanceId.IsMatch(instanceId))
            throw new ArgumentException("Identificador de dispositivo inválido.", nameof(instanceId));

        var result = Shell.Run("pnputil.exe", "/remove-device", instanceId);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Output)
                ? "O Windows recusou remover o dispositivo."
                : result.Output.Trim());
    }
}
