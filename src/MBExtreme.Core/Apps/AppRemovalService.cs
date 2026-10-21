using System.Runtime.Versioning;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Apps;

/// <summary>Lista e remove aplicativos da Microsoft Store do usuário atual.</summary>
[SupportedOSPlatform("windows")]
public static class AppRemovalService
{
    public static IReadOnlyList<string> InstalledPackageNames()
    {
        var result = Shell.PowerShell("Get-AppxPackage | Select-Object -ExpandProperty Name");
        if (result.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error)
                ? "Não foi possível listar os aplicativos instalados."
                : result.Error.Trim());

        return result.Output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }

    public static bool IsInstalled(BloatApp app, IReadOnlyList<string> installedNames) =>
        installedNames.Any(name => BloatCatalog.Matches(app.PackagePattern, name));

    /// <summary>Remove o aplicativo para o usuário atual. Para reinstalar, use a Microsoft Store ou o winget.</summary>
    public static void Remove(BloatApp app)
    {
        string pattern = app.PackagePattern.Replace("'", "");
        var result = Shell.PowerShell($"Get-AppxPackage -Name '{pattern}' | Remove-AppxPackage -ErrorAction Stop");
        if (result.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error)
                ? $"Não foi possível remover {app.Title}."
                : result.Error.Trim());
    }
}
