using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Games;

/// <summary>Comandos da política de QoS (marca os pacotes do jogo com prioridade DSCP 46, a de voz/tempo real). Só os caracteres seguros de nome de programa são aceitos.</summary>
public static class QosCommands
{
    public const int Dscp = 46;
    private static readonly Regex Safe = new(@"^[A-Za-z0-9._() \-]{1,100}\.exe$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsSafeExe(string? exe) => exe is not null && Safe.IsMatch(exe) && !exe.Contains("..", StringComparison.Ordinal);

    public static string PolicyName(string exe) => "MBExtreme-" + exe[..^4].Replace(' ', '_');

    private static void Check(string exe)
    {
        if (!IsSafeExe(exe)) throw new ArgumentException("Nome de programa inválido para a política de QoS.", nameof(exe));
    }

    public static string Create(string exe)
    {
        Check(exe);
        return $"New-NetQosPolicy -Name '{PolicyName(exe)}' -AppPathNameMatchCondition '{exe}' -DSCPAction {Dscp} -NetworkProfile All -ErrorAction Stop | Out-Null";
    }

    public static string Remove(string exe)
    {
        Check(exe);
        return $"Remove-NetQosPolicy -Name '{PolicyName(exe)}' -Confirm:$false -ErrorAction Stop";
    }

    public static string Exists(string exe)
    {
        Check(exe);
        return $"@(Get-NetQosPolicy -Name '{PolicyName(exe)}' -ErrorAction SilentlyContinue).Count";
    }
}

/// <summary>
/// Política de QoS do Windows para o jogo. Só adianta se o roteador e a rede respeitarem a marcação (a maioria dos provedores ignora),
/// então o efeito é pequeno; é reversível e não mexe no jogo.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GameQosService
{
    private static (int Code, string Output, string Error) PowerShell(string script) =>
        Shell.Run("powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script);

    public bool? IsActive(string exe)
    {
        if (!QosCommands.IsSafeExe(exe)) return null;
        var (code, output, _) = PowerShell(QosCommands.Exists(exe));
        return code == 0 && int.TryParse(output.Trim(), out int n) ? n > 0 : null;
    }

    public void Apply(string exe)
    {
        if (IsActive(exe) == true) return;
        var (code, _, error) = PowerShell(QosCommands.Create(exe));
        if (code != 0) throw new InvalidOperationException("Não foi possível criar a política de QoS (execute como administrador): " + error.Trim());
    }

    public void Remove(string exe)
    {
        if (IsActive(exe) != true) return;
        var (code, _, error) = PowerShell(QosCommands.Remove(exe));
        if (code != 0) throw new InvalidOperationException("Não foi possível remover a política de QoS: " + error.Trim());
    }
}
