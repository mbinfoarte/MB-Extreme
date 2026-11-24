using MBExtreme.Core.Settings;

namespace MBExtreme.Core.Gamepads;

/// <summary>Como o controle estava ligado durante a medição.</summary>
public enum PadLink { Usb, Bluetooth }

/// <summary>
/// Resultado de uma medição do Analisador: taxa, média entre pacotes, variação, picos e pacotes perdidos.
/// A média sozinha engana (o Bluetooth pode mandar mais pacotes e mesmo assim engasgar); por isso o veredito
/// pesa a variação e os picos, que são o que se sente no jogo.
/// </summary>
public sealed record PadLatencyReport(PadLink Link, int Reports, double RateHz, double AverageMs, double JitterMs, double WorstMs,
    int Spikes, int Lost, IReadOnlyList<double> Gaps)
{
    /// <summary>Intervalo acima disso conta como pico (engasgo perceptível).</summary>
    public const double SpikeMs = 8;

    /// <summary>
    /// Calcula a partir dos horários (ms) em que cada pacote chegou. Pausas acima de 250 ms são o controle parado e não contam.
    /// <paramref name="countLost"/> = false quando a leitura só vê pacotes que mudaram algo (XInput): aí intervalo grande não é perda.
    /// </summary>
    public static PadLatencyReport? From(PadLink link, IReadOnlyList<double> timestampsMs, bool countLost = true)
    {
        if (timestampsMs.Count < 10) return null;
        var gaps = new List<double>(timestampsMs.Count - 1);
        for (int i = 1; i < timestampsMs.Count; i++)
        {
            double gap = timestampsMs[i] - timestampsMs[i - 1];
            if (gap > 0 && gap < 250) gaps.Add(gap);
        }
        if (gaps.Count < 5) return null;
        double avg = gaps.Average();
        double jitter = Math.Sqrt(gaps.Sum(g => (g - avg) * (g - avg)) / gaps.Count);
        double median = Median(gaps);
        int spikes = gaps.Count(g => g > SpikeMs && g > median * 2);
        int lost = 0;
        if (countLost && median > 0)
            foreach (double g in gaps)
                if (g > median * 2.5) lost += (int)Math.Round(g / median) - 1;
        return new PadLatencyReport(link, timestampsMs.Count, Math.Round(1000 / avg, 1), Math.Round(avg, 2), Math.Round(jitter, 2),
            Math.Round(gaps.Max(), 2), spikes, lost, gaps);
    }

    /// <summary>Nota (menor é melhor): média + o dobro da variação + castigo por pico e por perda.</summary>
    public double Score => AverageMs + 2 * JitterMs + Spikes * 0.25 + Lost * 0.25;

    /// <summary>Faixa para a tela: 0 = excelente, 1 = bom, 2 = bom com picos, 3 = instável.</summary>
    public int Level => JitterMs <= 0.6 && Spikes == 0 && AverageMs <= 2.1 ? 0
        : JitterMs <= 1.5 && Spikes <= 2 && AverageMs <= 8.5 ? 1
        : JitterMs <= 4 && AverageMs <= 10 ? 2
        : 3;

    public string Quality => Level switch
    {
        0 => "EXCELENTE",
        1 => "BOM",
        2 => "BOM, COM PICOS",
        _ => "INSTÁVEL",
    };

    /// <summary>Barras do gráfico: o tempo até o próximo pacote, resumido em <paramref name="count"/> barras (cada uma guarda o pior do trecho, para os picos aparecerem).</summary>
    public IReadOnlyList<double> Bars(int count = 48)
    {
        if (Gaps.Count == 0 || count <= 0) return Array.Empty<double>();
        if (Gaps.Count <= count) return Gaps.ToList();
        var bars = new double[count];
        for (int i = 0; i < count; i++)
        {
            int from = i * Gaps.Count / count, to = Math.Max(from + 1, (i + 1) * Gaps.Count / count);
            double worst = 0;
            for (int j = from; j < to && j < Gaps.Count; j++) worst = Math.Max(worst, Gaps[j]);
            bars[i] = worst;
        }
        return bars;
    }

    /// <summary>Para guardar nas configurações (o gráfico fica com até 120 intervalos).</summary>
    public PadMeasureRecord ToRecord(DateTime when) => new()
    {
        When = when, RateHz = RateHz, AverageMs = AverageMs, JitterMs = JitterMs, WorstMs = WorstMs, Spikes = Spikes, Lost = Lost,
        Gaps = Bars(120).Select(g => Math.Round(g, 2)).ToList(),
    };

    public static PadLatencyReport FromRecord(PadLink link, PadMeasureRecord r) =>
        new(link, r.Gaps.Count + 1, r.RateHz, r.AverageMs, r.JitterMs, r.WorstMs, r.Spikes, r.Lost, r.Gaps);

    public static string RecordKey(ushort product, PadLink link) => $"{product:X4}|{link}";

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}

/// <summary>O que está em volta da conexão e pode piorar o resultado (para as dicas do Analisador).</summary>
public sealed record PadLinkContext(int BluetoothAudioDevices = 0, bool WifiOn24GHz = false, int BluetoothRadios = 1, bool BehindHub = false,
    bool UsbSuspendOn = false);

/// <summary>Compara cabo e Bluetooth e explica a escolha para quem não é técnico.</summary>
public static class PadAnalyzer
{
    /// <summary>A conexão mais estável das duas medidas (null se nenhuma foi medida). Empate técnico fica com o cabo.</summary>
    public static PadLink? Best(PadLatencyReport? usb, PadLatencyReport? bt)
    {
        if (usb is null && bt is null) return null;
        if (usb is null) return PadLink.Bluetooth;
        if (bt is null) return PadLink.Usb;
        return bt.Score < usb.Score * 0.85 ? PadLink.Bluetooth : PadLink.Usb;
    }

    public static string Verdict(PadLatencyReport? usb, PadLatencyReport? bt)
    {
        var best = Best(usb, bt);
        if (best is null) return "Meça no cabo e no Bluetooth para comparar.";
        if (usb is null || bt is null)
        {
            var only = usb ?? bt!;
            string other = only.Link == PadLink.Usb ? "no Bluetooth" : "no cabo USB";
            return $"{Name(only.Link)}: {only.Quality.ToLowerInvariant()}. Meça também {other} para saber qual é melhor para este controle.";
        }
        if (best == PadLink.Usb)
        {
            string why = bt.RateHz > usb.RateHz
                ? $"O Bluetooth manda mais pacotes ({bt.RateHz:0} Hz), mas varia {bt.JitterMs:0.0} ms e teve {bt.Spikes} picos de até {bt.WorstMs:0} ms; no cabo a variação é de {usb.JitterMs:0.0} ms."
                : $"No cabo a média é {usb.AverageMs:0.0} ms com variação de {usb.JitterMs:0.0} ms; no Bluetooth, {bt.AverageMs:0.0} ms com variação de {bt.JitterMs:0.0} ms.";
            return "Para este controle, use o cabo USB. " + why;
        }
        return $"Para este controle, o Bluetooth está melhor: média de {bt.AverageMs:0.0} ms, variação de {bt.JitterMs:0.0} ms e {bt.Spikes} picos. Confira se o cabo está numa porta direta (sem hub) e meça de novo.";
    }

    /// <summary>Dicas pelo resultado e pelo que está em volta, da mais importante para a menos.</summary>
    public static IReadOnlyList<string> Tips(PadLatencyReport report, PadLinkContext context)
    {
        var tips = new List<string>();
        if (report.Link == PadLink.Bluetooth)
        {
            if (context.BluetoothAudioDevices > 0)
                tips.Add($"{context.BluetoothAudioDevices} fone(s) ou caixa(s) de som no Bluetooth junto com o controle: dividem o rádio e causam picos. Use o fone no cabo ou desligue-o.");
            if (context.WifiOn24GHz)
                tips.Add("O Wi-Fi está em 2,4 GHz, a mesma faixa do Bluetooth. Ligue na rede de 5 GHz do roteador se puder.");
            if (context.BluetoothRadios > 1)
                tips.Add("Há mais de um adaptador Bluetooth no PC. Desligue o interno para o controle usar só o adaptador novo (Controle competitivo › Avançado).");
            if (report.Spikes > 0)
                tips.Add("Deixe o adaptador à vista do controle, com um cabo extensor, longe de portas USB 3.0 em uso e do roteador.");
        }
        else
        {
            if (context.BehindHub) tips.Add("O controle está ligado num hub ou na frente do gabinete. Use uma porta traseira, direto na placa-mãe (veja a aba Portas USB).");
            if (report.RateHz < 300) tips.Add("Pelo cabo, o DualSense e o DualShock 4 ficam em 250 Hz de fábrica. A aba Overclock 1000 Hz leva para 1000 Hz.");
        }
        if (context.UsbSuspendOn) tips.Add("O Windows ainda pode pôr o USB para dormir. Aplique o Controle competitivo (Básico) para desligar isso.");
        if (report.Lost > 0) tips.Add($"{report.Lost} pacotes não chegaram. Troque de porta ou de cabo e meça de novo.");
        return tips;
    }

    public static string Name(PadLink link) => link == PadLink.Usb ? "Cabo USB" : "Bluetooth";
}
