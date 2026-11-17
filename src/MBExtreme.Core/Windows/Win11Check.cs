using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MBExtreme.Core.Windows;

public enum ReqState { Ok, Fail, Unknown }

/// <summary>Um requisito do Windows 11: o que o PC tem, o que é exigido e se atende.</summary>
public sealed record Win11Requirement(string Title, string Have, string Needed, ReqState State, string Fix = "");

/// <summary>O que foi lido do PC (separado da avaliação para testar sem Windows).</summary>
public sealed record Win11Inputs(
    string CpuName, int LogicalCores, double Ghz, bool Is64Bit, bool Sse42, bool Popcnt,
    long RamBytes, long SystemDiskBytes, bool? Uefi, bool? SecureBootOn, string? TpmVersion, bool? TpmOn,
    int? DirectX, double? Wddm, string? GpuName, int ScreenWidth, int ScreenHeight, int CurrentBuild);

/// <summary>
/// Requisitos oficiais do Windows 11 (processador compatível, 64 bits, 2 núcleos a 1 GHz, SSE4.2 e POPCNT, 4 GB de memória,
/// 64 GB de disco, UEFI, Inicialização Segura, TPM 2.0, DirectX 12 com WDDM 2.0 e tela de 720p). Só lê, nada é alterado.
/// A lista de processadores segue as gerações da lista da Microsoft (Intel 8ª geração em diante, AMD Ryzen 2000 em diante, sem os de 1ª geração).
/// </summary>
public static class Win11Check
{
    private const double Gb = 1024.0 * 1024 * 1024;

    /// <summary>Processador na lista da Microsoft? null = não dá para saber pelo nome (confira na lista oficial).</summary>
    public static bool? CpuSupported(string name)
    {
        string n = Regex.Replace(name, @"\s+", " ").Trim();
        if (Regex.IsMatch(n, @"Core\(TM\) Ultra|Core Ultra|Snapdragon|Ryzen AI|Ryzen\s+Z\d", RegexOptions.IgnoreCase)) return true;
        // Intel Core i3/i5/i7/i9: 4 dígitos começando em 8 ou 9 (8ª e 9ª geração) ou 5 dígitos (10ª em diante)
        var intel = Regex.Match(n, @"\bi[3579]-(\d{4,5})", RegexOptions.IgnoreCase);
        if (intel.Success)
        {
            string digits = intel.Groups[1].Value;
            int generation = digits.Length == 5 ? int.Parse(digits[..2]) : digits[0] - '0';
            return generation >= 8;
        }
        if (Regex.IsMatch(n, @"Core\(TM\) [3579] \d{3}|Core [3579] \d{3}", RegexOptions.IgnoreCase)) return true;   // Core 5 120U etc.
        if (Regex.IsMatch(n, @"Core\(TM\)2|Core 2 |Pentium\(R\) (Dual|D)|Athlon\(tm\) (64|II)|Phenom|FX-\d{4}|\bA(4|6|8|10|12)-\d{4}", RegexOptions.IgnoreCase)) return false;
        // AMD Ryzen 3/5/7/9 NNNN: 1ª geração (1xxx) e as APUs 2xxxG/2xxxU (Zen 1) ficam de fora
        var ryzen = Regex.Match(n, @"Ryzen\s+(?:Threadripper\s+(?:PRO\s+)?)?(\d)?\s*(\d{4})([A-Z]{0,3})", RegexOptions.IgnoreCase);
        if (ryzen.Success)
        {
            string model = ryzen.Groups[2].Value;
            string suffix = ryzen.Groups[3].Value.ToUpperInvariant();
            int series = model[0] - '0';
            if (series <= 1) return false;
            if (series == 2 && (suffix.StartsWith('G') || suffix.StartsWith('U') || suffix.StartsWith('H'))) return false;
            return true;
        }
        return null;
    }

    public static IReadOnlyList<Win11Requirement> Evaluate(Win11Inputs i)
    {
        var list = new List<Win11Requirement>();
        bool? cpu = CpuSupported(i.CpuName);
        list.Add(new("Processador compatível", i.CpuName, "Modelo na lista da Microsoft (Intel 8ª geração ou mais nova, AMD Ryzen 2000 ou mais novo)",
            cpu switch { true => ReqState.Ok, false => ReqState.Fail, _ => ReqState.Unknown },
            cpu == false ? "Não tem ajuste: só trocando o processador (e geralmente a placa-mãe)." : cpu is null ? "Confira o modelo na lista oficial da Microsoft." : ""));
        list.Add(new("Processador: 64 bits, 2 núcleos, 1 GHz", $"{(i.Is64Bit ? "64 bits" : "32 bits")} · {i.LogicalCores} núcleos lógicos · {i.Ghz:0.0} GHz",
            "64 bits, 2 núcleos ou mais, 1 GHz ou mais", i.Is64Bit && i.LogicalCores >= 2 && i.Ghz >= 1 ? ReqState.Ok : ReqState.Fail));
        list.Add(new("Instruções SSE4.2 e POPCNT", $"SSE4.2: {(i.Sse42 ? "sim" : "não")} · POPCNT: {(i.Popcnt ? "sim" : "não")}",
            "As duas (exigidas desde o Windows 11 24H2)", i.Sse42 && i.Popcnt ? ReqState.Ok : ReqState.Fail));
        list.Add(new("Memória RAM", $"{i.RamBytes / Gb:0} GB", "4 GB ou mais", i.RamBytes >= 3.8 * Gb ? ReqState.Ok : ReqState.Fail,
            i.RamBytes < 3.8 * Gb ? "Coloque mais memória (um pente a mais costuma resolver)." : ""));
        list.Add(new("Armazenamento", $"Disco do Windows: {i.SystemDiskBytes / Gb:0} GB", "64 GB ou mais", i.SystemDiskBytes >= 60 * Gb ? ReqState.Ok : ReqState.Fail));
        list.Add(new("Firmware UEFI", i.Uefi switch { true => "UEFI", false => "BIOS antiga (Legacy/CSM)", _ => "não deu para ler" }, "UEFI (a BIOS moderna)",
            State(i.Uefi), i.Uefi == false ? "Dá para converter o disco para GPT (mbr2gpt) e ligar o UEFI na BIOS. Faça backup antes." : ""));
        list.Add(new("Inicialização Segura (Secure Boot)", i.SecureBootOn switch { true => "Ligada", false => "Desligada", _ => "não deu para ler" },
            "Capaz de usar a Inicialização Segura", i.SecureBootOn is null ? ReqState.Unknown : i.Uefi == false ? ReqState.Fail : ReqState.Ok,
            i.SecureBootOn == false ? "Desligada mas suportada em PCs UEFI: pode ligar na BIOS (Secure Boot) se quiser." : ""));
        bool tpm2 = i.TpmVersion is { } v && v.TrimStart().StartsWith("2", StringComparison.Ordinal);
        list.Add(new("TPM 2.0", i.TpmVersion is null ? "Não encontrado" : $"TPM {i.TpmVersion}{(i.TpmOn == false ? " · desligado" : " · ligado")}",
            "TPM versão 2.0", i.TpmVersion is null ? ReqState.Fail : tpm2 && i.TpmOn != false ? ReqState.Ok : ReqState.Fail,
            i.TpmVersion is null || i.TpmOn == false ? "Muitos PCs têm TPM desligado na BIOS: procure fTPM (AMD) ou PTT (Intel) e ligue." : ""));
        list.Add(new("Placa de vídeo: DirectX 12 e WDDM 2.0",
            $"{i.GpuName ?? "Placa de vídeo"} · DirectX {(i.DirectX?.ToString() ?? "?")} · WDDM {(i.Wddm?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "?")}",
            "DirectX 12 com driver WDDM 2.0 ou mais novo",
            i.DirectX is null || i.Wddm is null ? ReqState.Unknown : i.DirectX >= 12 && i.Wddm >= 2.0 ? ReqState.Ok : ReqState.Fail,
            i.Wddm is < 2.0 ? "Atualize o driver da placa de vídeo." : ""));
        list.Add(new("Tela", $"{i.ScreenWidth} × {i.ScreenHeight}", "720p (1280 × 720) ou mais", i.ScreenHeight >= 720 || i.ScreenWidth >= 1280 ? ReqState.Ok : ReqState.Fail));
        return list;
    }

    private static ReqState State(bool? value) => value switch { true => ReqState.Ok, false => ReqState.Fail, _ => ReqState.Unknown };

    /// <summary>Lê o "DDI Version" e o "Driver Model" do relatório do dxdiag.</summary>
    public static (int? DirectX, double? Wddm, string? Gpu) ParseDxDiag(string text)
    {
        int? dx = null;
        double? wddm = null;
        string? gpu = null;
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (gpu is null && line.StartsWith("Card name:", StringComparison.OrdinalIgnoreCase)) gpu = line[(line.IndexOf(':') + 1)..].Trim();
            var ddi = Regex.Match(line, @"^DDI Version:\s*(\d+)", RegexOptions.IgnoreCase);
            if (ddi.Success) dx = Math.Max(dx ?? 0, int.Parse(ddi.Groups[1].Value));
            var model = Regex.Match(line, @"^Driver Model:\s*WDDM\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (model.Success) wddm = Math.Max(wddm ?? 0, double.Parse(model.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        }
        return (dx, wddm, gpu);
    }

    // ---- leitura no Windows ----

    [DllImport("kernel32.dll")]
    private static extern bool GetFirmwareType(out int firmwareType);

    public static Win11Inputs Read(int screenWidth, int screenHeight, CancellationToken ct)
    {
        string cpu = SystemSummary.ProcessorName();
        double ghz = 0;
        using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
            if (key?.GetValue("~MHz") is int mhz) ghz = mhz / 1000.0;

        bool? uefi = null;
        try { if (GetFirmwareType(out int type)) uefi = type == 2; } catch (EntryPointNotFoundException) { }

        bool? secureBoot = null;
        using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State"))
            if (key?.GetValue("UEFISecureBootEnabled") is int sb) secureBoot = sb == 1;
        if (secureBoot is null && uefi == false) secureBoot = false;

        string? tpmVersion = null;
        bool? tpmOn = null;
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\CIMV2\Security\MicrosoftTpm", "SELECT SpecVersion, IsEnabled_InitialValue, IsActivated_InitialValue FROM Win32_Tpm");
            foreach (ManagementObject tpm in searcher.Get())
            {
                tpmVersion = (tpm["SpecVersion"] as string)?.Split(',')[0].Trim();
                tpmOn = tpm["IsEnabled_InitialValue"] as bool? ?? true;
            }
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException) { }

        int? dx = null; double? wddm = null; string? gpu = null;
        string file = Path.Combine(Path.GetTempPath(), $"mbx-dxdiag-{Environment.ProcessId}.txt");
        try
        {
            ct.ThrowIfCancellationRequested();
            Shell.Run("dxdiag.exe", "/whql:off", "/t", file);
            for (int t = 0; t < 60 && !File.Exists(file); t++) { ct.ThrowIfCancellationRequested(); Thread.Sleep(500); }
            if (File.Exists(file)) (dx, wddm, gpu) = ParseDxDiag(File.ReadAllText(file));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        finally { try { File.Delete(file); } catch (IOException) { } }

        long ram = Memory.MemoryService.GetStatus().TotalBytes;
        long disk = 0;
        try { disk = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\").TotalSize; } catch (IOException) { }
        int build = 0;
        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            int.TryParse(key?.GetValue("CurrentBuild") as string, out build);

        return new Win11Inputs(cpu, Environment.ProcessorCount, ghz, Environment.Is64BitOperatingSystem,
            Sse42.IsSupported, Popcnt.IsSupported, ram, disk, uefi, secureBoot, tpmVersion, tpmOn, dx, wddm, gpu, screenWidth, screenHeight, build);
    }
}
