using System.Text.RegularExpressions;

namespace MBExtreme.Core.Apps;

/// <summary>Tipo do desinstalador, descoberto pelo comando que o programa deixou no Windows.</summary>
public enum UninstallerKind { Store, Quiet, Msi, Inno, Nsis, Unknown }

/// <summary>Como remover: o executável, os argumentos e se roda sem janela para clicar.</summary>
public sealed record UninstallPlan(UninstallerKind Kind, string File, string Arguments, bool Silent)
{
    /// <summary>Texto curto para a tela ("silencioso" ou o motivo de pedir confirmação).</summary>
    public string Label => Kind switch
    {
        UninstallerKind.Store => "Microsoft Store",
        UninstallerKind.Msi => "silencioso (MSI)",
        UninstallerKind.Inno => "silencioso (Inno Setup)",
        UninstallerKind.Nsis => "silencioso (NSIS)",
        UninstallerKind.Quiet => "silencioso",
        _ => "pede confirmação na janela dele",
    };
}

/// <summary>
/// Remoção sem cliques: monta o comando silencioso de cada tipo de instalador (MSI, Inno Setup, NSIS ou o comando silencioso
/// que o próprio programa informa). O que não for reconhecido abre a janela normal do fabricante. Nenhum comando reinicia o PC.
/// </summary>
public static class SilentUninstall
{
    private static readonly Regex Guid = new(@"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}", RegexOptions.Compiled);
    private static readonly Regex Inno = new(@"^unins\d{3}\.exe$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Nsis = new(@"^(uninstall|uninst|un_?install(er)?)\.exe$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static UninstallPlan? Plan(InstalledProgram program)
    {
        if (program.Source == ProgramSource.Store)
            return string.IsNullOrWhiteSpace(program.StoreFullName) ? null : new UninstallPlan(UninstallerKind.Store, "", program.StoreFullName, true);

        if (InstalledPrograms.SplitCommand(program.QuietUninstallCommand) is { } quiet && !NeedsRestartSwitch(quiet.Arguments))
            return new UninstallPlan(UninstallerKind.Quiet, quiet.File, quiet.Arguments, true);

        if (InstalledPrograms.SplitCommand(program.UninstallCommand) is not { } command) return null;
        return Plan(command.File, command.Arguments, program.RegistryKeyName);
    }

    /// <summary>Plano a partir do comando de desinstalação (separado para os testes).</summary>
    public static UninstallPlan Plan(string file, string arguments, string? registryKeyName = null)
    {
        string name = CommandLine.FileName(file.Trim().Trim('"'));
        if (name.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            string? code = Guid.Match(arguments).Success ? Guid.Match(arguments).Value
                : registryKeyName is not null && Guid.IsMatch(registryKeyName) ? Guid.Match(registryKeyName).Value : null;
            return code is null
                ? new UninstallPlan(UninstallerKind.Unknown, file, arguments, false)
                : new UninstallPlan(UninstallerKind.Msi, "msiexec.exe", $"/x {code} /qn /norestart", true);
        }
        if (Inno.IsMatch(name))
            return new UninstallPlan(UninstallerKind.Inno, file, Join(Strip(arguments, "/SILENT", "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART"), "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART"), true);
        if (Nsis.IsMatch(name))
        {
            // _?= faz o NSIS desinstalar no lugar, sem se copiar para a pasta temporária e sair antes de terminar
            string folder = CommandLine.Folder(file.Trim().Trim('"'));
            string args = Strip(arguments, "/S");
            if (!args.Contains("_?=", StringComparison.Ordinal) && folder.Length > 0) args = Join(args, "/S _?=" + folder);
            else args = Join(args, "/S");
            return new UninstallPlan(UninstallerKind.Nsis, file, args, true);
        }
        return new UninstallPlan(UninstallerKind.Unknown, file, arguments, false);
    }

    private static bool NeedsRestartSwitch(string arguments) =>
        arguments.Contains("/forcerestart", StringComparison.OrdinalIgnoreCase) || arguments.Contains("/forcereboot", StringComparison.OrdinalIgnoreCase);

    private static string Strip(string arguments, params string[] switches)
    {
        var parts = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !switches.Any(s => p.Equals(s, StringComparison.OrdinalIgnoreCase)));
        return string.Join(' ', parts);
    }

    private static string Join(string a, string b) => a.Length == 0 ? b : a + " " + b;

    /// <summary>
    /// Código de saída dos desinstaladores: 0 ok, 3010/1641 ok depois de reiniciar, 1602/1223 cancelado, 1605 "não está instalado"
    /// (conta como removido).
    /// </summary>
    public static (bool Ok, bool Reboot, bool Cancelled) ReadExit(int code) => code switch
    {
        0 or 1605 or 1614 => (true, false, false),
        3010 or 1641 => (true, true, false),
        1602 or 1223 => (false, false, true),
        _ => (false, false, false),
    };
}
