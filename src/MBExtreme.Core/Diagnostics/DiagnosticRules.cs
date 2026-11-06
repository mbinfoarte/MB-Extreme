using MBExtreme.Core.Storage;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Diagnostics;

/// <summary>As regras das verificações, sem tocar no Windows (para dar para testar).</summary>
public static class DiagnosticRules
{
    public static string Size(long bytes) => bytes >= 1L << 30
        ? (bytes / (double)(1L << 30)).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) + " GB"
        : Math.Max(1, bytes >> 20) + " MB";

    /// <summary>Espaço livre: abaixo de 5% é crítico, de 10% é problema, de 15% merece atenção.</summary>
    public static Finding? DiskSpace(VolumeInfo v, string systemDrive)
    {
        if (v.TotalBytes <= 0) return null;
        double free = 100.0 * v.FreeBytes / v.TotalBytes;
        bool system = v.Name.StartsWith(systemDrive, StringComparison.OrdinalIgnoreCase);
        var severity = free < 5 ? Severity.Critical : free < 10 ? Severity.Problem : free < 15 ? Severity.Warning : Severity.Ok;
        if (severity == Severity.Ok) return null;
        if (!system && severity == Severity.Critical) severity = Severity.Problem;   // disco de dados cheio não trava o Windows
        string name = v.Name.TrimEnd('\\');
        return new Finding("disk-space-" + name, $"Pouco espaço livre em {name}",
            $"{Size(v.FreeBytes)} livres de {Size(v.TotalBytes)} ({free:0}%).{(system ? " Com o disco do Windows quase cheio, atualizações falham e o PC fica lento." : "")}",
            severity, new FixPlan("Ver o que ocupa espaço", Page: "cleanup", ChangesSystem: false));
    }

    /// <summary>Arquivos descartáveis: a partir de 1 GB merece atenção; de 200 MB, só informação.</summary>
    public static Severity Junk(long bytes) => bytes >= 1L << 30 ? Severity.Warning : bytes >= 200L << 20 ? Severity.Info : Severity.Ok;

    public static Severity Startup(int enabled) => enabled >= 15 ? Severity.Problem : enabled >= 8 ? Severity.Warning : Severity.Ok;

    public static Severity Security(SecurityLevel level) => level switch { SecurityLevel.Bad => Severity.Problem, SecurityLevel.Warning => Severity.Warning, _ => Severity.Ok };

    /// <summary>Saúde do disco pelo que o próprio Windows informa (SMART). Sem o dado, não inventa nada.</summary>
    public static Finding? DiskHealth(DiskInfo d)
    {
        if (!string.Equals(d.Health, "Healthy", StringComparison.OrdinalIgnoreCase) && d.Health.Length > 0)
            return new Finding("disk-health-" + d.Name, $"Disco {d.Name} com saúde \"{d.Health}\"",
                "O próprio disco informa problema. Faça backup dos seus arquivos o quanto antes.", Severity.Critical,
                new FixPlan("Ver a saúde dos discos", Page: "storage", ChangesSystem: false));
        if (d.WearPercent is double wear && wear >= 90)
            return new Finding("disk-wear-" + d.Name, $"SSD {d.Name} perto do fim da vida útil", $"Desgaste de {wear:0}%. Planeje a troca e mantenha backup.", Severity.Problem,
                new FixPlan("Ver a saúde dos discos", Page: "storage", ChangesSystem: false));
        if (d.Temperature is double temp && temp >= 65)
            return new Finding("disk-temp-" + d.Name, $"Disco {d.Name} quente", $"{temp:0} °C. Acima de 65 °C o disco pode reduzir a velocidade.", Severity.Warning,
                new FixPlan("Ver a saúde dos discos", Page: "storage", ChangesSystem: false));
        return null;
    }

    /// <summary>Saída do "fsutil behavior query DisableDeleteNotify": TRIM ligado quando o NTFS mostra 0.</summary>
    public static bool? TrimEnabled(string fsutilOutput)
    {
        string? value = Tweaks.CommandParsers.FsutilValue(fsutilOutput, "DisableDeleteNotify");
        return value switch { "0" => true, "1" => false, _ => null };
    }

    /// <summary>Chaves que o Windows cria quando falta reiniciar para terminar uma atualização ou instalação.</summary>
    public static readonly string[] RebootKeys =
    {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired",
    };
}
