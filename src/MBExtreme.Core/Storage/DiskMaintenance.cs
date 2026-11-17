using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Storage;

/// <summary>Instalação anterior do Windows ou sobras de atualização de versão (Windows.old, $Windows.~BT).</summary>
public sealed record PreviousWindows(string Path, long Bytes);

/// <summary>Estado da tarefa do Windows que faz o TRIM (SSD) e a desfragmentação (HD) sozinha.</summary>
public sealed record OptimizeTask(bool Enabled, DateTimeOffset? LastRun);

/// <summary>Uma leitura da saúde de um disco, guardada para ver a evolução.</summary>
public sealed record DiskSnapshot(string Name, DateTimeOffset At, string Health, double? WearPercent, double? Temperature, long? PowerOnHours, long? ReadErrors, long? WriteErrors);

/// <summary>Manutenção dos discos com o que o próprio Windows informa: Proteção do Sistema por unidade, otimização agendada e sobras de versões antigas.</summary>
[SupportedOSPlatform("windows")]
public static class DiskMaintenance
{
    /// <summary>Valor do registro onde o Windows lista as unidades com a Proteção do Sistema ligada.</summary>
    private const string SppClients = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SPP\Clients";
    private const string SppValue = "{09F7EDC5-294E-4180-AF6A-FB0E6A0E9513}";

    private static readonly Regex Letter = new(@"\(([A-Za-z])%3A\)", RegexOptions.Compiled);

    /// <summary>Lê as letras das unidades protegidas no texto do registro (ex.: "\\?\Volume{...}\:(C%3A)").</summary>
    public static IReadOnlySet<char> ParseProtected(IEnumerable<string> values) =>
        values.SelectMany(v => Letter.Matches(v).Select(m => char.ToUpperInvariant(m.Groups[1].Value[0]))).ToHashSet();

    public static IReadOnlySet<char> ProtectedDrives()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(SppClients);
            return key?.GetValue(SppValue) is string[] values ? ParseProtected(values) : new HashSet<char>();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return new HashSet<char>(); }
    }

    /// <summary>TRIM ligado no NTFS (null = o Windows não informou).</summary>
    public static bool? TrimEnabled() =>
        Diagnostics.DiagnosticRules.TrimEnabled(Shell.Run("fsutil.exe", "behavior", "query", "DisableDeleteNotify").Output);

    public static OptimizeTask? ScheduledOptimize()
    {
        var r = Shell.PowerShellUtf8(
            "$t = Get-ScheduledTask -TaskPath '\\Microsoft\\Windows\\Defrag\\' -TaskName 'ScheduledDefrag' -ErrorAction Stop; " +
            "$i = $t | Get-ScheduledTaskInfo; \"$($t.State)|$($i.LastRunTime.ToUniversalTime().ToString('o'))\"");
        return r.ExitCode == 0 ? ParseOptimize(r.Output) : null;
    }

    /// <summary>"Ready|2026-11-01T10:00:00.0000000Z". A data 1999/2000 que o Windows usa para "nunca rodou" vira nula.</summary>
    public static OptimizeTask? ParseOptimize(string output)
    {
        var parts = output.Trim().Split('|');
        if (parts.Length != 2 || parts[0].Length == 0) return null;
        bool enabled = !parts[0].Equals("Disabled", StringComparison.OrdinalIgnoreCase);
        DateTimeOffset? last = DateTimeOffset.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) && d.Year > 2001 ? d : null;
        return new OptimizeTask(enabled, last);
    }

    /// <summary>Tamanho das instalações anteriores do Windows (só mede; quem apaga é a Limpeza de Disco do próprio Windows).</summary>
    public static IReadOnlyList<PreviousWindows> FindPrevious(string systemDrive)
    {
        var list = new List<PreviousWindows>();
        foreach (string name in new[] { "Windows.old", "$Windows.~BT", "$Windows.~WS" })
        {
            string path = Path.Combine(systemDrive.TrimEnd('\\') + "\\", name);
            if (!Directory.Exists(path)) continue;
            long bytes = 0;
            try
            {
                foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                {
                    try { bytes += file.Length; } catch (IOException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            list.Add(new PreviousWindows(path, bytes));
        }
        return list;
    }
}

/// <summary>Histórico da saúde de cada disco (uma leitura por dia, até um ano) em %AppData%\MBExtreme\discos.json.</summary>
public sealed class DiskHistory
{
    private static readonly JsonSerializerOptions Json = new();

    public DiskHistory(string? path = null) => FilePath = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "discos.json");

    public string FilePath { get; }

    public IReadOnlyList<DiskSnapshot> Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<List<DiskSnapshot>>(File.ReadAllText(FilePath), Json) ?? new() : new List<DiskSnapshot>(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new List<DiskSnapshot>(); }
    }

    /// <summary>Guarda as leituras de hoje (troca a do mesmo dia) e descarta as de mais de um ano.</summary>
    public IReadOnlyList<DiskSnapshot> Record(IEnumerable<DiskInfo> disks, DateTimeOffset now)
    {
        var today = disks.Select(d => new DiskSnapshot(d.Name, now, d.Health, d.WearPercent, d.Temperature, d.PowerOnHours, d.ReadErrors, d.WriteErrors)).ToList();
        var names = today.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var list = Load()
            .Where(s => now - s.At <= TimeSpan.FromDays(365))
            .Where(s => !(names.Contains(s.Name) && s.At.ToLocalTime().Date == now.ToLocalTime().Date))
            .Concat(today)
            .OrderBy(s => s.At)
            .ToList();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return list;
    }

    /// <summary>Resumo da evolução de um disco desde a primeira leitura guardada (null se só há uma).</summary>
    public static string? Trend(IReadOnlyList<DiskSnapshot> history, string name)
    {
        var mine = history.Where(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).OrderBy(s => s.At).ToList();
        if (mine.Count < 2) return null;
        var first = mine[0];
        var last = mine[^1];
        var parts = new List<string> { $"desde {first.At.ToLocalTime():dd/MM/yyyy}" };
        if (first.WearPercent is double w0 && last.WearPercent is double w1) parts.Add($"desgaste {w0:0}% → {w1:0}%");
        if (first.PowerOnHours is long h0 && last.PowerOnHours is long h1) parts.Add($"+{h1 - h0} h ligado");
        long errors = (last.ReadErrors ?? 0) + (last.WriteErrors ?? 0) - (first.ReadErrors ?? 0) - (first.WriteErrors ?? 0);
        if (errors > 0) parts.Add($"{errors} erro(s) novo(s) de leitura/escrita");
        if (!string.Equals(first.Health, last.Health, StringComparison.OrdinalIgnoreCase)) parts.Add($"saúde {first.Health} → {last.Health}");
        return string.Join(" · ", parts);
    }
}
