using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using MBExtreme.Core.Windows;
using Microsoft.Win32.SafeHandles;

namespace MBExtreme.Core.Storage;

/// <summary>Uma unidade (letra) ligada ao disco físico onde está.</summary>
public sealed record DriveEntry(
    string Letter, string Label, string Format, long TotalBytes, long FreeBytes,
    string Model, string Media, string Bus, bool IsSystem)
{
    public double UsedPercent => TotalBytes > 0 ? 100.0 * (TotalBytes - FreeBytes) / TotalBytes : 0;

    /// <summary>Pendrive, HD ou SSD externo e cartões (barramento USB ou mídia removível).</summary>
    public bool IsExternal => Bus.Equals("USB", StringComparison.OrdinalIgnoreCase) || Media.Equals("Removable", StringComparison.OrdinalIgnoreCase);

    public string Name => $"{Letter}:";
}

/// <summary>Une as letras de unidade ao modelo, tipo e barramento do disco físico.</summary>
[SupportedOSPlatform("windows")]
public static class DriveMap
{
    private static string _cachedLetters = "";
    private static Dictionary<string, (string Model, string Bus, string Media)> _cachedMap = new();

    /// <summary>Letras das unidades prontas agora (leitura barata, sem PowerShell). Serve para saber quando algo foi conectado ou removido.</summary>
    public static string LettersSignature() =>
        string.Join(",", DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable && d.IsReady).Select(d => d.Name[..1]));

    /// <param name="forceRefresh">Consulta de novo o modelo e o barramento dos discos (leva alguns segundos); sem isso só consulta quando as letras mudam.</param>
    public static IReadOnlyList<DriveEntry> List(bool forceRefresh = false)
    {
        string letters = LettersSignature();
        Dictionary<string, (string Model, string Bus, string Media)> map;
        lock (typeof(DriveMap))
        {
            if (forceRefresh || letters != _cachedLetters || _cachedMap.Count == 0)
            {
                _cachedMap = QueryMap();
                _cachedLetters = letters;
            }

            map = _cachedMap;
        }

        return Build(map);
    }

    private static Dictionary<string, (string Model, string Bus, string Media)> QueryMap()
    {
        const string script =
            "$disks = @{}; Get-Disk | ForEach-Object { $disks[[string]$_.Number] = $_ }; " +
            "$phys = @{}; Get-PhysicalDisk | ForEach-Object { $phys[[string]$_.DeviceId] = $_ }; " +
            "Get-Partition | Where-Object { $_.DriveLetter } | ForEach-Object { " +
            "$n = [string]$_.DiskNumber; $d = $disks[$n]; $p = $phys[$n]; " +
            "[pscustomobject]@{ Letter=[string]$_.DriveLetter; Model=[string]$d.FriendlyName; Bus=[string]$d.BusType; Media=[string]$p.MediaType } } | " +
            "ConvertTo-Json -Compress";

        var result = Shell.PowerShellUtf8(script);
        return result.ExitCode == 0 ? ParseMap(result.Output) : new Dictionary<string, (string Model, string Bus, string Media)>();
    }

    private static IReadOnlyList<DriveEntry> Build(Dictionary<string, (string Model, string Bus, string Media)> map)
    {
        string systemLetter = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd(':', '\\').ToUpperInvariant();

        var list = new List<DriveEntry>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady) continue;
            string letter = drive.Name.TrimEnd(':', '\\').ToUpperInvariant();
            map.TryGetValue(letter, out var info);
            string media = info.Media is { Length: > 0 } m ? m : drive.DriveType == DriveType.Removable ? "Removable" : "";
            list.Add(new DriveEntry(letter, drive.VolumeLabel, drive.DriveFormat, drive.TotalSize, drive.AvailableFreeSpace,
                info.Model ?? "", media, info.Bus ?? "", letter == systemLetter));
        }

        return list;
    }

    /// <summary>Letra → (modelo, barramento, mídia).</summary>
    public static Dictionary<string, (string Model, string Bus, string Media)> ParseMap(string json)
    {
        var map = new Dictionary<string, (string, string, string)>(StringComparer.OrdinalIgnoreCase);
        string text = json.Trim();
        if (text.Length == 0) return map;

        try
        {
            using var document = JsonDocument.Parse(text);
            var items = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().ToList()
                : new List<JsonElement> { document.RootElement };
            foreach (var e in items)
            {
                string Str(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                string letter = Str("Letter");
                if (letter.Length == 1) map[letter] = (Str("Model"), Str("Bus"), Str("Media"));
            }
        }
        catch (JsonException)
        {
            // saída inesperada: sem mapeamento
        }

        return map;
    }

    // ---- remover com segurança

    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, ShareReadWrite = 3, OpenExisting = 3;
    private const uint FsctlLockVolume = 0x90018, FsctlDismountVolume = 0x90020;
    private const uint IoctlStorageMediaRemoval = 0x2D4804, IoctlStorageEjectMedia = 0x2D4808;

    [StructLayout(LayoutKind.Sequential)]
    private struct PreventMediaRemoval { public byte Prevent; }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, IntPtr input, int inputSize, IntPtr output, int outputSize, out int returned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, ref PreventMediaRemoval input, int inputSize, IntPtr output, int outputSize, out int returned, IntPtr overlapped);

    /// <summary>Quais letras podem ser removidas por aqui: só unidades externas, nunca a do Windows.</summary>
    public static bool CanEject(DriveEntry drive) => drive.IsExternal && !drive.IsSystem;

    /// <summary>
    /// Fecha os arquivos abertos na unidade, grava o que estiver na memória e desmonta o volume; em seguida pede ao dispositivo para ejetar.
    /// Quando termina com sucesso, é seguro desconectar.
    /// </summary>
    public static (bool Ok, string Message) Eject(DriveEntry drive)
    {
        if (!CanEject(drive)) return (false, "Esta unidade não é externa; por segurança ela não é removida por aqui.");
        if (drive.Letter.Length != 1 || !char.IsLetter(drive.Letter[0])) return (false, "Unidade inválida.");

        // a lista pode ter alguns segundos: confere de novo que a letra ainda é a mesma unidade externa
        var fresh = List(forceRefresh: true).FirstOrDefault(d => d.Letter == drive.Letter);
        if (fresh is null) return (false, "A unidade não está mais conectada.");
        if (!CanEject(fresh) || !string.Equals(fresh.Model, drive.Model, StringComparison.OrdinalIgnoreCase))
            return (false, "A unidade mudou desde que a lista foi montada. Atualize a lista e tente de novo.");

        using var handle = CreateFile($@"\\.\{drive.Letter}:", GenericRead | GenericWrite, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            return (false, "Não foi possível abrir a unidade (acesso negado ou já removida).");

        if (!DeviceIoControl(handle, FsctlLockVolume, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
            return (false, "A unidade está em uso. Feche os arquivos e programas que a usam (inclusive o Explorador de Arquivos) e tente de novo.");

        if (!DeviceIoControl(handle, FsctlDismountVolume, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
            return (false, "O Windows não conseguiu desmontar a unidade.");

        var allow = new PreventMediaRemoval { Prevent = 0 };
        DeviceIoControl(handle, IoctlStorageMediaRemoval, ref allow, Marshal.SizeOf<PreventMediaRemoval>(), IntPtr.Zero, 0, out _, IntPtr.Zero);
        bool ejected = DeviceIoControl(handle, IoctlStorageEjectMedia, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);

        return (true, ejected
            ? $"{drive.Name} removida. Pode desconectar com segurança."
            : $"{drive.Name} foi desmontada e os dados foram gravados. Pode desconectar; em HD externo, espere ele parar de girar. Se o Windows voltar a mostrar a unidade, repita antes de desconectar.");
    }
}
