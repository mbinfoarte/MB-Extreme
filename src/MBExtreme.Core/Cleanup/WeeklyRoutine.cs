using System.Runtime.Versioning;
using System.Text;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Cleanup;

/// <summary>
/// Rotina semanal opcional: uma tarefa agendada do Windows abre o MB Extreme com --weekly, que faz a limpeza segura
/// (só as categorias marcadas por padrão) e grava o resultado em %AppData%\MBExtreme\weekly.log. Não reinicia nem mexe em ajustes.
/// </summary>
public static class WeeklyRoutine
{
    public const string TaskName = "MB Extreme - Limpeza semanal";
    public const string Argument = "--weekly";

    /// <summary>Argumentos do schtasks para criar a tarefa (domingo, 12:00 por padrão). O programa roda com o usuário atual.</summary>
    public static string[] CreateArguments(string exePath, DayOfWeek day = DayOfWeek.Sunday, string time = "12:00") => new[]
    {
        "/Create", "/F", "/TN", TaskName,
        "/TR", $"\"{exePath}\" {Argument}",
        "/SC", "WEEKLY", "/D", DayCode(day), "/ST", time,
        "/RL", "LIMITED",
    };

    public static string DayCode(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "MON", DayOfWeek.Tuesday => "TUE", DayOfWeek.Wednesday => "WED", DayOfWeek.Thursday => "THU",
        DayOfWeek.Friday => "FRI", DayOfWeek.Saturday => "SAT", _ => "SUN",
    };

    public static bool IsSafeExePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && path.IndexOfAny(new[] { '"', '%', '&', '|', '<', '>', '^' }) < 0;

    public static string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "weekly.log");

    /// <summary>Roda a limpeza segura e devolve o texto do resultado.</summary>
    public static string RunClean(DateTime nowUtc)
    {
        var sb = new StringBuilder();
        long total = 0;
        foreach (var category in CleanupCatalog.Create().Where(c => c.DefaultSelected))
        {
            try
            {
                var result = CleanupService.Clean(category, nowUtc);
                total += result.FreedBytes;
                sb.AppendLine($"{category.Title}: {result.FilesDeleted} arquivo(s), {result.FreedBytes / 1_048_576.0:0.0} MB");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                sb.AppendLine($"{category.Title}: não foi possível limpar ({ex.Message})");
            }
        }

        sb.Insert(0, $"[{nowUtc.ToLocalTime():dd/MM/yyyy HH:mm}] Limpeza semanal: {total / 1_048_576.0:0.0} MB liberados{Environment.NewLine}");
        return sb.ToString();
    }

    [SupportedOSPlatform("windows")]
    public static bool IsScheduled() => Shell.Run("schtasks.exe", "/Query", "/TN", TaskName).ExitCode == 0;

    [SupportedOSPlatform("windows")]
    public static (bool Ok, string Message) Schedule(string exePath)
    {
        if (!IsSafeExePath(exePath)) return (false, "Caminho do programa inválido.");
        var (code, _, error) = Shell.Run("schtasks.exe", CreateArguments(exePath));
        return code == 0 ? (true, "Limpeza semanal agendada para domingo às 12:00.") : (false, "Não foi possível agendar: " + error.Trim());
    }

    [SupportedOSPlatform("windows")]
    public static (bool Ok, string Message) Unschedule()
    {
        var (code, _, error) = Shell.Run("schtasks.exe", "/Delete", "/F", "/TN", TaskName);
        return code == 0 ? (true, "Agendamento removido.") : (false, "Não foi possível remover: " + error.Trim());
    }
}
