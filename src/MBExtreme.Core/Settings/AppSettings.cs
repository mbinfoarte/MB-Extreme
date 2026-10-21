using System.Text.Json;

namespace MBExtreme.Core.Settings;

/// <summary>Configurações do usuário, guardadas em %AppData%\MBExtreme\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>Nomes de processos que a limpeza de RAM nunca toca.</summary>
    public List<string> MemoryAllowlist { get; set; } = new();

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
