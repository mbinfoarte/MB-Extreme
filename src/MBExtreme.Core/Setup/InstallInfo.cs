using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MBExtreme.Core.Setup;

/// <summary>Dados e rotinas de instalação compartilhados entre o instalador e o aplicativo (desinstalação).</summary>
[SupportedOSPlatform("windows")]
public static class InstallInfo
{
    public const string AppName = "MB Extreme";
    public const string Publisher = "MB InfoArt";
    public const string Version = "0.2.0";
    public const string ExecutableName = "MBExtreme.exe";
    public const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MBExtreme";

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);

    public static string StartMenuShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName + ".lnk");

    public static string DesktopShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), AppName + ".lnk");

    /// <summary>Registra o programa em "Aplicativos instalados" do Windows.</summary>
    public static void RegisterUninstall(string installDirectory, int estimatedSizeKb)
    {
        string exe = Path.Combine(installDirectory, ExecutableName);
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = root.CreateSubKey(UninstallKeyPath, true)
            ?? throw new InvalidOperationException("Não foi possível registrar o programa no Windows.");
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", Version);
        key.SetValue("Publisher", Publisher);
        key.SetValue("InstallLocation", installDirectory);
        key.SetValue("DisplayIcon", exe);
        key.SetValue("UninstallString", $"\"{exe}\" /uninstall");
        key.SetValue("EstimatedSize", estimatedSizeKb, RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    public static void CreateShortcut(string shortcutPath, string target, string workingDirectory)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Não foi possível criar atalhos neste Windows.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic link = shell.CreateShortcut(shortcutPath);
        link.TargetPath = target;
        link.WorkingDirectory = workingDirectory;
        link.Description = AppName + " - " + Publisher;
        link.IconLocation = target + ",0";
        link.Save();
    }

    /// <summary>Remove atalhos e o registro. Os arquivos da pasta de instalação são removidos por quem chama.</summary>
    public static void RemoveIntegration()
    {
        foreach (var shortcut in new[] { StartMenuShortcut, DesktopShortcut })
        {
            try { if (File.Exists(shortcut)) File.Delete(shortcut); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        root.DeleteSubKeyTree(UninstallKeyPath, false);
    }

    /// <summary>Evita apagar uma pasta que não seja claramente a do MB Extreme.</summary>
    public static bool IsSafeInstallDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return false;
        string full = Path.GetFullPath(directory).TrimEnd('\\', '/');
        if (Path.GetPathRoot(full)?.TrimEnd('\\', '/') == full) return false;
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\', '/');
        if (full.Equals(windows, StringComparison.OrdinalIgnoreCase)) return false;
        return File.Exists(Path.Combine(full, ExecutableName));
    }
}
