using System.Diagnostics;
using System.Text;

namespace MBExtreme.Core.Windows;

/// <summary>Executa programas do Windows (powercfg, sc, powershell...) e captura a saída.</summary>
internal static class Shell
{
    public static (int ExitCode, string Output, string Error) Run(string file, params string[] arguments) =>
        Execute(file, null, arguments);

    /// <summary>Como <see cref="Run"/>, mas lê a saída em UTF-8 (nomes com acento, winget).</summary>
    public static (int ExitCode, string Output, string Error) RunUtf8(string file, params string[] arguments) =>
        Execute(file, Encoding.UTF8, arguments);

    private static (int ExitCode, string Output, string Error) Execute(string file, Encoding? encoding, string[] arguments)
    {
        var info = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (encoding is not null)
        {
            info.StandardOutputEncoding = encoding;
            info.StandardErrorEncoding = encoding;
        }
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

    /// <summary>PowerShell com saída em UTF-8, para ler textos com acento ou JSON.</summary>
    public static (int ExitCode, string Output, string Error) PowerShellUtf8(string command) =>
        RunUtf8("powershell.exe", "-NoProfile", "-NonInteractive", "-Command",
            "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; " + command);
}
