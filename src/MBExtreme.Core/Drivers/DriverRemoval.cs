using System.Runtime.Versioning;
using System.Text.Json;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Drivers;

/// <summary>Pacote de driver de vídeo instalado no Windows (um arquivo oemNN.inf do repositório de drivers).</summary>
public sealed record DisplayDriverPackage(string PublishedName, string Provider, string Version, string Date, GpuVendor Vendor);

public sealed record DriverRemovalStep(string Title, bool Ok, string Detail);

/// <summary>
/// Remoção completa do driver de vídeo (NVIDIA, AMD ou Intel), feita com as ferramentas do próprio Windows (pnputil) e a limpeza das
/// pastas que os instaladores deixam. Para limpeza ainda mais profunda do registro existe o DDU, programa de terceiros que o MB Extreme
/// não inclui, mas abre pela página oficial.
/// </summary>
public static class DriverRemoval
{
    public const string DduPage = "https://www.wagnardsoft.com/display-driver-uninstaller-DDU-";

    public static GpuVendor VendorOfProvider(string? provider)
    {
        string p = (provider ?? "").ToLowerInvariant();
        if (p.Contains("nvidia")) return GpuVendor.Nvidia;
        if (p.Contains("advanced micro") || p.Contains("amd") || p.Contains("ati tech")) return GpuVendor.Amd;
        if (p.Contains("intel")) return GpuVendor.Intel;
        return GpuVendor.Other;
    }

    /// <summary>Lê a saída JSON de Get-WindowsDriver (um objeto ou uma lista) e fica só com os drivers de vídeo.</summary>
    public static IReadOnlyList<DisplayDriverPackage> ParsePackages(string json)
    {
        var result = new List<DisplayDriverPackage>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new List<JsonElement> { doc.RootElement };
            foreach (var e in items)
            {
                string Str(string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) ? v.ToString() : "";
                string published = Str("Driver");
                if (!IsValidPublishedName(published)) continue;
                if (!string.Equals(Str("ClassName"), "Display", StringComparison.OrdinalIgnoreCase)) continue;
                var vendor = VendorOfProvider(Str("ProviderName"));
                result.Add(new DisplayDriverPackage(published, Str("ProviderName"), Str("Version"), Str("Date"), vendor));
            }
        }
        catch (JsonException) { }
        return result;
    }

    /// <summary>Só aceita nomes como oem12.inf (nunca caminhos), porque o nome vai para a linha de comando.</summary>
    public static bool IsValidPublishedName(string? name) =>
        name is { Length: >= 8 and <= 16 } && name.StartsWith("oem", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".inf", StringComparison.OrdinalIgnoreCase)
        && name[3..^4].All(char.IsDigit);

    /// <summary>Pastas que os instaladores deixam depois da remoção (só as do próprio fabricante, nunca pastas de outros programas).</summary>
    public static IReadOnlyList<string> LeftoverFolders(GpuVendor vendor, string systemDrive, string programData, string localAppData, string programFiles) => vendor switch
    {
        GpuVendor.Nvidia => new[]
        {
            Path.Combine(systemDrive + "\\", "NVIDIA"),
            Path.Combine(programData, "NVIDIA Corporation"),
            Path.Combine(programFiles, "NVIDIA Corporation"),
            Path.Combine(localAppData, "NVIDIA"),
            Path.Combine(localAppData, "NVIDIA Corporation"),
        },
        GpuVendor.Amd => new[]
        {
            Path.Combine(systemDrive + "\\", "AMD"),
            Path.Combine(localAppData, "AMD"),
            Path.Combine(programData, "AMD", "CN"),
        },
        GpuVendor.Intel => new[]
        {
            Path.Combine(systemDrive + "\\", "Intel"),
            Path.Combine(localAppData, "Intel", "ShaderCache"),
        },
        _ => Array.Empty<string>(),
    };

    public static bool IsSafeToDelete(string folder)
    {
        string full = Path.GetFullPath(folder).TrimEnd('\\', '/');
        string root = Path.GetPathRoot(full)?.TrimEnd('\\', '/') ?? "";
        if (full.Length <= root.Length + 1) return false;                       // raiz do disco
        string name = Path.GetFileName(full);
        return name is "NVIDIA" or "NVIDIA Corporation" or "AMD" or "Intel" or "ShaderCache" or "CN";
    }

    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<DisplayDriverPackage> ListPackages()
    {
        var run = Shell.PowerShellUtf8("Get-WindowsDriver -Online -All | Where-Object { $_.ClassName -eq 'Display' } | Select-Object Driver, ClassName, ProviderName, Version, Date | ConvertTo-Json -Compress");
        return ParsePackages(run.Output);
    }

    /// <summary>Remove os pacotes escolhidos e, se pedido, as pastas que sobram. Devolve um passo por item.</summary>
    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<DriverRemovalStep> Remove(IReadOnlyList<DisplayDriverPackage> packages, bool deleteLeftovers, GpuVendor leftoverVendor, bool pauseWindowsUpdateDrivers)
    {
        var steps = new List<DriverRemovalStep>();
        foreach (var pkg in packages)
        {
            if (!IsValidPublishedName(pkg.PublishedName)) { steps.Add(new(pkg.PublishedName, false, "Nome de pacote inválido.")); continue; }
            var run = Shell.Run("pnputil.exe", "/delete-driver", pkg.PublishedName, "/uninstall", "/force");
            steps.Add(new($"{pkg.Provider} {pkg.Version} ({pkg.PublishedName})", run.ExitCode is 0 or 3010, run.ExitCode == 3010 ? "Removido. Reinicie o PC." : run.ExitCode == 0 ? "Removido." : $"pnputil terminou com código {run.ExitCode}. {run.Output.Trim()}"));
        }

        if (deleteLeftovers)
        {
            string drive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
            var folders = LeftoverFolders(leftoverVendor, drive,
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            foreach (string folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                if (!IsSafeToDelete(folder)) { steps.Add(new(folder, false, "Pasta fora da lista segura; não apagada.")); continue; }
                try
                {
                    Directory.Delete(folder, true);
                    steps.Add(new(folder, true, "Pasta apagada."));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    steps.Add(new(folder, false, "Não foi possível apagar (em uso?). Feche o painel do fabricante e tente de novo: " + ex.Message));
                }
            }
        }

        if (pauseWindowsUpdateDrivers)
        {
            var run = Shell.Run("reg.exe", "add", @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "/v", "ExcludeWUDriversInQualityUpdate", "/t", "REG_DWORD", "/d", "1", "/f");
            steps.Add(new("Pausar drivers do Windows Update", run.ExitCode == 0, run.ExitCode == 0 ? "O Windows não instala driver de vídeo sozinho. Desfaça em Jogos e GPU quando quiser." : run.Error.Trim()));
        }

        return steps;
    }
}
