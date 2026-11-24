namespace MBExtreme.Core.Latency;

public sealed record DriverLatency(string Module, string Kind, long Count, double MaxMicroseconds, double AverageMicroseconds, double TotalMilliseconds);

/// <summary>
/// Agrupa os tempos de DPC e ISR (rotinas dos drivers que rodam em prioridade alta e atrasam o resto do sistema) por driver.
/// Tempos longos (acima de 1 ms) causam micro-travadas, picos de latência de áudio e perda de quadros.
/// </summary>
public sealed class DpcIsrAggregator
{
    private sealed class Stats
    {
        public long Count;
        public double MaxUs;
        public double TotalUs;
    }

    private readonly Dictionary<(string Kind, ulong Routine), Stats> _routines = new();
    private readonly List<(ulong Base, ulong End, string Name)> _images = new();

    public long TotalEvents { get; private set; }

    public void AddImage(ulong imageBase, ulong imageSize, string fileName)
    {
        if (imageBase == 0 || imageSize == 0) return;
        string name = Path.GetFileName(fileName.Replace('/', '\\').Replace("\\", Path.DirectorySeparatorChar.ToString()));
        _images.Add((imageBase, imageBase + imageSize, name));
    }

    public void AddEvent(string kind, ulong routine, double elapsedMicroseconds)
    {
        TotalEvents++;
        if (!_routines.TryGetValue((kind, routine), out var s)) _routines[(kind, routine)] = s = new Stats();
        s.Count++;
        s.TotalUs += elapsedMicroseconds;
        if (elapsedMicroseconds > s.MaxUs) s.MaxUs = elapsedMicroseconds;
    }

    public string ModuleOf(ulong address)
    {
        foreach (var (start, end, name) in _images)
            if (address >= start && address < end) return name;
        return "desconhecido";
    }

    public IReadOnlyList<DriverLatency> Top(int count)
    {
        return _routines
            .GroupBy(kv => (kv.Key.Kind, Module: ModuleOf(kv.Key.Routine)))
            .Select(g => new DriverLatency(g.Key.Module, g.Key.Kind, g.Sum(x => x.Value.Count), g.Max(x => x.Value.MaxUs),
                g.Sum(x => x.Value.TotalUs) / Math.Max(1, g.Sum(x => x.Value.Count)), g.Sum(x => x.Value.TotalUs) / 1000))
            .OrderByDescending(d => d.MaxMicroseconds)
            .Take(count)
            .ToList();
    }

    public static string Verdict(double maxMicroseconds) => maxMicroseconds switch
    {
        < 500 => "ótimo",
        < 1000 => "bom",
        < 2000 => "atenção",
        _ => "ruim",
    };

    /// <summary>Dica do que fazer, conforme o driver que aparece no topo da lista.</summary>
    public static string Advice(string module)
    {
        string m = module.ToLowerInvariant();
        if (m.Contains("nvlddmkm") || m.Contains("amdkmdag") || m.Contains("amdkmpfd") || m.Contains("igdkmd") || m.Contains("dxgkrnl"))
            return "Driver de vídeo. Atualize pela página Drivers; se persistir, reinstale limpo (modo de instalação limpa do fabricante).";
        if (m.Contains("ndis") || m.Contains("netwtw") || m.Contains("rt640") || m.Contains("e1d") || m.Contains("rtwlane") || m.Contains("athw") || m.Contains("wdiwifi"))
            return "Rede (placa de rede ou Wi-Fi). Atualize o driver da rede ou teste outra porta/cabo. Wi-Fi costuma gerar mais picos que cabo.";
        if (m.Contains("hdaudbus") || m.Contains("portcls") || m.Contains("rtkvhd") || m.Contains("hdaudio"))
            return "Áudio. Atualize o driver de áudio e desative melhorias de áudio e aplicativos de equalização.";
        if (m.Contains("stornvme") || m.Contains("storport") || m.Contains("nvme") || m.Contains("storahci") || m.Contains("iaStor".ToLowerInvariant()))
            return "Armazenamento (NVMe/SATA). Atualize o firmware do SSD e o driver de armazenamento/chipset.";
        if (m.Contains("usbxhci") || m.Contains("usbport") || m.Contains("ucx01000") || m.Contains("usbhub"))
            return "USB. Desconecte dispositivos USB desnecessários, troque de porta e desative a suspensão USB (ajuste em Jogos).";
        if (m.Contains("acpi") || m.Contains("wmi") || m.Contains("amdppm") || m.Contains("intelppm") || m.Contains("pci.sys"))
            return "ACPI/chipset/BIOS. Atualize a BIOS e o driver de chipset.";
        if (m.Contains("wdf01000") || m.Contains("ntoskrnl"))
            return "Núcleo do Windows ou framework de drivers: geralmente reflete outro driver. Repita o teste com programas de monitoramento fechados.";
        return "Atualize ou reinstale este driver; se for de um programa que você não precisa, desinstale o programa.";
    }
}
