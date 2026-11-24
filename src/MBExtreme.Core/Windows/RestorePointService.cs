using System.Runtime.Versioning;

namespace MBExtreme.Core.Windows;

/// <summary>Cria um ponto de restauração do Windows antes de alterações maiores.</summary>
[SupportedOSPlatform("windows")]
public static class RestorePointService
{
    public static (bool Ok, string? Error) TryCreate(string description)
    {
        string safe = description.Replace("'", "");
        var result = Shell.PowerShell($"Checkpoint-Computer -Description '{safe}' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop");
        if (result.ExitCode == 0) return (true, null);

        string error = string.IsNullOrWhiteSpace(result.Error)
            ? "O Windows não criou o ponto de restauração (a proteção do sistema pode estar desligada)."
            : result.Error.Trim();
        return (false, error);
    }
}
