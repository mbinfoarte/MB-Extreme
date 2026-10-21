using System.Text.Json;

namespace MBExtreme.Core.Settings;

/// <summary>Configurações do usuário, guardadas em %AppData%\MBExtreme\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>Nomes de processos que a limpeza de RAM nunca toca.</summary>
    public List<string> MemoryAllowlist { get; set; } = new();

    /// <summary>Programas (sem .exe) que contam como jogo no Modo Jogo.</summary>
    public List<string> GameProcesses { get; set; } = new();

    /// <summary>Programas (sem .exe) que o Modo Jogo pede para fechar quando um jogo abre.</summary>
    public List<string> BoostCloseApps { get; set; } = new();

    /// <summary>Liga o Modo Jogo sozinho quando um programa da lista de jogos abre.</summary>
    public bool BoostAuto { get; set; }

    public bool BoostSwitchPlan { get; set; } = true;

    public bool BoostCleanMemory { get; set; }

    private static string FolderPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme");

    private static string FilePath => Path.Combine(FolderPath, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Arquivo corrompido ou inacessível: começar com os padrões.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sem permissão para gravar: as configurações valem só nesta sessão.
        }
    }
}
