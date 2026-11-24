using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Install;

/// <summary>O que o diagnóstico leu do PC (só leitura).</summary>
public sealed record ReinstallFacts(bool Uefi, string PartitionStyle, bool BitLocker, bool OnBattery, long FreeBytes, long CSize, long CMinSize, int Disk, int Partition);

public enum CheckLevel { Ok, Warn, Block }

public sealed record ReinstallCheck(CheckLevel Level, string Text);

/// <summary>O que a preparação criou (para Cancelar e para remover depois). Fica em reinstalar.json no C: e na partição.</summary>
public sealed record ReinstallState(int Disk, int Partition, char Letter, long Bytes, string LoaderId, string DeviceId, DateTime Created);

/// <summary>
/// Reinstalar o Windows sem pendrive (plano Técnico): reduz o C:, cria a partição MB-SETUP, copia o instalador da ISO e cria no
/// boot do Windows uma entrada que abre o instalador (boot.wim em ramdisk) só no próximo reinício. Nada é apagado até o técnico
/// formatar na tela do instalador; Cancelar remove a entrada, apaga a partição e devolve o espaço ao C:.
/// </summary>
public static class Reinstall
{
    public const string Label = "MB-SETUP";
    public const long Gb = 1024L * 1024 * 1024;
    public const string StateFileName = "reinstalar.json";
    public static string LocalState => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MBExtreme", StateFileName);

    /// <summary>Tamanho da partição: 8 GB ou a ISO + 1,5 GB (arredondado para cima em GB), o que for maior.</summary>
    public static long PartitionBytes(long isoBytes) => Math.Max(8 * Gb, (long)Math.Ceiling((isoBytes + 1.5 * Gb) / Gb) * Gb);

    public static IReadOnlyList<ReinstallCheck> Evaluate(ReinstallFacts f, long partitionBytes)
    {
        var list = new List<ReinstallCheck>();
        list.Add(f.Uefi
            ? new(f.PartitionStyle.Equals("GPT", StringComparison.OrdinalIgnoreCase) ? CheckLevel.Ok : CheckLevel.Warn, f.PartitionStyle.Equals("GPT", StringComparison.OrdinalIgnoreCase) ? "UEFI + GPT" : $"UEFI com disco {f.PartitionStyle}: o instalador pode pedir para converter")
            : new(CheckLevel.Ok, $"BIOS antiga (Legacy) + {f.PartitionStyle}"));
        list.Add(f.BitLocker
            ? new(CheckLevel.Block, "BitLocker ligado no C:: desligue ou suspenda antes (Painel de Controle › BitLocker)")
            : new(CheckLevel.Ok, "BitLocker desligado"));
        long need = partitionBytes + 10 * Gb;
        list.Add(f.FreeBytes >= need && f.CSize - partitionBytes >= f.CMinSize
            ? new(CheckLevel.Ok, $"{f.FreeBytes / Gb} GB livres no C:")
            : new(CheckLevel.Block, $"Pouco espaço: {f.FreeBytes / Gb} GB livres no C: (precisa de {need / Gb} GB). Limpe o disco antes."));
        list.Add(f.OnBattery
            ? new(CheckLevel.Block, "Notebook na bateria: ligue na tomada")
            : new(CheckLevel.Ok, "Na tomada"));
        return list;
    }

    public static bool CanPrepare(IReadOnlyList<ReinstallCheck> checks) => checks.All(c => c.Level != CheckLevel.Block);

    // ---------- leitura ----------

    [DllImport("kernel32.dll")]
    private static extern bool GetFirmwareType(out int type);

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus { public byte AcLine, BatteryFlag, BatteryPercent, Saver; public int LifeTime, FullLifeTime; }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out PowerStatus status);

    public const string FactsScript =
        "$p=Get-Partition -DriveLetter C; $d=Get-Disk -Number $p.DiskNumber; $s=Get-PartitionSupportedSize -DriveLetter C; " +
        "$b=(New-Object -ComObject Shell.Application).NameSpace('C:').Self.ExtendedProperty('System.Volume.BitLockerProtection'); " +
        "\"$($p.DiskNumber)|$($p.PartitionNumber)|$($p.Size)|$($s.SizeMin)|$($d.PartitionStyle)|$b\"";

    /// <summary>Lê "disco|partição|tamanho|mínimo|estilo|bitlocker" (BitLocker: 1, 3 e 5 = protegido).</summary>
    public static (int Disk, int Partition, long Size, long Min, string Style, bool BitLocker)? ParseFacts(string output)
    {
        string? line = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Count(c => c == '|') == 5);
        if (line is null) return null;
        var p = line.Split('|');
        if (!int.TryParse(p[0], out int disk) || !int.TryParse(p[1], out int part) ||
            !long.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long size) ||
            !long.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long min)) return null;
        bool bitlocker = p[5].Trim() is "1" or "3" or "5";
        return (disk, part, size, min, p[4].Trim(), bitlocker);
    }

    public static ReinstallFacts ReadFacts()
    {
        bool uefi = GetFirmwareType(out int type) && type == 2;
        bool battery = GetSystemPowerStatus(out var power) && power.AcLine == 0 && power.BatteryFlag != 128;
        long free = new DriveInfo("C").AvailableFreeSpace;
        var (_, output, error) = Shell.PowerShell(FactsScript);
        var parsed = ParseFacts(output) ?? throw new InvalidOperationException("Não foi possível ler o disco do Windows. " + error.Trim());
        return new ReinstallFacts(uefi, parsed.Style, parsed.BitLocker, battery, free, parsed.Size, parsed.Min, parsed.Disk, parsed.Partition);
    }

    // ---------- preparação ----------

    public static string ShrinkScript(long cSize, long bytes) =>
        $"Resize-Partition -DriveLetter C -Size {(cSize - bytes).ToString(CultureInfo.InvariantCulture)}; 'ok'";

    /// <summary>Cria a partição MB-SETUP no espaço liberado e devolve "letra|partição".</summary>
    public static string CreateScript(int disk, long bytes) =>
        $"$p=New-Partition -DiskNumber {disk} -Size {bytes.ToString(CultureInfo.InvariantCulture)} -AssignDriveLetter; " +
        $"Format-Volume -Partition $p -FileSystem NTFS -NewFileSystemLabel '{Label}' -Confirm:$false | Out-Null; " +
        "$q=Get-Partition -DiskNumber $p.DiskNumber -PartitionNumber $p.PartitionNumber; \"$($q.DriveLetter)|$($q.PartitionNumber)\"";

    public static (char Letter, int Partition)? ParseCreated(string output)
    {
        var m = Regex.Match(output, @"(?m)^\s*([A-Z])\|(\d+)\s*$");
        return m.Success ? (m.Groups[1].Value[0], int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)) : null;
    }

    private static readonly Regex Guid = new(@"\{[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}", RegexOptions.Compiled);

    /// <summary>O {guid} que o bcdedit /create devolve (o texto muda com o idioma do Windows; o guid não).</summary>
    public static string? ParseGuid(string output) => Guid.Match(output) is { Success: true } m ? m.Value : null;

    /// <summary>Comandos do bcdedit que apontam a entrada nova para o boot.wim da partição (ramdisk) e a abrem só no próximo boot.</summary>
    public static IReadOnlyList<string[]> BootCommands(char letter, string device, string loader, bool uefi)
    {
        string l = char.ToUpperInvariant(letter) + ":";
        string ramdisk = $"ramdisk=[{l}]\\sources\\boot.wim,{device}";
        return new[]
        {
            new[] { "/set", device, "ramdisksdidevice", "partition=" + l },
            new[] { "/set", device, "ramdisksdipath", @"\boot\boot.sdi" },
            new[] { "/set", loader, "device", ramdisk },
            new[] { "/set", loader, "osdevice", ramdisk },
            new[] { "/set", loader, "path", uefi ? @"\windows\system32\boot\winload.efi" : @"\windows\system32\boot\winload.exe" },
            new[] { "/set", loader, "systemroot", @"\windows" },
            new[] { "/set", loader, "winpe", "yes" },
            new[] { "/set", loader, "detecthal", "yes" },
            new[] { "/displayorder", loader, "/addlast" },
            new[] { "/bootsequence", loader },
        };
    }

    /// <summary>Apaga a partição MB-SETUP (confere o nome antes) e devolve o espaço ao C:.</summary>
    public static string RemoveScript(int disk, int partition) =>
        $"$p=Get-Partition -DiskNumber {disk} -PartitionNumber {partition}; " +
        $"if ((Get-Volume -Partition $p).FileSystemLabel -ne '{Label}') {{ throw 'A partição não é a MB-SETUP.' }}; " +
        "Remove-Partition -DiskNumber $p.DiskNumber -PartitionNumber $p.PartitionNumber -Confirm:$false; " +
        "$s=Get-PartitionSupportedSize -DriveLetter C; Resize-Partition -DriveLetter C -Size $s.SizeMax -ErrorAction SilentlyContinue; 'ok'";

    /// <summary>Acha a MB-SETUP pelo nome (depois de reinstalar, o reinstalar.json do C: não existe mais). Saída: "disco|partição|letra".</summary>
    public const string FindScript =
        "Get-Volume -FileSystemLabel '" + Label + "' -ErrorAction SilentlyContinue | Get-Partition | ForEach-Object { \"$($_.DiskNumber)|$($_.PartitionNumber)|$($_.DriveLetter)\" }";

    public static (int Disk, int Partition, char? Letter)? ParseFound(string output)
    {
        var m = Regex.Match(output, @"(?m)^\s*(\d+)\|(\d+)\|([A-Z]?)\s*$");
        if (!m.Success) return null;
        return (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
            m.Groups[3].Value.Length == 1 ? m.Groups[3].Value[0] : null);
    }

    public static string ToJson(ReinstallState s) => JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true });

    public static ReinstallState? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<ReinstallState>(json); }
        catch (JsonException) { return null; }
    }

    public static ReinstallState? LoadState()
    {
        try { return File.Exists(LocalState) ? FromJson(File.ReadAllText(LocalState)) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    // ---------- execução (Windows, administrador) ----------

    /// <summary>Backup antes: drivers e Wi-Fi (Backup para formatar) e a chave do Windows gravada na BIOS, numa pasta temporária.</summary>
    public static (FormatBackupInfo Info, string Key) Backup(IProgress<string> progress, CancellationToken ct)
    {
        string temp = Path.Combine(Path.GetTempPath(), "MB-Reinstalar");
        if (Directory.Exists(temp)) Directory.Delete(temp, true);
        Directory.CreateDirectory(temp);
        var (info, _) = FormatBackup.Create(temp, true, true, progress, ct);
        progress.Report("Lendo a chave do Windows...");
        var (_, output, _) = Shell.PowerShell("(Get-CimInstance -ClassName SoftwareLicensingService).OA3xOriginalProductKey");
        string key = output.Trim();
        File.WriteAllText(Path.Combine(info.Folder, "chave-windows.txt"),
            key.Length > 0 ? key + "\r\n\r\nChave gravada na BIOS deste PC. O Windows usa sozinha ao reinstalar a mesma edição." : "Este PC não tem chave na BIOS: a ativação é digital (vinculada à conta/placa).");
        return (info, key);
    }

    public static ReinstallState Prepare(string iso, ReinstallFacts facts, string backupFolder, IProgress<string> progress, CancellationToken ct)
    {
        if (LoadState() is not null) throw new InvalidOperationException("Já existe uma preparação. Cancele a anterior antes.");
        long bytes = PartitionBytes(new FileInfo(iso).Length);
        ReinstallState? state = null;
        string device = "", loader = "";
        try
        {
            progress.Report("Reduzindo o C: em " + bytes / Gb + " GB...");
            Must(Shell.PowerShell(ShrinkScript(facts.CSize, bytes)), "Não foi possível reduzir o C:");
            progress.Report("Criando a partição MB-SETUP...");
            var created = Shell.PowerShell(CreateScript(facts.Disk, bytes));
            var made = ParseCreated(created.Output);
            if (made is null)
            {
                Shell.PowerShell("$s=Get-PartitionSupportedSize -DriveLetter C; Resize-Partition -DriveLetter C -Size $s.SizeMax");
                throw new InvalidOperationException("Não foi possível criar a partição. " + created.Error.Trim());
            }
            state = new ReinstallState(facts.Disk, made.Value.Partition, made.Value.Letter, bytes, "", "", DateTime.Now);
            ct.ThrowIfCancellationRequested();

            progress.Report("Copiando o instalador da ISO...");
            var (code, output, error) = Shell.PowerShell(IsoBuilder.MountScript(iso));
            string isoLetter = output.Trim().Split('\n').LastOrDefault()?.Trim() ?? "";
            if (code != 0 || isoLetter.Length != 1) throw new InvalidOperationException("Não foi possível abrir a ISO. " + error.Trim());
            try
            {
                var copy = Shell.RunOem("robocopy.exe", isoLetter + ":\\", state.Letter + ":\\", "/E", "/NFL", "/NDL", "/NJH", "/R:2", "/W:2");
                if (!IsoBuilder.RobocopyOk(copy.ExitCode)) throw new InvalidOperationException("Não foi possível copiar o instalador (robocopy " + copy.ExitCode + ").");
            }
            finally { Shell.PowerShell(IsoBuilder.DismountScript(iso)); }
            string target = Path.Combine(state.Letter + ":\\", FormatBackup.RootFolder, Path.GetFileName(backupFolder));
            Shell.RunOem("robocopy.exe", backupFolder, target, "/E", "/NFL", "/NDL", "/NJH");
            ct.ThrowIfCancellationRequested();

            progress.Report("Programando o instalador no próximo reinício...");
            device = ParseGuid(Shell.RunOem("bcdedit.exe", "/create", "/device", "/d", "MB-SETUP ramdisk").Output) ?? throw new InvalidOperationException("O bcdedit não criou a entrada do ramdisk.");
            loader = ParseGuid(Shell.RunOem("bcdedit.exe", "/create", "/d", "Instalar o Windows (MB Extreme)", "/application", "osloader").Output) ?? throw new InvalidOperationException("O bcdedit não criou a entrada do instalador.");
            state = state with { DeviceId = device, LoaderId = loader };
            foreach (var args in BootCommands(state.Letter, device, loader, facts.Uefi))
            {
                var r = Shell.RunOem("bcdedit.exe", args);
                if (r.ExitCode != 0) throw new InvalidOperationException("O bcdedit recusou: " + string.Join(' ', args) + ". " + (r.Output + r.Error).Trim());
            }
            Directory.CreateDirectory(Path.GetDirectoryName(LocalState)!);
            File.WriteAllText(LocalState, ToJson(state));
            File.WriteAllText(Path.Combine(state.Letter + ":\\", StateFileName), ToJson(state));
            return state;
        }
        catch
        {
            if (state is not null) Cancel(state with { DeviceId = device, LoaderId = loader });
            throw;
        }
    }

    /// <summary>Desfaz tudo: apaga as entradas do boot, a partição e devolve o espaço ao C:.</summary>
    public static void Cancel(ReinstallState state)
    {
        foreach (var id in new[] { state.LoaderId, state.DeviceId }.Where(i => i.Length > 0))
            Shell.RunOem("bcdedit.exe", "/delete", id, "/f");
        Must(Shell.PowerShell(RemoveScript(state.Disk, state.Partition)), "Não foi possível apagar a partição MB-SETUP");
        try { File.Delete(LocalState); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Depois de reinstalar: devolve drivers e Wi-Fi salvos na MB-SETUP, apaga a partição e devolve o espaço ao C:.</summary>
    public static string RemoveAfterInstall(IProgress<string> progress, CancellationToken ct)
    {
        var found = ParseFound(Shell.PowerShell(FindScript).Output) ?? throw new InvalidOperationException("A partição MB-SETUP não foi encontrada.");
        if (found.Letter is { } l)
        {
            var backup = FormatBackup.Find(new[] { l + ":\\" }).FirstOrDefault();
            if (backup is not null) FormatBackup.Restore(backup, progress, ct);
        }
        progress.Report("Apagando a MB-SETUP e devolvendo o espaço ao C:...");
        Must(Shell.PowerShell(RemoveScript(found.Disk, found.Partition)), "Não foi possível apagar a partição MB-SETUP");
        return "ok";
    }

    public static bool HasSetupPartition() => ParseFound(Shell.PowerShell(FindScript).Output) is not null;

    public static void Restart() => Shell.Run("shutdown.exe", "/r", "/t", "5", "/c", "MB Extreme: abrindo o instalador do Windows");

    private static void Must((int Code, string Out, string Err) r, string what)
    {
        if (r.Code != 0) throw new InvalidOperationException($"{what}. {(r.Err + r.Out).Trim().Split('\n').LastOrDefault()?.Trim()}");
    }
}
