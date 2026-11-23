using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Install;

/// <summary>Uma edição dentro do install.wim/esd (Home, Pro...).</summary>
public sealed record WimEdition(int Index, string Name, long SizeBytes);

/// <summary>Para onde vai o resultado: arquivo .iso ou pendrive (apaga o pendrive).</summary>
public enum IsoTarget { IsoFile, UsbDrive }

/// <summary>Tudo o que o técnico marca na tela Criar ISO. Também é o que fica salvo num modelo.</summary>
public sealed record IsoOptions
{
    public string EditionName { get; init; } = "Windows 11 Pro";
    public bool Unattended { get; init; } = true;
    public string LocalAccount { get; init; } = "Técnico";
    public bool BypassChecks { get; init; }
    public bool RemoveApps { get; init; } = true;
    public bool RemoveXbox { get; init; }
    public bool DisableTelemetry { get; init; } = true;
    public bool DisableCopilot { get; init; } = true;
    public bool KeepStore { get; init; } = true;
    public bool AddDrivers { get; init; } = true;
    public bool InstallMbExtreme { get; init; } = true;
    public string Kit { get; init; } = "essencial";   // "" = nenhum
    public bool GamerProfile { get; init; }
    public bool AutoPostInstall { get; init; }
}

/// <summary>Um modelo salvo (nome + opções + quantas vezes foi usado).</summary>
public sealed record IsoTemplate(string Name, IsoOptions Options, int Uses = 0);

/// <summary>Um passo do criador de ISO: um programa do Windows com os argumentos (nunca texto solto num shell).</summary>
public sealed record IsoStep(string Title, string File, IReadOnlyList<string> Arguments);

/// <summary>
/// Criador de ISO personalizada (plano Técnico). Parte sempre de uma ISO oficial da Microsoft e trabalha numa cópia:
/// exporta só a edição escolhida, tira os apps que vêm junto, desliga telemetria/Copilot no registro da imagem, põe os drivers do
/// Backup para formatar, grava o autounattend.xml e o script do primeiro login, e gera o .iso (oscdimg, do Windows ADK) ou grava
/// no pendrive (FAT32, com o install.wim dividido em partes de 3,8 GB). Não ativa o Windows: o PC usa a própria licença.
/// </summary>
public static class IsoBuilder
{
    public const string SetupUrl = "https://github.com/mbinfoarte/MB-Extreme-Download/releases/latest/download/MBExtreme-Setup.exe";
    public const string MicrosoftDownloadPage = "https://www.microsoft.com/pt-br/software-download/windows11";
    public const long Fat32MaxFile = 4L * 1024 * 1024 * 1024 - 1;
    public const string FirstRunMarker = @"C:\ProgramData\MBExtreme\primeira-execucao.json";

    // ---------- edições da ISO ----------

    /// <summary>Lê a saída de "dism /English /Get-WimInfo /WimFile:...".</summary>
    public static IReadOnlyList<WimEdition> ParseWimInfo(string output)
    {
        var list = new List<WimEdition>();
        int? index = null;
        string? name = null;
        long size = 0;
        void Flush()
        {
            if (index is { } i && !string.IsNullOrWhiteSpace(name)) list.Add(new WimEdition(i, name!.Trim(), size));
            index = null; name = null; size = 0;
        }
        foreach (var raw in output.Split('\n'))
        {
            string line = raw.Trim();
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            string key = line[..colon].Trim(), value = line[(colon + 1)..].Trim();
            switch (key)
            {
                case "Index":
                    Flush();
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) index = i;
                    break;
                case "Name": name = value; break;
                case "Size":
                    string digits = new(value.Where(char.IsDigit).ToArray());
                    if (long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out long s)) size = s;
                    break;
            }
        }
        Flush();
        return list;
    }

    /// <summary>A edição com esse nome, ou a Pro, ou a primeira.</summary>
    public static WimEdition? Pick(IReadOnlyList<WimEdition> editions, string name) =>
        editions.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? editions.FirstOrDefault(e => e.Name.EndsWith(" Pro", StringComparison.OrdinalIgnoreCase))
        ?? editions.FirstOrDefault();

    // ---------- apps que vêm junto ----------

    private static readonly string[] Bloat =
    {
        "Clipchamp.Clipchamp", "Microsoft.BingNews", "Microsoft.BingWeather", "Microsoft.BingSearch", "Microsoft.GetHelp", "Microsoft.Getstarted",
        "Microsoft.MicrosoftOfficeHub", "Microsoft.MicrosoftSolitaireCollection", "Microsoft.People", "Microsoft.PowerAutomateDesktop",
        "Microsoft.Todos", "Microsoft.WindowsFeedbackHub", "Microsoft.WindowsMaps", "Microsoft.549981C3F5F10", "MicrosoftTeams", "MSTeams",
        "Microsoft.OutlookForWindows", "Microsoft.Windows.DevHome", "Microsoft.MixedReality.Portal", "Microsoft.SkypeApp", "Microsoft.ZuneVideo",
    };
    private static readonly string[] Xbox = { "Microsoft.GamingApp", "Microsoft.XboxApp", "Microsoft.XboxGamingOverlay", "Microsoft.XboxGameOverlay", "Microsoft.XboxSpeechToTextOverlay", "Microsoft.XboxIdentityProvider", "Microsoft.Xbox.TCUI" };
    private static readonly string[] Store = { "Microsoft.WindowsStore", "Microsoft.StorePurchaseApp" };
    private static readonly string[] Copilot = { "Microsoft.Copilot", "Microsoft.Windows.Ai.Copilot.Provider" };

    /// <summary>Nunca sai: sem o App Installer não tem winget (o kit de programas depende dele).</summary>
    private static readonly string[] Never = { "Microsoft.DesktopAppInstaller", "Microsoft.VCLibs", "Microsoft.UI.Xaml", "Microsoft.NET", "Microsoft.SecHealthUI" };

    /// <summary>Nomes completos dos pacotes provisionados (saída de /Get-ProvisionedAppxPackages) que devem sair, conforme as opções.</summary>
    public static IReadOnlyList<string> AppsToRemove(IEnumerable<string> packageNames, IsoOptions options)
    {
        var prefixes = new List<string>();
        if (options.RemoveApps) prefixes.AddRange(Bloat);
        if (options.RemoveXbox) prefixes.AddRange(Xbox);
        if (!options.KeepStore) prefixes.AddRange(Store);
        if (options.DisableCopilot) prefixes.AddRange(Copilot);
        return packageNames
            .Where(p => !Never.Any(n => p.StartsWith(n + "_", StringComparison.OrdinalIgnoreCase) || p.StartsWith(n + ".", StringComparison.OrdinalIgnoreCase)))
            .Where(p => prefixes.Any(x => p.StartsWith(x + "_", StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Lê os "PackageName : ..." de "dism /English /Image:... /Get-ProvisionedAppxPackages".</summary>
    public static IReadOnlyList<string> ParseProvisioned(string output) =>
        output.Split('\n').Select(l => l.Trim())
            .Where(l => l.StartsWith("PackageName", StringComparison.Ordinal) && l.Contains(':'))
            .Select(l => l[(l.IndexOf(':') + 1)..].Trim())
            .Where(n => n.Length > 0)
            .ToList();

    // ---------- registro da imagem (offline) ----------

    public const string OfflineSoftware = @"HKLM\MB_SOFTWARE";
    public const string OfflineSystem = @"HKLM\MB_SYSTEM";

    /// <summary>Valores (chave, nome, DWORD) gravados no SOFTWARE da imagem montada.</summary>
    public static IReadOnlyList<(string Key, string Name, int Value)> OfflineTweaks(IsoOptions options)
    {
        var list = new List<(string, string, int)>();
        string pol = OfflineSoftware + @"\Policies\Microsoft\Windows";
        if (options.DisableTelemetry)
        {
            list.Add((pol + @"\DataCollection", "AllowTelemetry", 0));
            list.Add((pol + @"\CloudContent", "DisableWindowsConsumerFeatures", 1));
            list.Add((pol + @"\CloudContent", "DisableSoftLanding", 1));
            list.Add((pol + @"\AdvertisingInfo", "DisabledByGroupPolicy", 1));
            list.Add((OfflineSoftware + @"\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0));
        }
        if (options.DisableCopilot)
        {
            list.Add((pol + @"\WindowsCopilot", "TurnOffWindowsCopilot", 1));
            list.Add((pol + @"\WindowsAI", "DisableAIDataAnalysis", 1));
            list.Add((pol + @"\WindowsAI", "AllowRecallEnablement", 0));
        }
        return list;
    }

    public static IReadOnlyList<string> RegAdd((string Key, string Name, int Value) t) =>
        new[] { "add", t.Key, "/v", t.Name, "/t", "REG_DWORD", "/d", t.Value.ToString(CultureInfo.InvariantCulture), "/f" };

    // ---------- autounattend.xml ----------

    private static readonly Regex AccountPattern = new(@"^[\p{L}\p{N} ._\-]{1,20}$", RegexOptions.Compiled);

    public static string? ValidateAccount(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Digite o nome da conta local.";
        if (!AccountPattern.IsMatch(name)) return "Use até 20 letras, números, espaço, ponto, hífen ou sublinhado no nome da conta.";
        string[] reserved = { "administrator", "administrador", "guest", "convidado", "system", "defaultaccount" };
        if (reserved.Contains(name.Trim().ToLowerInvariant())) return "Esse nome é reservado pelo Windows. Escolha outro.";
        return null;
    }

    /// <summary>
    /// Gera o autounattend.xml. Pula idioma, região, contrato e conta Microsoft (cria a conta local sem senha), mas deixa a tela de
    /// disco para o técnico escolher onde instalar: apagar disco no automático é arriscado demais quando o PC tem mais de um disco.
    /// </summary>
    public static string Unattend(IsoOptions options)
    {
        const string arch = "processorArchitecture=\"amd64\" publicKeyToken=\"31bf3856ad364e35\" language=\"neutral\" versionScope=\"nonSxS\" xmlns:wcm=\"http://schemas.microsoft.com/WMIConfig/2002/State\"";
        string account = SecurityElement.Escape(options.LocalAccount.Trim()) ?? "Tecnico";
        var x = new StringBuilder();
        x.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        x.AppendLine("<unattend xmlns=\"urn:schemas-microsoft-com:unattend\">");
        x.AppendLine("  <settings pass=\"windowsPE\">");
        x.AppendLine($"    <component name=\"Microsoft-Windows-International-Core-WinPE\" {arch}>");
        x.AppendLine("      <SetupUILanguage><UILanguage>pt-BR</UILanguage></SetupUILanguage>");
        x.AppendLine("      <InputLocale>0416:00010416</InputLocale><SystemLocale>pt-BR</SystemLocale><UILanguage>pt-BR</UILanguage><UserLocale>pt-BR</UserLocale>");
        x.AppendLine("    </component>");
        x.AppendLine($"    <component name=\"Microsoft-Windows-Setup\" {arch}>");
        if (options.BypassChecks)
        {
            x.AppendLine("      <RunSynchronous>");
            int order = 1;
            foreach (var value in new[] { "BypassTPMCheck", "BypassSecureBootCheck", "BypassRAMCheck", "BypassCPUCheck" })
                x.AppendLine($"        <RunSynchronousCommand wcm:action=\"add\"><Order>{order++}</Order><Path>reg.exe add HKLM\\SYSTEM\\Setup\\LabConfig /v {value} /t REG_DWORD /d 1 /f</Path></RunSynchronousCommand>");
            x.AppendLine("      </RunSynchronous>");
        }
        // a imagem só tem a edição escolhida (índice 1); sem chave, o Windows ativa sozinho com a licença do PC (digital ou da BIOS)
        x.AppendLine("      <ImageInstall><OSImage><InstallFrom><MetaData wcm:action=\"add\"><Key>/IMAGE/INDEX</Key><Value>1</Value></MetaData></InstallFrom></OSImage></ImageInstall>");
        x.AppendLine("      <UserData><AcceptEula>true</AcceptEula><ProductKey><Key></Key><WillShowUI>OnError</WillShowUI></ProductKey></UserData>");
        x.AppendLine("    </component>");
        x.AppendLine("  </settings>");
        x.AppendLine("  <settings pass=\"specialize\">");
        x.AppendLine($"    <component name=\"Microsoft-Windows-Shell-Setup\" {arch}><TimeZone>E. South America Standard Time</TimeZone></component>");
        x.AppendLine("  </settings>");
        x.AppendLine("  <settings pass=\"oobeSystem\">");
        x.AppendLine($"    <component name=\"Microsoft-Windows-International-Core\" {arch}>");
        x.AppendLine("      <InputLocale>0416:00010416</InputLocale><SystemLocale>pt-BR</SystemLocale><UILanguage>pt-BR</UILanguage><UserLocale>pt-BR</UserLocale>");
        x.AppendLine("    </component>");
        x.AppendLine($"    <component name=\"Microsoft-Windows-Shell-Setup\" {arch}>");
        x.AppendLine("      <OOBE><HideEULAPage>true</HideEULAPage><HideOEMRegistrationScreen>true</HideOEMRegistrationScreen><HideOnlineAccountScreens>true</HideOnlineAccountScreens><ProtectYourPC>3</ProtectYourPC></OOBE>");
        x.AppendLine("      <UserAccounts><LocalAccounts><LocalAccount wcm:action=\"add\">");
        x.AppendLine($"        <Name>{account}</Name><DisplayName>{account}</DisplayName><Group>Administrators</Group><Password><Value></Value><PlainText>true</PlainText></Password>");
        x.AppendLine("      </LocalAccount></LocalAccounts></UserAccounts>");
        x.AppendLine($"      <AutoLogon><Enabled>true</Enabled><LogonCount>1</LogonCount><Username>{account}</Username><Password><Value></Value><PlainText>true</PlainText></Password></AutoLogon>");
        if (NeedsFirstLogon(options))
            x.AppendLine("      <FirstLogonCommands><SynchronousCommand wcm:action=\"add\"><Order>1</Order><CommandLine>cmd.exe /c C:\\Windows\\Setup\\Scripts\\mb-primeiro-login.cmd</CommandLine><Description>MB Extreme</Description></SynchronousCommand></FirstLogonCommands>");
        x.AppendLine("    </component>");
        x.AppendLine("  </settings>");
        x.AppendLine("</unattend>");
        return x.ToString();
    }

    public static bool NeedsFirstLogon(IsoOptions o) => o.InstallMbExtreme || o.Kit.Length > 0 || o.GamerProfile || o.AutoPostInstall;

    // ---------- primeiro login ----------

    private static readonly Regex WingetId = new(@"^[A-Za-z0-9][A-Za-z0-9.\-_+]*$", RegexOptions.Compiled);

    /// <summary>Script do primeiro login: espera a internet e o winget, instala o kit, baixa e abre o instalador do MB Extreme.</summary>
    public static string FirstLogonScript(IsoOptions options, IEnumerable<string> kitIds)
    {
        var s = new StringBuilder();
        s.AppendLine("@echo off");
        s.AppendLine("title MB Extreme - preparando o Windows");
        s.AppendLine("echo Preparando o Windows (MB Extreme). Nao feche esta janela.");
        s.AppendLine("set /a tries=0");
        s.AppendLine(":wait");
        s.AppendLine("ping -n 1 www.microsoft.com >nul 2>&1 && where winget >nul 2>&1 && goto ready");
        s.AppendLine("set /a tries+=1");
        s.AppendLine("if %tries% GEQ 60 goto ready");
        s.AppendLine("timeout /t 10 /nobreak >nul");
        s.AppendLine("goto wait");
        s.AppendLine(":ready");
        foreach (var id in kitIds.Where(i => WingetId.IsMatch(i)).Distinct(StringComparer.OrdinalIgnoreCase))
            s.AppendLine($"winget install -e --id {id} --silent --accept-package-agreements --accept-source-agreements --disable-interactivity");
        if (options.GamerProfile || options.AutoPostInstall)
        {
            s.AppendLine("mkdir \"C:\\ProgramData\\MBExtreme\" >nul 2>&1");
            s.AppendLine($"echo {FirstRunJson(options)}> \"{FirstRunMarker}\"");
        }
        if (options.InstallMbExtreme)
        {
            s.AppendLine($"curl.exe -L --retry 5 -o \"%TEMP%\\MBExtreme-Setup.exe\" \"{SetupUrl}\"");
            s.AppendLine("if exist \"%TEMP%\\MBExtreme-Setup.exe\" start \"\" \"%TEMP%\\MBExtreme-Setup.exe\"");
        }
        s.AppendLine("del \"%~f0\"");
        return s.ToString().Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
    }

    public static string FirstRunJson(IsoOptions o) =>
        JsonSerializer.Serialize(new FirstRun(o.GamerProfile ? "gamer" : "", o.AutoPostInstall));

    public sealed record FirstRun(string Profile, bool PostInstall);

    public static FirstRun? ReadFirstRun(string json)
    {
        try { return JsonSerializer.Deserialize<FirstRun>(json.Trim()); }
        catch (JsonException) { return null; }
    }

    // ---------- gerar ----------

    /// <summary>Onde o oscdimg (Windows ADK, Ferramentas de Implantação) costuma ficar.</summary>
    public static IEnumerable<string> OscdimgCandidates(string programFilesX86, string appFolder) => new[]
    {
        Path.Combine(appFolder, "tools", "oscdimg.exe"),
        Path.Combine(programFilesX86, "Windows Kits", "10", "Assessment and Deployment Kit", "Deployment Tools", "amd64", "Oscdimg", "oscdimg.exe"),
    };

    /// <summary>Comando do winget que instala só as Ferramentas de Implantação do ADK (onde vem o oscdimg).</summary>
    public static IReadOnlyList<string> AdkInstallArgs() => new[]
    {
        "install", "-e", "--id", "Microsoft.WindowsADK", "--accept-package-agreements", "--accept-source-agreements",
        "--override", "/features OptionId.DeploymentTools /quiet /norestart",
    };

    /// <summary>Argumentos do oscdimg para uma ISO que dá boot em BIOS e UEFI.</summary>
    public static IReadOnlyList<string> OscdimgArgs(string root, string output, string label)
    {
        string bios = Path.Combine(root, "boot", "etfsboot.com"), uefi = Path.Combine(root, "efi", "microsoft", "boot", "efisys.bin");
        return new[] { "-m", "-o", "-u2", "-udfver102", "-l" + CleanLabel(label), $"-bootdata:2#p0,e,b{bios}#pEF,e,b{uefi}", root, output };
    }

    public static string CleanLabel(string label)
    {
        string clean = new(label.ToUpperInvariant().Select(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' ? c : '_').ToArray());
        return clean.Length == 0 ? "MB_WIN" : clean[..Math.Min(32, clean.Length)];
    }

    public static string DefaultOutputName(string editionName) =>
        "MB-" + Regex.Replace(editionName.Replace("Windows ", "Win", StringComparison.OrdinalIgnoreCase), @"[^A-Za-z0-9]+", "-").Trim('-') + ".iso";

    /// <summary>Espaço que a pasta de trabalho precisa: a ISO copiada + a imagem montada + folga.</summary>
    public static long WorkSpaceNeeded(long isoBytes) => isoBytes * 3 + 4L * 1024 * 1024 * 1024;

    /// <summary>Texto entre aspas simples para o PowerShell (aspas simples dobradas).</summary>
    public static string Ps(string text) => "'" + text.Replace("'", "''") + "'";

    /// <summary>Monta a ISO e devolve a letra (saída: uma letra).</summary>
    public static string MountScript(string iso) => $"(Mount-DiskImage -ImagePath {Ps(iso)} -PassThru | Get-Volume).DriveLetter";

    public static string DismountScript(string iso) => $"Dismount-DiskImage -ImagePath {Ps(iso)} | Out-Null";

    /// <summary>Apaga o pendrive (só se for USB e não for o disco do Windows), cria FAT32 e devolve a letra nova.</summary>
    public static string PrepareUsbScript(char letter, string label = "MB-WIN")
    {
        char l = char.ToUpperInvariant(letter);
        if (l is < 'D' or > 'Z') throw new ArgumentException("Letra de pendrive inválida.", nameof(letter));
        return
            $"$n=(Get-Partition -DriveLetter {l}).DiskNumber; $d=Get-Disk -Number $n; " +
            "if ($d.BusType -ne 'USB' -or $d.IsBoot -or $d.IsSystem) { throw 'Esse disco não é um pendrive USB.' }; " +
            "Clear-Disk -Number $n -RemoveData -RemoveOEM -Confirm:$false; Initialize-Disk -Number $n -PartitionStyle MBR -ErrorAction SilentlyContinue; " +
            "$size=[Math]::Min((Get-Disk -Number $n).LargestFreeExtent, 32GB); " +
            $"$p=New-Partition -DiskNumber $n -Size $size -IsActive -AssignDriveLetter; Format-Volume -Partition $p -FileSystem FAT32 -NewFileSystemLabel '{CleanLabel(label)[..Math.Min(11, CleanLabel(label).Length)]}' -Confirm:$false | Out-Null; " +
            "(Get-Partition -DiskNumber $n -PartitionNumber $p.PartitionNumber).DriveLetter";
    }

    /// <summary>Robocopy: 0 a 7 é sucesso (8 ou mais é erro).</summary>
    public static bool RobocopyOk(int code) => code is >= 0 and < 8;

    public static string Sha256(string file, CancellationToken ct = default)
    {
        using var stream = File.OpenRead(file);
        using var sha = System.Security.Cryptography.SHA256.Create();
        var buffer = new byte[4 * 1024 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            sha.TransformBlock(buffer, 0, read, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    // ---------- modelos ----------

    public static string TemplatesFile(string appData) => Path.Combine(appData, "MBExtreme", "iso-modelos.json");

    public static IReadOnlyList<IsoTemplate> LoadTemplates(string file)
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<List<IsoTemplate>>(File.ReadAllText(file)) ?? new() : new(); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return Array.Empty<IsoTemplate>(); }
    }

    /// <summary>Salva (ou substitui pelo nome) um modelo.</summary>
    public static IReadOnlyList<IsoTemplate> SaveTemplate(IReadOnlyList<IsoTemplate> list, IsoTemplate template)
    {
        var next = list.Where(t => !string.Equals(t.Name, template.Name, StringComparison.CurrentCultureIgnoreCase)).ToList();
        var old = list.FirstOrDefault(t => string.Equals(t.Name, template.Name, StringComparison.CurrentCultureIgnoreCase));
        next.Insert(0, template with { Uses = old?.Uses ?? template.Uses });
        return next;
    }

    public static IReadOnlyList<IsoTemplate> CountUse(IReadOnlyList<IsoTemplate> list, string name) =>
        list.Select(t => string.Equals(t.Name, name, StringComparison.CurrentCultureIgnoreCase) ? t with { Uses = t.Uses + 1 } : t).ToList();

    public static void WriteTemplates(string file, IReadOnlyList<IsoTemplate> list)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Resumo de uma linha do modelo ("Win11 Pro + perfil Gamer").</summary>
    public static string Summary(IsoOptions o)
    {
        var parts = new List<string> { o.EditionName.Replace("Windows ", "Win", StringComparison.OrdinalIgnoreCase) };
        if (o.Kit.Length > 0) parts.Add("kit " + (Apps.AppCatalog.Kits.FirstOrDefault(k => k.Key == o.Kit)?.Title ?? o.Kit));
        if (o.GamerProfile) parts.Add("perfil Gamer");
        if (o.BypassChecks) parts.Add("sem TPM");
        return string.Join(" + ", parts);
    }
}
