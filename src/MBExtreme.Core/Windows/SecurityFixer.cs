using System.Runtime.Versioning;

namespace MBExtreme.Core.Windows;

/// <summary>Uma correção de segurança que o MB Extreme sabe aplicar.</summary>
public sealed record SecurityFix(string Id, string Title, string Detail, string Script, bool NeedsRestart = false);

public sealed record FixOutcome(SecurityFix Fix, bool Ok, string Message);

/// <summary>
/// Corrige o que a página de segurança apontou: liga o firewall, a proteção em tempo real e o controle de contas, e atualiza as definições de vírus.
/// Só mexe no que está desligado ou vencido; nunca desliga nada.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SecurityFixer
{
    public static IReadOnlyList<SecurityFix> Plan(SecurityStatus s)
    {
        var fixes = new List<SecurityFix>();

        if (s.FirewallDomain == false || s.FirewallPrivate == false || s.FirewallPublic == false)
            fixes.Add(new SecurityFix("firewall", "Ligar o Firewall do Windows",
                "Liga o firewall em todos os perfis de rede (domínio, privada e pública).",
                "Set-NetFirewallProfile -Profile Domain,Private,Public -Enabled True -ErrorAction Stop"));

        if (s.OtherAntivirus.Count == 0 && (s.RealTimeProtection == false || s.DefenderEnabled == false))
            fixes.Add(new SecurityFix("defender", "Ligar a proteção em tempo real",
                "Liga a proteção em tempo real do Microsoft Defender. Se o Windows estiver com a Proteção contra Adulteração ou uma política de empresa, pode recusar.",
                "Set-MpPreference -DisableRealtimeMonitoring $false -ErrorAction Stop"));

        if (s.SignatureAgeDays is > 7)
            fixes.Add(new SecurityFix("signatures", "Atualizar as definições de vírus",
                $"As definições têm {s.SignatureAgeDays} dias. Baixa as mais recentes pelo Windows Update.",
                "Update-MpSignature -ErrorAction Stop"));

        if (s.UacEnabled == false)
            fixes.Add(new SecurityFix("uac", "Ligar o Controle de Conta de Usuário",
                "Volta a pedir permissão antes de programas alterarem o sistema. Exige reiniciar o PC.",
                @"Set-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -Name EnableLUA -Value 1 -Type DWord -ErrorAction Stop",
                NeedsRestart: true));

        return fixes;
    }

    public static IReadOnlyList<FixOutcome> Apply(IReadOnlyList<SecurityFix> fixes)
    {
        var results = new List<FixOutcome>();
        foreach (var fix in fixes)
        {
            try
            {
                var run = Shell.PowerShellUtf8(fix.Script);
                results.Add(run.ExitCode == 0
                    ? new FixOutcome(fix, true, fix.NeedsRestart ? "Feito. Reinicie o PC para valer." : "Feito.")
                    : new FixOutcome(fix, false, Shorten(string.IsNullOrWhiteSpace(run.Error) ? "O Windows recusou a alteração." : run.Error)));
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                results.Add(new FixOutcome(fix, false, ex.Message));
            }
        }

        return results;
    }

    private static string Shorten(string text)
    {
        string line = text.Trim().Split('\n')[0].Trim();
        return line.Length > 200 ? line[..200] + "…" : line;
    }
}
