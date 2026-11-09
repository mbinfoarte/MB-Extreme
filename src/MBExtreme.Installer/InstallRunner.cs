using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using MBExtreme.Core.Setup;

namespace MBExtreme.Installer;

internal sealed record InstallOptions(string Directory, bool DesktopShortcut, bool StartMenuShortcut);

/// <summary>Copia o aplicativo (pacote embutido) para a pasta escolhida e integra ao Windows.</summary>
internal static class InstallRunner
{
    public static void Run(InstallOptions options, IProgress<(double Percent, string Message)> progress)
    {
        using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")
            ?? throw new InvalidOperationException("Este instalador não contém o aplicativo. Baixe o instalador completo de novo.");

        progress.Report((2, "Preparando a instalação"));
        string root = Path.GetFullPath(options.Directory).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        StopRunningApp(root);
        Directory.CreateDirectory(root);

        using var zip = new ZipArchive(payload, ZipArchiveMode.Read);
        long total = Math.Max(1, zip.Entries.Sum(e => e.Length));
        long done = 0;

        foreach (var entry in zip.Entries)
        {
            string target = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("O pacote contém um caminho inválido.");

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
            done += entry.Length;
            progress.Report((5 + 80.0 * done / total, $"Copiando {entry.Name}"));
        }

        string exe = Path.Combine(root, InstallInfo.ExecutableName);
        if (!File.Exists(exe))
            throw new InvalidOperationException("A instalação não gerou o programa principal.");

        progress.Report((88, "Registrando no Windows"));
        int sizeKb = (int)Math.Min(int.MaxValue, new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) / 1024);
        InstallInfo.RegisterUninstall(root.TrimEnd('\\'), sizeKb);

        progress.Report((94, "Criando atalhos"));
        if (options.StartMenuShortcut) InstallInfo.CreateShortcut(InstallInfo.StartMenuShortcut, exe, root);
        if (options.DesktopShortcut) InstallInfo.CreateShortcut(InstallInfo.DesktopShortcut, exe, root);

        progress.Report((100, "Instalação concluída"));
    }

    /// <summary>Fecha uma versão anterior em execução para poder substituir os arquivos.</summary>
    private static void StopRunningApp(string root)
    {
        foreach (var process in Process.GetProcessesByName("MBExtreme"))
        {
            try
            {
                string? path = process.MainModule?.FileName;
                if (path is not null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            finally { process.Dispose(); }
        }
    }
}
