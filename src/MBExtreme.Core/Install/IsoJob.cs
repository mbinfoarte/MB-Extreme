using MBExtreme.Core.Apps;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Install;

/// <summary>Andamento do criador: etapa atual, total e o texto.</summary>
public sealed record IsoProgress(int Step, int Total, string Text);

/// <summary>Pedido completo: ISO de origem, opções, destino e (se for pendrive) a letra dele.</summary>
public sealed record IsoRequest(string SourceIso, IsoOptions Options, IsoTarget Target, string OutputIso, char UsbLetter, string WorkFolder, string? DriversFolder);

/// <summary>
/// Executa o criador de ISO no Windows (precisa de administrador). Tudo numa pasta de trabalho; a ISO original só é lida.
/// Se algo falhar ou for cancelado, desmonta a imagem sem salvar e desmonta a ISO.
/// </summary>
public static class IsoJob
{
    public const int Total = 6;
    public const string WorkFolderName = "MB-ISO-trabalho";

    public static IReadOnlyList<WimEdition> ReadEditions(string iso)
    {
        string letter = Mount(iso);
        try
        {
            string wim = ImageFile($"{letter}:\\");
            var (_, output, _) = Shell.RunOem("dism.exe", "/English", "/Get-WimInfo", "/WimFile:" + wim);
            return IsoBuilder.ParseWimInfo(output);
        }
        finally { Shell.PowerShell(IsoBuilder.DismountScript(iso)); }
    }

    public static string Run(IsoRequest r, string oscdimg, IProgress<IsoProgress> progress, CancellationToken ct)
    {
        var log = new System.Text.StringBuilder();
        string media = Path.Combine(r.WorkFolder, "midia"), mount = Path.Combine(r.WorkFolder, "montagem"), wim = Path.Combine(r.WorkFolder, "install.wim");
        bool mounted = false, hiveSoft = false, isoMounted = false;
        void Step(int n, string text) { ct.ThrowIfCancellationRequested(); progress.Report(new IsoProgress(n, Total, text)); }
        (int, string, string) Do(string file, params string[] args)
        {
            ct.ThrowIfCancellationRequested();
            var res = Shell.RunOem(file, args);
            log.AppendLine($"> {file} {string.Join(' ', args)}").AppendLine(res.Output).AppendLine(res.Error);
            return res;
        }
        void Must((int Code, string Out, string Err) res, string what)
        {
            if (res.Code != 0) throw new InvalidOperationException($"{what} (código {res.Code}). {Last(res.Out + res.Err)}");
        }

        if (!Path.GetFileName(r.WorkFolder.TrimEnd('\\', '/')).Equals(WorkFolderName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Pasta de trabalho inválida.", nameof(r));
        try
        {
            if (Directory.Exists(r.WorkFolder)) Directory.Delete(r.WorkFolder, true);
            Directory.CreateDirectory(media);
            Directory.CreateDirectory(mount);

            Step(1, "Copiando os arquivos da ISO oficial...");
            string letter = Mount(r.SourceIso);
            isoMounted = true;
            var copy = Do("robocopy.exe", $"{letter}:\\", media, "/E", "/NFL", "/NDL", "/NJH", "/R:2", "/W:2");
            if (!IsoBuilder.RobocopyOk(copy.Item1)) throw new InvalidOperationException("Não foi possível copiar os arquivos da ISO (robocopy " + copy.Item1 + ").");
            Shell.PowerShell(IsoBuilder.DismountScript(r.SourceIso));
            isoMounted = false;
            foreach (var f in Directory.GetFiles(media, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);

            Step(2, "Separando a edição escolhida...");
            string source = ImageFile(media);
            var (_, info, _) = Do("dism.exe", "/English", "/Get-WimInfo", "/WimFile:" + source);
            var edition = IsoBuilder.Pick(IsoBuilder.ParseWimInfo(info), r.Options.EditionName)
                ?? throw new InvalidOperationException("A ISO não tem nenhuma edição do Windows.");
            Must(Do("dism.exe", "/English", "/Export-Image", "/SourceImageFile:" + source, "/SourceIndex:" + edition.Index, "/DestinationImageFile:" + wim, "/Compress:max", "/CheckIntegrity"), "O DISM não separou a edição");

            Step(3, "Montando a imagem...");
            Must(Do("dism.exe", "/English", "/Mount-Image", "/ImageFile:" + wim, "/Index:1", "/MountDir:" + mount), "O DISM não montou a imagem");
            mounted = true;

            Step(4, "Removendo apps e ajustando o Windows...");
            var (_, pkgs, _) = Do("dism.exe", "/English", "/Image:" + mount, "/Get-ProvisionedAppxPackages");
            foreach (var pkg in IsoBuilder.AppsToRemove(IsoBuilder.ParseProvisioned(pkgs), r.Options))
                Do("dism.exe", "/English", "/Image:" + mount, "/Remove-ProvisionedAppxPackage", "/PackageName:" + pkg);
            var tweaks = IsoBuilder.OfflineTweaks(r.Options);
            if (tweaks.Count > 0)
            {
                Must(Do("reg.exe", "load", IsoBuilder.OfflineSoftware, Path.Combine(mount, "Windows", "System32", "config", "SOFTWARE")), "Não foi possível abrir o registro da imagem");
                hiveSoft = true;
                foreach (var t in tweaks) Do("reg.exe", IsoBuilder.RegAdd(t).ToArray());
                UnloadHive(ref hiveSoft, log);
            }
            if (r.Options.AddDrivers && r.DriversFolder is { } drivers && Directory.Exists(drivers))
            {
                progress.Report(new IsoProgress(4, Total, "Colocando os drivers deste PC na imagem..."));
                Do("dism.exe", "/English", "/Image:" + mount, "/Add-Driver", "/Driver:" + drivers, "/Recurse");
            }
            if (IsoBuilder.NeedsFirstLogon(r.Options))
            {
                string scripts = Path.Combine(mount, "Windows", "Setup", "Scripts");
                Directory.CreateDirectory(scripts);
                var kit = r.Options.Kit.Length > 0 ? AppCatalog.InKit(r.Options.Kit).Select(a => a.Id) : Enumerable.Empty<string>();
                File.WriteAllText(Path.Combine(scripts, "mb-primeiro-login.cmd"), IsoBuilder.FirstLogonScript(r.Options, kit));
            }

            Step(5, "Salvando a imagem...");
            Must(Do("dism.exe", "/English", "/Unmount-Image", "/MountDir:" + mount, "/Commit"), "O DISM não salvou a imagem");
            mounted = false;
            foreach (var old in new[] { "install.wim", "install.esd" }) File.Delete(Path.Combine(media, "sources", old));
            if (r.Options.Unattended) File.WriteAllText(Path.Combine(media, "autounattend.xml"), IsoBuilder.Unattend(r.Options));

            if (r.Target == IsoTarget.IsoFile)
            {
                Step(6, "Gerando o arquivo .iso...");
                File.Move(wim, Path.Combine(media, "sources", "install.wim"));
                if (File.Exists(r.OutputIso)) File.Delete(r.OutputIso);
                Must(Do(oscdimg, IsoBuilder.OscdimgArgs(media, r.OutputIso, "MB_" + edition.Name).ToArray()), "O oscdimg não gerou a ISO");
                return log.ToString();
            }

            Step(6, "Gravando no pendrive...");
            var (code, outText, err) = Shell.PowerShell(IsoBuilder.PrepareUsbScript(r.UsbLetter));
            log.AppendLine(outText).AppendLine(err);
            string usbLetter = outText.Trim().Split('\n').LastOrDefault()?.Trim() ?? "";
            if (code != 0 || usbLetter.Length != 1) throw new InvalidOperationException("Não foi possível preparar o pendrive. " + Last(err));
            string usb = usbLetter + ":\\";
            var copyUsb = Do("robocopy.exe", media, usb, "/E", "/NFL", "/NDL", "/NJH", "/R:2", "/W:2");
            if (!IsoBuilder.RobocopyOk(copyUsb.Item1)) throw new InvalidOperationException("Não foi possível copiar para o pendrive (robocopy " + copyUsb.Item1 + ").");
            if (new FileInfo(wim).Length > IsoBuilder.Fat32MaxFile)
                Must(Do("dism.exe", "/English", "/Split-Image", "/ImageFile:" + wim, "/SWMFile:" + Path.Combine(usb, "sources", "install.swm"), "/FileSize:3800"), "O DISM não dividiu a imagem para o FAT32");
            else
                File.Copy(wim, Path.Combine(usb, "sources", "install.wim"), true);
            return log.ToString();
        }
        finally
        {
            if (hiveSoft) UnloadHive(ref hiveSoft, log);
            if (mounted) Shell.RunOem("dism.exe", "/English", "/Unmount-Image", "/MountDir:" + mount, "/Discard");
            if (isoMounted) Shell.PowerShell(IsoBuilder.DismountScript(r.SourceIso));
            try { if (Directory.Exists(r.WorkFolder) && !mounted) Directory.Delete(r.WorkFolder, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void UnloadHive(ref bool loaded, System.Text.StringBuilder log)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var (code, output, error) = Shell.RunOem("reg.exe", "unload", IsoBuilder.OfflineSoftware);
        log.AppendLine(output).AppendLine(error);
        loaded = code != 0 ? loaded : false;
        if (code != 0) { Thread.Sleep(2000); if (Shell.RunOem("reg.exe", "unload", IsoBuilder.OfflineSoftware).ExitCode == 0) loaded = false; }
    }

    private static string Mount(string iso)
    {
        var (code, output, error) = Shell.PowerShell(IsoBuilder.MountScript(iso));
        string letter = output.Trim().Split('\n').LastOrDefault()?.Trim() ?? "";
        if (code != 0 || letter.Length != 1) throw new InvalidOperationException("Não foi possível abrir a ISO. " + Last(error));
        return letter;
    }

    private static string ImageFile(string root)
    {
        foreach (var name in new[] { "install.wim", "install.esd" })
        {
            string path = Path.Combine(root, "sources", name);
            if (File.Exists(path)) return path;
        }
        throw new InvalidOperationException("Essa ISO não tem sources\\install.wim nem install.esd. Use uma ISO oficial do Windows 10 ou 11.");
    }

    private static string Last(string text) =>
        text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "";
}
