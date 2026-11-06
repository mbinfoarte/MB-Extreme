using System.Globalization;
using System.Text.RegularExpressions;

namespace MBExtreme.Core.Repair;

/// <summary>Tempo limite de uma etapa de reparo e o que fazer quando ele passa.</summary>
public sealed record StepTimeout(TimeSpan Limit, bool Stop);

/// <summary>
/// Limites de tempo dos reparos. Comandos comuns param quando passam do limite; o DISM, o SFC e o CHKDSK nunca são interrompidos
/// à força (cortar no meio pode deixar o Windows pior): passando do tempo normal, o programa só avisa que está demorando.
/// </summary>
public static class RepairTimeouts
{
    public static StepTimeout For(StepKind kind, int genericMinutes) => kind switch
    {
        StepKind.Dism or StepKind.DismCheck => new StepTimeout(TimeSpan.FromMinutes(60), Stop: false),
        StepKind.Sfc => new StepTimeout(TimeSpan.FromMinutes(45), Stop: false),
        StepKind.Chkdsk or StepKind.ChkdskSchedule => new StepTimeout(TimeSpan.FromMinutes(45), Stop: false),
        _ => new StepTimeout(TimeSpan.FromMinutes(Math.Clamp(genericMinutes, 1, 120)), Stop: true),
    };

    public static string SlowWarning(StepTimeout t) =>
        $"  Está demorando mais que o normal ({(int)t.Limit.TotalMinutes} min). Não é recomendado interromper: deixe terminar ou cancele sabendo que o reparo fica pela metade.";

    public static string StoppedVerdict(StepTimeout t) => $"Passou do limite de {(int)t.Limit.TotalMinutes} min e foi interrompido.";
}

/// <summary>Relógio do Windows: diferença para um servidor de hora e se está num ano que faz sentido.</summary>
public static class ClockCheck
{
    private static readonly Regex Offset = new(@"([+-]\d+[.,]\d+)s", RegexOptions.Compiled);

    /// <summary>
    /// Lê a diferença (em segundos) da saída do "w32tm /stripchart ... /dataonly". A linha de dado é "hh:mm:ss, +00.1234567s"
    /// em qualquer idioma; a mensagem de erro do w32tm não tem esse formato.
    /// </summary>
    public static double? ParseOffset(string output)
    {
        foreach (Match m in Offset.Matches(output))
            if (double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) return value;
        return null;
    }

    /// <summary>Mais de 5 min errado quebra sites seguros, o Windows Update e a ativação; mais de 1 min já vale ajustar.</summary>
    public static Diagnostics.Severity Severity(double offsetSeconds) => Math.Abs(offsetSeconds) switch
    {
        >= 300 => Diagnostics.Severity.Problem,
        >= 60 => Diagnostics.Severity.Warning,
        _ => Diagnostics.Severity.Ok,
    };

    /// <summary>Ano impossível (bateria da placa-mãe fraca): o PC acha que está no passado.</summary>
    public static bool YearLooksWrong(DateTime now) => now.Year < 2024 || now.Year > 2040;

    public static string Describe(double seconds)
    {
        double abs = Math.Abs(seconds);
        string amount = abs >= 3600 ? $"{abs / 3600:0.#} h" : abs >= 60 ? $"{abs / 60:0.#} min" : $"{abs:0} s";
        return seconds > 0 ? $"{amount} atrasado" : $"{amount} adiantado";
    }
}

/// <summary>Regras do diagnóstico do Windows Update.</summary>
public static class UpdateCheck
{
    /// <summary>Serviços que o Windows Update precisa. "Disabled" quebra as atualizações.</summary>
    public static readonly (string Name, string Title)[] Services =
    {
        ("wuauserv", "Windows Update"),
        ("bits", "Serviço de Transferência Inteligente em Segundo Plano (BITS)"),
        ("cryptsvc", "Serviços de Criptografia"),
        ("TrustedInstaller", "Instalador de Módulos do Windows"),
    };

    /// <summary>Sem atualização instalada há mais de 45 dias merece atenção; há mais de 90, é problema.</summary>
    public static Diagnostics.Severity Age(int days) => days >= 90 ? Diagnostics.Severity.Problem : days >= 45 ? Diagnostics.Severity.Warning : Diagnostics.Severity.Ok;

    /// <summary>Data no formato ISO ("o") que o PowerShell devolve; vazio = nunca instalou nada pelo Windows Update.</summary>
    public static DateTimeOffset? ParseDate(string output)
    {
        string text = output.Trim().Split('\n').LastOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;
    }
}
