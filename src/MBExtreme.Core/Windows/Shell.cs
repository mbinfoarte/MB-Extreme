using System.Diagnostics;

namespace MBExtreme.Core.Windows;

/// <summary>Executa programas do Windows (powercfg, sc, powershell...) e captura a saída.</summary>
internal static class Shell
{
    public static (int ExitCode, string Output, string Error) Run(string file, params string[] arguments)
    {
        var info = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"Não foi possível iniciar {file}.");
        var output = process.StandardOutput.ReadToEndAsync();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output.Result, error);
    }

    public static (int ExitCode, string Output, string Error) PowerShell(string command) =>
        Run("powershell.exe", "-NoProfile", "-NonInteractive", "-Command", command);
}
