using System.Diagnostics;
using System.Text.Json;

namespace MBExtreme.Core.Setup;

/// <summary>
/// Abrir o MB Extreme com o Windows, direto na bandeja (Painel MB e Modo jogo à mão). O programa pede administrador, então usa uma
/// tarefa agendada "ao entrar no Windows" com privilégio mais alto (assim não aparece a pergunta de permissão a cada início).
/// </summary>
public static class StartupTask
{
    public const string TaskName = "MB Extreme (bandeja)";
    public const string TrayArgument = "--tray";

    public static string CreateArguments(string exe) =>
        $"/Create /F /SC ONLOGON /RL HIGHEST /TN \"{TaskName}\" /TR \"\\\"{exe}\\\" {TrayArgument}\"";

    public static bool Enable(string exe) => Run(CreateArguments(exe));

    public static bool Disable() => Run($"/Delete /F /TN \"{TaskName}\"");

    public static bool IsEnabled() => Run($"/Query /TN \"{TaskName}\"");

    private static bool Run(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
            if (p is null) return false;
            p.StandardOutput.ReadToEnd();
            return p.WaitForExit(10000) && p.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
    }
}

/// <summary>Escolhas feitas no instalador, aplicadas pelo programa na primeira vez que abre (arquivo apagado depois).</summary>
public sealed record FirstRunOptions(bool Widget, bool TrayAtStartup)
{
    public const string FileName = "first-run.json";

    public string ToJson() => JsonSerializer.Serialize(this);

    public static FirstRunOptions? Parse(string json)
    {
        try { return JsonSerializer.Deserialize<FirstRunOptions>(json); }
        catch (JsonException) { return null; }
    }
}
