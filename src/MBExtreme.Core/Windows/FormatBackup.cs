using System.Text.Json;

namespace MBExtreme.Core.Windows;

/// <summary>O que tem num backup para formatar (arquivo backup.json na pasta).</summary>
public sealed record FormatBackupInfo(string Computer, DateTime Created, int Drivers, int WifiNetworks, string Windows)
{
    public string Folder { get; init; } = "";
}

/// <summary>
/// Backup para formatar: salva os drivers instalados (pnputil /export-driver, menos os que já vêm com o Windows) e as redes Wi-Fi
/// com a senha (netsh wlan export profile key=clear) numa pasta "MBExtreme-Backup" no pendrive ou disco escolhido. Depois de formatar,
/// a Pós-formatação acha a pasta sozinha e reinstala tudo. As senhas Wi-Fi ficam em texto: guarde o pendrive com cuidado.
/// </summary>
public static class FormatBackup
{
    public const string RootFolder = "MBExtreme-Backup";
    public const string Manifest = "backup.json";

    public static string NewFolder(string destination, string computer, DateTime now) =>
        Path.Combine(destination, RootFolder, $"{Sanitize(computer)}-{now:yyyy-MM-dd-HHmm}");

    // caracteres que o Windows não aceita em nome de pasta (fixos, para dar o mesmo resultado em qualquer sistema)
    private const string Invalid = "<>:\"/\\|?*";

    private static string Sanitize(string name) => string.Concat(name.Select(c => Invalid.Contains(c) || c < 32 ? '_' : c));

    public static string ToJson(FormatBackupInfo info) => JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });

    public static FormatBackupInfo? FromJson(string json, string folder)
    {
        try { return JsonSerializer.Deserialize<FormatBackupInfo>(json) is { } i ? i with { Folder = folder } : null; }
        catch (JsonException) { return null; }
    }

    /// <summary>Procura backups nas pastas raiz informadas (pendrives e discos), do mais novo para o mais velho.</summary>
    public static IReadOnlyList<FormatBackupInfo> Find(IEnumerable<string> roots)
    {
        var list = new List<FormatBackupInfo>();
        foreach (var root in roots)
        {
            string dir = Path.Combine(root, RootFolder);
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var folder in Directory.EnumerateDirectories(dir))
                {
                    string manifest = Path.Combine(folder, Manifest);
                    if (File.Exists(manifest) && FromJson(File.ReadAllText(manifest), folder) is { } info) list.Add(info);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return list.OrderByDescending(b => b.Created).ToList();
    }

    public static IEnumerable<string> ReadyRoots() =>
        DriveInfo.GetDrives().Where(d => { try { return d.IsReady; } catch (IOException) { return false; } }).Select(d => d.RootDirectory.FullName);

    /// <summary>Faz o backup. Retorna a pasta criada.</summary>
    public static (FormatBackupInfo Info, string Log) Create(string destination, bool drivers, bool wifi, IProgress<string>? progress, CancellationToken ct)
    {
        string folder = NewFolder(destination, Environment.MachineName, DateTime.Now);
        Directory.CreateDirectory(folder);
        var log = new System.Text.StringBuilder();
        int driverCount = 0, wifiCount = 0;
        if (drivers)
        {
            progress?.Report("Salvando os drivers (pode levar alguns minutos)...");
            string target = Path.Combine(folder, "drivers");
            Directory.CreateDirectory(target);
            var (code, output, error) = Shell.RunOem("pnputil.exe", "/export-driver", "*", target);
            log.AppendLine(output).AppendLine(error);
            if (code != 0 && Directory.GetFiles(target, "*.inf", SearchOption.AllDirectories).Length == 0)
                throw new InvalidOperationException("O Windows não exportou os drivers (pnputil, código " + code + ").");
            driverCount = Directory.GetDirectories(target).Length;
        }
        ct.ThrowIfCancellationRequested();
        if (wifi)
        {
            progress?.Report("Salvando as redes Wi-Fi...");
            string target = Path.Combine(folder, "wifi");
            Directory.CreateDirectory(target);
            var (_, output, error) = Shell.RunOem("netsh.exe", "wlan", "export", "profile", "key=clear", $"folder={target}");
            log.AppendLine(output).AppendLine(error);
            wifiCount = Directory.GetFiles(target, "*.xml").Length;
        }
        var info = new FormatBackupInfo(Environment.MachineName, DateTime.Now, driverCount, wifiCount, SystemSummary.WindowsName()) { Folder = folder };
        File.WriteAllText(Path.Combine(folder, Manifest), ToJson(info));
        return (info, log.ToString());
    }

    /// <summary>Reinstala os drivers e as redes Wi-Fi de um backup.</summary>
    public static string Restore(FormatBackupInfo backup, IProgress<string>? progress, CancellationToken ct)
    {
        var log = new System.Text.StringBuilder();
        string drivers = Path.Combine(backup.Folder, "drivers");
        if (Directory.Exists(drivers))
        {
            progress?.Report("Instalando os drivers do backup (o Windows usa só os que servem neste PC)...");
            var (_, output, error) = Shell.RunOem("pnputil.exe", "/add-driver", Path.Combine(drivers, "*.inf"), "/subdirs", "/install");
            log.AppendLine(output).AppendLine(error);
        }
        ct.ThrowIfCancellationRequested();
        string wifi = Path.Combine(backup.Folder, "wifi");
        if (Directory.Exists(wifi))
        {
            progress?.Report("Devolvendo as redes Wi-Fi...");
            foreach (var file in Directory.GetFiles(wifi, "*.xml"))
            {
                var (_, output, error) = Shell.RunOem("netsh.exe", "wlan", "add", "profile", $"filename={file}", "user=all");
                log.AppendLine(output).AppendLine(error);
            }
        }
        return log.ToString();
    }
}
