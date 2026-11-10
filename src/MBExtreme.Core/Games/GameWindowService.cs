using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MBExtreme.Core.Games;

/// <summary>
/// Aplica o tipo de janela que precisa de arquivo ou registro: o FullscreenMode do Unreal (GameUserSettings.ini do usuário, com cópia)
/// e o "Desativar otimizações de tela cheia" do Windows (com o valor de antes guardado). Desfazer devolve tudo como estava.
/// </summary>
[SupportedOSPlatform("windows")]
public static class GameWindowService
{
    private static string BackupFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "backup", "game-ini");

    /// <summary>Unreal: grava o modo no GameUserSettings.ini (só se o arquivo já existir, isto é, o jogo já abriu uma vez). Devolve o arquivo mudado ou null.</summary>
    public static string? ApplyUnreal(InstalledGame game, GameWindowMode mode, GameSettings settings)
    {
        if (GameWindow.UnrealFullscreenMode(mode) is not { } value) return null;
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string? ini = settings.UnrealIni is { } known && File.Exists(known) ? known : GameWindow.UnrealIniCandidates(local, game.InstallPath).FirstOrDefault(File.Exists);
        if (ini is null) return null;
        string text = File.ReadAllText(ini);
        if (settings.UnrealIniBackup is null || !File.Exists(settings.UnrealIniBackup))
        {
            Directory.CreateDirectory(BackupFolder);
            string backup = Path.Combine(BackupFolder, CoverSearch.FileKey(game.Id) + ".ini");
            File.WriteAllText(backup, text);
            settings.UnrealIniBackup = backup;
        }
        settings.UnrealIni = ini;
        File.WriteAllText(ini, GameWindow.SetUnrealFullscreenMode(text, value));
        return ini;
    }

    public static bool RestoreUnreal(GameSettings settings)
    {
        if (settings.UnrealIni is not { } ini || settings.UnrealIniBackup is not { } backup || !File.Exists(backup)) return false;
        File.Copy(backup, ini, overwrite: true);
        File.Delete(backup);
        settings.UnrealIni = null;
        settings.UnrealIniBackup = null;
        return true;
    }

    /// <summary>Liga/desliga "Desativar otimizações de tela cheia" para o executável (registro do usuário, sem precisar de administrador).</summary>
    public static void SetFullscreenOptimizations(string exePath, bool disable, GameSettings settings)
    {
        using var key = Registry.CurrentUser.CreateSubKey(GameWindow.LayersKey, writable: true);
        string? now = key.GetValue(exePath) as string;
        if (disable)
        {
            if (!settings.FsoDisabled) settings.FsoBackup = now;
            key.SetValue(exePath, GameWindow.MergeLayers(now, GameWindow.DisableFsoFlag, true)!, RegistryValueKind.String);
            settings.FsoDisabled = true;
        }
        else
        {
            string? restored = settings.FsoDisabled ? settings.FsoBackup : GameWindow.MergeLayers(now, GameWindow.DisableFsoFlag, false);
            if (restored is null) key.DeleteValue(exePath, throwOnMissingValue: false);
            else key.SetValue(exePath, restored, RegistryValueKind.String);
            settings.FsoDisabled = false;
            settings.FsoBackup = null;
        }
    }

    /// <summary>Caminho do steam.exe (registro do usuário).</summary>
    public static string? SteamExe()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            string? exe = key?.GetValue("SteamExe") as string;
            return exe is { Length: > 0 } && File.Exists(exe) ? Path.GetFullPath(exe) : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException) { return null; }
    }
}
