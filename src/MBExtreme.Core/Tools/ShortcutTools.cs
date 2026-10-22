using System.Runtime.Versioning;

namespace MBExtreme.Core.Tools;

/// <summary>Atalhos (.lnk) cujo destino não existe mais.</summary>
[SupportedOSPlatform("windows")]
public static class ShortcutTools
{
    public static IReadOnlyList<string> DefaultFolders() => new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
    }.Where(f => f.Length > 0 && Directory.Exists(f)).ToList();

    public static string? TargetOf(string lnkPath)
    {
        try
        {
            Type? type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return null;
            dynamic shell = Activator.CreateInstance(type)!;
            try
            {
                dynamic link = shell.CreateShortcut(lnkPath);
                string target = link.TargetPath;
                return string.IsNullOrWhiteSpace(target) ? null : Environment.ExpandEnvironmentVariables(target);
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return null;
        }
    }

    /// <summary>Atalhos com destino em arquivo ou pasta que não existe. Atalhos para endereços, aplicativos da loja e itens sem destino ficam de fora.</summary>
    public static IReadOnlyList<string> Broken(IEnumerable<string> folders, CancellationToken token)
    {
        var broken = new List<string>();
        foreach (string folder in folders)
        {
            IEnumerable<string> links;
            try
            {
                links = Directory.EnumerateFiles(folder, "*.lnk", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            foreach (string link in links)
            {
                token.ThrowIfCancellationRequested();
                string? target = TargetOf(link);
                if (target is null) continue;
                // só conta como quebrado quando a unidade do destino existe, é local e está pronta (HD externo desconectado ou rede offline não contam)
                if (target.Length < 3 || target[1] != ':') continue;
                try
                {
                    var drive = new DriveInfo(target[..1]);
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                }
                catch (ArgumentException) { continue; }
                if (!File.Exists(target) && !Directory.Exists(target)) broken.Add(link);
            }
        }

        return broken;
    }
}
