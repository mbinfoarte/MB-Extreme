using System.Globalization;
using System.Text.RegularExpressions;

namespace MBExtreme.Core.Storage;

/// <summary>Nota de saúde no estilo do CrystalDiskInfo: Bom, Atenção ou Ruim, com o motivo.</summary>
public enum DiskGrade { Good, Caution, Bad, Unknown }

/// <summary>Saúde do disco a partir do que o Windows e o SMART informam (setores, desgaste, temperatura e status).</summary>
public static class DiskHealth
{
    /// <summary>Lê os atributos SMART crus (bloco de 512 bytes do WMI): id → valor bruto (32 bits de baixo).</summary>
    public static IReadOnlyDictionary<int, long> ParseSmart(byte[] data)
    {
        var map = new Dictionary<int, long>();
        for (int i = 0; i < 30; i++)
        {
            int at = 2 + i * 12;
            if (at + 12 > data.Length) break;
            int id = data[at];
            if (id == 0) continue;
            long raw = data[at + 5] | (long)data[at + 6] << 8 | (long)data[at + 7] << 16 | (long)data[at + 8] << 24;
            map[id] = raw;
        }
        return map;
    }

    /// <summary>O nome do dispositivo no WMI ("SCSI\Disk&amp;Ven_&amp;Prod_Samsung_SSD_860...") é deste modelo?</summary>
    public static bool SameDisk(string instance, string model)
    {
        string inst = instance.ToUpperInvariant().Replace('_', ' ');
        var words = Regex.Split(model.ToUpperInvariant(), @"\s+").Where(w => w.Length >= 3).ToList();
        if (words.Count == 0 || !inst.Contains(words[0], StringComparison.Ordinal)) return false;
        return words.Count(w => inst.Contains(w, StringComparison.Ordinal)) >= Math.Min(2, words.Count);
    }

    public static (DiskGrade Grade, string Reason) Grade(DiskInfo d)
    {
        bool ssd = d.Media.Contains("SSD", StringComparison.OrdinalIgnoreCase) || d.Bus.Equals("NVMe", StringComparison.OrdinalIgnoreCase);
        if (d.Health.Length > 0 && !d.Health.Equals("Healthy", StringComparison.OrdinalIgnoreCase))
            return (DiskGrade.Bad, d.Health.Equals("Warning", StringComparison.OrdinalIgnoreCase) ? "o Windows avisa problema no disco" : "o Windows marca o disco como com defeito");
        if (d.Pending is > 0 || d.Uncorrectable is > 0)
            return (DiskGrade.Bad, $"setores defeituosos: {d.Pending ?? 0} pendentes, {d.Uncorrectable ?? 0} incorrigíveis · faça backup");
        if (d.Reallocated is > 50) return (DiskGrade.Bad, $"{d.Reallocated} setores realocados · faça backup");
        if (d.WearPercent is >= 90) return (DiskGrade.Bad, $"desgaste {d.WearPercent:0}% · fim da vida útil");
        if (d.Reallocated is > 0) return (DiskGrade.Caution, $"{d.Reallocated} setores realocados");
        if (d.WearPercent is >= 70) return (DiskGrade.Caution, $"desgaste {d.WearPercent:0}%");
        if (d.ReadErrors is > 0 || d.WriteErrors is > 0) return (DiskGrade.Caution, $"erros: leitura {d.ReadErrors ?? 0}, gravação {d.WriteErrors ?? 0}");
        if (d.Temperature is { } t && t >= (ssd ? 70 : 55)) return (DiskGrade.Caution, $"temperatura alta: {t:0} °C");
        if (d.Health.Length == 0) return (DiskGrade.Unknown, "o disco não informou a saúde");
        return (DiskGrade.Good, "sem setores ruins nem erros");
    }

    public static string GradeText(DiskGrade g) => g switch
    {
        DiskGrade.Good => "Bom", DiskGrade.Caution => "Atenção", DiskGrade.Bad => "Ruim", _ => "Sem dados",
    };

    public static string GradeBrush(DiskGrade g) => g switch
    {
        DiskGrade.Good => "OkBrush", DiskGrade.Caution => "WarnBrush", DiskGrade.Bad => "BadBrush", _ => "MutedBrush",
    };

    public static string Kind(DiskInfo d)
    {
        string media = d.Media.Contains("SSD", StringComparison.OrdinalIgnoreCase) ? "SSD" : d.Media.Contains("HDD", StringComparison.OrdinalIgnoreCase) ? "HD" : d.Media;
        return d.Bus.Length > 0 && !d.Bus.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? $"{media} {d.Bus}".Trim() : media;
    }
}

/// <summary>Teste de velocidade com o WinSAT do próprio Windows (sequencial e aleatório 4K, leitura e gravação).</summary>
public static class DiskBench
{
    public static IReadOnlyList<(string Title, string[] Args)> Tests(char drive)
    {
        string d = char.ToLowerInvariant(drive).ToString();
        if (!char.IsLetter(drive)) throw new ArgumentException("Unidade inválida.", nameof(drive));
        return new[]
        {
            ("Leitura sequencial", new[] { "disk", "-seq", "-read", "-drive", d }),
            ("Gravação sequencial", new[] { "disk", "-seq", "-write", "-drive", d }),
            ("Leitura aleatória 4K", new[] { "disk", "-ran", "-read", "-drive", d }),
            ("Gravação aleatória 4K", new[] { "disk", "-ran", "-write", "-drive", d }),
        };
    }

    /// <summary>Roda um teste do WinSAT e devolve a velocidade em MB/s (null se o Windows não mediu).</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static double? Measure(string[] args)
    {
        var (_, output, error) = MBExtreme.Core.Windows.Shell.Run("winsat.exe", args);
        return ParseMbps(output + "\n" + error);
    }

    /// <summary>Velocidade (MB/s) na saída do winsat ("Disk  Sequential 64.0 Read   1234.56 MB/s").</summary>
    public static double? ParseMbps(string output)
    {
        var matches = Regex.Matches(output, @"(\d+(?:[.,]\d+)?)\s*MB/s", RegexOptions.IgnoreCase);
        if (matches.Count == 0) return null;
        string v = matches[^1].Groups[1].Value.Replace(',', '.');
        return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double mbps) ? mbps : null;
    }
}
