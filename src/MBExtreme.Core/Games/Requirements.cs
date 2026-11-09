using System.Globalization;
using System.Text.RegularExpressions;
using MBExtreme.Core.Hardware;

namespace MBExtreme.Core.Games;

/// <summary>O que o jogo pede, separado em itens que dá para comparar. Valores nulos são o que o texto não informou.</summary>
public sealed record RequirementSpec(string? Os, string? Cpu, string? Gpu, double? RamGb, double? VramGb, double? DiskGb, int? DirectX)
{
    public bool IsEmpty => Os is null && Cpu is null && Gpu is null && RamGb is null && VramGb is null && DiskGb is null && DirectX is null;
}

/// <summary>Lê o texto de requisitos da loja (em inglês ou português), mesmo com os itens na mesma linha.</summary>
public static class RequirementParser
{
    private static readonly Regex LabelRx = new(
        @"(?<![\p{L}])(?<label>OS|SO|Sistema operacional|Processor|Processador|CPU|Memory|Mem[óo]ria(?: RAM)?|RAM|Graphics|Gr[áa]ficos|Placa de v[íi]deo|Video Card|Placa gr[áa]fica|DirectX|Storage|Armazenamento|Hard Drive|Disco r[íi]gido|Espa[çc]o em disco|Additional Notes|Notas adicionais|Observa[çc][õo]es adicionais|Sound Card|Placa de som|Network|Rede)\s*:",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static RequirementSpec Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new RequirementSpec(null, null, null, null, null, null, null);
        var matches = LabelRx.Matches(text);
        string? os = null, cpu = null, gpu = null, memory = null, storage = null, directx = null;
        for (int i = 0; i < matches.Count; i++)
        {
            int from = matches[i].Index + matches[i].Length;
            int to = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            string value = Regex.Replace(text[from..to], @"\s+", " ").Trim().TrimEnd(',', ';');
            if (value.Length == 0) continue;
            switch (Field(matches[i].Groups["label"].Value))
            {
                case "os": os ??= value; break;
                case "cpu": cpu ??= value; break;
                case "gpu": gpu ??= value; break;
                case "ram": memory ??= value; break;
                case "disk": storage ??= value; break;
                case "dx": directx ??= value; break;
            }
        }

        return new RequirementSpec(os, cpu, gpu, memory is null ? null : Size(memory), gpu is null ? null : VideoMemory(gpu), storage is null ? null : Size(storage), DirectXVersion(directx));
    }

    private static string Field(string label)
    {
        string l = label.ToLowerInvariant();
        if (l is "os" or "so" or "sistema operacional") return "os";
        if (l is "processor" or "processador" or "cpu") return "cpu";
        if (l.StartsWith("mem") || l == "ram") return "ram";
        if (l is "graphics" or "gráficos" or "graficos" or "video card" || l.StartsWith("placa de v") || l.StartsWith("placa gr")) return "gpu";
        if (l == "directx") return "dx";
        if (l is "storage" or "armazenamento" or "hard drive" || l.StartsWith("disco") || l.StartsWith("espa")) return "disk";
        return "other";
    }

    private static double? Size(string text)
    {
        var m = Regex.Match(text, @"(\d+(?:[.,]\d+)?)\s*(TB|GB|MB|G\b)", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        double v = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        return m.Groups[2].Value.ToUpperInvariant() switch { "TB" => v * 1024, "MB" => v / 1024, _ => v };
    }

    private static double? VideoMemory(string gpuText)
    {
        var m = Regex.Match(gpuText, @"(\d+(?:[.,]\d+)?)\s*(GB|MB)\s*(?:de\s*)?(?:VRAM|video memory|mem[óo]ria de v[íi]deo|dedicated|dedicada)", RegexOptions.IgnoreCase);
        if (!m.Success) m = Regex.Match(gpuText, @"VRAM\s*[:\-]?\s*(\d+(?:[.,]\d+)?)\s*(GB|MB)", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        double v = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        return m.Groups[2].Value.Equals("MB", StringComparison.OrdinalIgnoreCase) ? v / 1024 : v;
    }

    private static int? DirectXVersion(string? text)
    {
        if (text is null) return null;
        var m = Regex.Match(text, @"(\d{1,2})");
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }
}

/// <summary>O seu PC, no que importa para comparar com os requisitos.</summary>
public sealed record MachineProfile(string CpuName, int Cores, int Threads, IReadOnlyList<string> GpuNames, double VramGb, double RamGb, int WindowsBuild, string WindowsName)
{
    public static MachineProfile From(HardwareInventory hw)
    {
        var main = hw.Gpus.OrderByDescending(g => g.VramBytes).FirstOrDefault();
        return new MachineProfile(hw.Cpu.Name.Trim(), hw.Cpu.Cores, hw.Cpu.Threads, hw.Gpus.Select(g => g.Name).ToList(),
            (main?.VramBytes ?? 0) / 1024.0 / 1024 / 1024, hw.TotalMemoryBytes / 1024.0 / 1024 / 1024, hw.Windows.Build, $"{hw.Windows.Edition} {hw.Windows.DisplayVersion}".Trim());
    }
}

public enum ItemStatus { Ok, Close, Below, Unknown, Info }

public sealed record RequirementItem(string Name, string Required, string Yours, ItemStatus Status, string? Note = null);

public enum Fit { Recommended, Minimum, Below, Unknown }

public sealed record RequirementReport(IReadOnlyList<RequirementItem> Minimum, IReadOnlyList<RequirementItem> Recommended, Fit Verdict, string Summary);

/// <summary>Compara o PC com os requisitos: números exatos para memória, vídeo e Windows; estimativa pelo nível do modelo para processador e placa.</summary>
public static class RequirementChecker
{
    private static readonly Regex Split = new(@"\s+or\s+|\s+ou\s+|/|;|\|", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static RequirementReport Check(string? minimumText, string? recommendedText, MachineProfile machine)
    {
        var min = RequirementParser.Parse(minimumText);
        var rec = RequirementParser.Parse(recommendedText);
        if (min.IsEmpty && rec.IsEmpty)
            return new RequirementReport(Array.Empty<RequirementItem>(), Array.Empty<RequirementItem>(), Fit.Unknown, "A loja não informou requisitos que dê para comparar.");

        var minItems = min.IsEmpty ? Array.Empty<RequirementItem>() : Items(min, machine);
        var recItems = rec.IsEmpty ? Array.Empty<RequirementItem>() : Items(rec, machine);
        var basis = minItems.Length > 0 ? minItems : recItems;
        int known = basis.Count(i => i.Status is ItemStatus.Ok or ItemStatus.Close or ItemStatus.Below);
        if (known == 0) return new RequirementReport(minItems, recItems, Fit.Unknown, "Não reconheci os componentes pedidos; compare na mão.");

        var below = basis.Where(i => i.Status == ItemStatus.Below).ToList();
        if (below.Count > 0)
            return new RequirementReport(minItems, recItems, Fit.Below, "Abaixo do mínimo em: " + string.Join(", ", below.Select(i => i.Name.ToLowerInvariant())) + ".");

        bool recommendedOk = recItems.Length > 0 && recItems.Count(i => i.Status is ItemStatus.Ok) >= 2 && recItems.All(i => i.Status is ItemStatus.Ok or ItemStatus.Info or ItemStatus.Unknown);
        if (recommendedOk) return new RequirementReport(minItems, recItems, Fit.Recommended, "Atende o recomendado.");

        var short_ = recItems.Where(i => i.Status is ItemStatus.Below or ItemStatus.Close).Select(i => i.Name.ToLowerInvariant()).ToList();
        var close = basis.Where(i => i.Status == ItemStatus.Close).Select(i => i.Name.ToLowerInvariant()).ToList();
        string summary = "Atende o mínimo" + (close.Count > 0 ? " por pouco (" + string.Join(", ", close) + ")" : "") +
            (short_.Count > 0 ? "; abaixo do recomendado em: " + string.Join(", ", short_) : "") + ".";
        return new RequirementReport(minItems, recItems, Fit.Minimum, summary);
    }

    private static RequirementItem[] Items(RequirementSpec spec, MachineProfile m)
    {
        var items = new List<RequirementItem>();
        if (spec.Os is { } os) items.Add(Os(os, m));
        if (spec.Cpu is { } cpu) items.Add(Cpu(cpu, m));
        if (spec.RamGb is { } ram) items.Add(Number("Memória", ram, m.RamGb, "GB de RAM"));
        if (spec.Gpu is { } gpu) items.Add(Gpu(gpu, m));
        if (spec.VramGb is { } vram) items.Add(m.VramGb > 0 ? Number("Memória de vídeo", vram, m.VramGb, "GB de VRAM") : new RequirementItem("Memória de vídeo", $"{vram:0.#} GB", "—", ItemStatus.Unknown));
        if (spec.DiskGb is { } disk) items.Add(new RequirementItem("Espaço em disco", $"{disk:0.#} GB", "não verificado", ItemStatus.Info, "Precisa de espaço livre para instalar; jogos já instalados não dependem disto."));
        return items.ToArray();
    }

    private static RequirementItem Number(string name, double required, double yours, string unit)
    {
        double ratio = required <= 0 ? 1 : yours / required;
        var status = ratio >= 0.95 ? ItemStatus.Ok : ratio >= 0.75 ? ItemStatus.Close : ItemStatus.Below;
        return new RequirementItem(name, $"{required:0.#} {unit}", $"{yours:0.#} {unit}", status);
    }

    private static RequirementItem Os(string text, MachineProfile m)
    {
        var w = Regex.Match(text, @"Windows\s*(11|10|8\.1|8|7|Vista|XP)", RegexOptions.IgnoreCase);
        if (!w.Success) return new RequirementItem("Sistema", text, m.WindowsName, ItemStatus.Unknown);
        int build = w.Groups[1].Value.ToLowerInvariant() switch { "11" => 22000, "10" => 10240, "8.1" => 9600, "8" => 9200, "7" => 7601, "vista" => 6000, _ => 2600 };
        return new RequirementItem("Sistema", $"Windows {w.Groups[1].Value}", m.WindowsName, m.WindowsBuild >= build ? ItemStatus.Ok : ItemStatus.Below);
    }

    private static RequirementItem Cpu(string text, MachineProfile m)
    {
        double? required = Lowest(text, HardwareTiers.CpuIndex);
        double? yours = HardwareTiers.CpuIndex(m.CpuName);
        if (required is null)
        {
            // sem modelo reconhecido: usa a quantidade de núcleos, se o texto pedir (ex.: "quad-core")
            int? cores = CoreCount(text);
            if (cores is { } need && m.Cores > 0)
                return new RequirementItem("Processador", text, $"{m.CpuName} ({m.Cores} núcleos)", m.Cores >= need ? ItemStatus.Ok : ItemStatus.Below, $"Só a quantidade de núcleos foi comparada ({need} pedidos).");
            return new RequirementItem("Processador", text, m.CpuName, ItemStatus.Unknown, "Modelo não reconhecido.");
        }
        if (yours is null) return new RequirementItem("Processador", text, m.CpuName, ItemStatus.Unknown, "Não conheço o seu processador para comparar.");
        return Compare("Processador", text, m.CpuName, required.Value, yours.Value);
    }

    private static RequirementItem Gpu(string text, MachineProfile m)
    {
        double? required = Lowest(text, HardwareTiers.GpuIndex);
        string yoursName = m.GpuNames.Count > 0 ? string.Join(" + ", m.GpuNames) : "—";
        double? yours = m.GpuNames.Select(HardwareTiers.GpuIndex).Where(v => v is not null).Select(v => v!.Value).DefaultIfEmpty().Max() is var best && best > 0 ? best : null;
        if (required is null) return new RequirementItem("Placa de vídeo", text, yoursName, ItemStatus.Unknown, "Modelo não reconhecido.");
        if (yours is null) return new RequirementItem("Placa de vídeo", text, yoursName, ItemStatus.Unknown, "Não conheço a sua placa para comparar.");
        return Compare("Placa de vídeo", text, yoursName, required.Value, yours.Value);
    }

    private static RequirementItem Compare(string name, string required, string yoursName, double requiredIndex, double yoursIndex)
    {
        double ratio = yoursIndex / requiredIndex;
        var status = ratio >= 0.92 ? ItemStatus.Ok : ratio >= 0.75 ? ItemStatus.Close : ItemStatus.Below;
        return new RequirementItem(name, required, yoursName, status, "Estimativa pelo nível do modelo, não é um teste.");
    }

    /// <summary>Entre alternativas ("A ou B"), qualquer uma serve: vale a mais fraca que reconhecemos.</summary>
    private static double? Lowest(string text, Func<string?, double?> index)
    {
        var values = Split.Split(text).Select(p => index(p.Trim())).Where(v => v is not null).Select(v => v!.Value).ToList();
        return values.Count == 0 ? null : values.Min();
    }

    private static int? CoreCount(string text)
    {
        var word = Regex.Match(text, @"\b(dual|quad|hexa|six|octa|eight|dois|quatro|seis|oito)[- ]?core", RegexOptions.IgnoreCase);
        if (word.Success) return word.Groups[1].Value.ToLowerInvariant() switch { "dual" or "dois" => 2, "quad" or "quatro" => 4, "hexa" or "six" or "seis" => 6, _ => 8 };
        var number = Regex.Match(text, @"(\d{1,2})[- ]?(?:core|núcleos|nucleos)", RegexOptions.IgnoreCase);
        return number.Success ? int.Parse(number.Groups[1].Value) : null;
    }
}
