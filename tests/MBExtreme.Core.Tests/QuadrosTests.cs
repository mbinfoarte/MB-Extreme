using MBExtreme.Core.Fps;
using MBExtreme.Core.Sensors;
using Xunit;

namespace MBExtreme.Core.Tests;

public class QuadrosTests
{
    private static List<double> Times(int count, double ms, double start = 0)
    {
        var list = new List<double>();
        double t = start;
        for (int i = 0; i < count; i++) { list.Add(t); t += ms / 1000.0; }
        return list;
    }

    // ---- tempos de quadro

    [Fact]
    public void Instantes_viram_tempos_de_quadro_em_ms()
    {
        var (ms, pauses) = FrameAnalysis.FrameTimes(Times(101, 10));
        Assert.Equal(100, ms.Count);
        Assert.All(ms, v => Assert.Equal(10, v, 6));
        Assert.Equal(0, pauses);
    }

    [Fact]
    public void Aquecimento_e_cortado_do_comeco()
    {
        var (ms, _) = FrameAnalysis.FrameTimes(Times(1001, 10), warmupSeconds: 5);
        Assert.InRange(ms.Count, 498, 500);                         // 10 s de dados menos 5 s de aquecimento
    }

    [Fact]
    public void Pausa_longa_nao_entra_na_conta_mas_e_contada()
    {
        var times = Times(50, 10);
        times.AddRange(Times(50, 10, start: times[^1] + 3.0));      // alt+tab de 3 s
        var (ms, pauses) = FrameAnalysis.FrameTimes(times);
        Assert.Equal(1, pauses);
        Assert.DoesNotContain(ms, v => v > 1000);
        Assert.Equal(98, ms.Count);
    }

    // ---- medidas

    [Fact]
    public void Quadros_iguais_dao_fps_igual_e_nenhum_engasgo()
    {
        var m = FrameAnalysis.Compute(Enumerable.Repeat(10.0, 100).ToList());
        Assert.Equal(100, m.AverageFps, 6);
        Assert.Equal(100, m.Low1Fps, 6);
        Assert.Equal(100, m.Low01Fps, 6);
        Assert.Equal(10, m.P99FrameMs, 6);
        Assert.Equal(0, m.StdDevMs, 6);
        Assert.Equal(0, m.Stutters);
        Assert.Equal(1.0, m.DurationSeconds, 6);
        Assert.Equal(FrameAnalysis.Smoothness.Excellent, FrameAnalysis.Rate(m));
    }

    [Fact]
    public void Um_quadro_lento_derruba_o_low_e_pouco_a_media()
    {
        var frames = Enumerable.Repeat(10.0, 99).Append(100.0).ToList();
        var m = FrameAnalysis.Compute(frames);
        Assert.Equal(1000 / 10.9, m.AverageFps, 4);
        Assert.Equal(10, m.Low1Fps, 6);
        Assert.Equal(10, m.MinFps, 6);
        Assert.Equal(100, m.MaxFrameMs, 6);
    }

    [Fact]
    public void Os_dois_metodos_de_low_dao_valores_diferentes()
    {
        var frames = Enumerable.Repeat(10.0, 198).Append(50.0).Append(20.0).ToList();
        var m = FrameAnalysis.Compute(frames);
        Assert.Equal(1000 / 35.0, m.Low1Fps, 4);       // média dos 2 piores: (50+20)/2
        Assert.Equal(20, m.Low1TimeFps, 6);            // 1% do tempo (20,5 ms) já é atingido pelo pior quadro (50 ms)
        Assert.Equal(20, m.Low01Fps, 6);               // o pior quadro: 50 ms
        Assert.False(m.Low01Reliable);
        Assert.True(FrameAnalysis.Compute(Enumerable.Repeat(8.0, 2500).ToList()).Low01Reliable);
    }

    [Fact]
    public void Percentil_99_e_o_quadro_do_1_por_cento_mais_lento()
    {
        var frames = Enumerable.Range(1, 100).Select(i => (double)i).ToList();
        var m = FrameAnalysis.Compute(frames);
        Assert.Equal(99, m.P99FrameMs);
        Assert.Equal(51, m.MedianFrameMs);
        var curve = FrameAnalysis.PercentileCurve(frames, 50, 99);
        Assert.Equal(new[] { 50.0, 99.0 }, curve.Select(c => c.Ms));
    }

    [Fact]
    public void Conjunto_vazio_nao_quebra()
    {
        Assert.Equal(FrameMetrics.Empty, FrameAnalysis.Compute(new List<double>()));
        Assert.Empty(FrameAnalysis.FrameTimes(new List<double> { 1 }).FrameMs);
        Assert.Empty(FrameAnalysis.PercentileCurve(new List<double>()));
        Assert.Empty(FrameAnalysis.FpsPerSecond(new List<double>()));
    }

    // ---- engasgos

    [Fact]
    public void Engasgos_seguidos_contam_uma_vez_e_cena_mais_pesada_nao_conta()
    {
        var frames = Enumerable.Repeat(10.0, 120).ToList();
        frames[50] = 40; frames[51] = 40;     // um engasgo de dois quadros
        frames[90] = 60;                      // outro
        Assert.Equal(2, FrameAnalysis.CountStutters(frames));
        Assert.Equal(new[] { 50, 90 }, FrameAnalysis.StutterIndexes(frames));

        var heavier = Enumerable.Repeat(10.0, 60).Concat(Enumerable.Repeat(18.0, 60)).ToList();   // a cena ficou mais pesada, mas é constante
        Assert.Equal(0, FrameAnalysis.CountStutters(heavier));
    }

    [Fact]
    public void Quadro_lento_pequeno_demais_nao_e_engasgo()
    {
        var frames = Enumerable.Repeat(4.0, 100).ToList();
        frames[60] = 9;   // mais que o dobro, mas só 5 ms a mais
        Assert.Equal(0, FrameAnalysis.CountStutters(frames));
    }

    [Fact]
    public void Nota_de_fluidez()
    {
        FrameMetrics M(double avg, double low, double perMinute) => FrameMetrics.Empty with { Frames = 1000, AverageFps = avg, Low1Fps = low, StuttersPerMinute = perMinute };
        Assert.Equal(FrameAnalysis.Smoothness.Excellent, FrameAnalysis.Rate(M(100, 90, 0)));
        Assert.Equal(FrameAnalysis.Smoothness.Good, FrameAnalysis.Rate(M(100, 70, 5)));
        Assert.Equal(FrameAnalysis.Smoothness.Irregular, FrameAnalysis.Rate(M(100, 45, 20)));
        Assert.Equal(FrameAnalysis.Smoothness.Bad, FrameAnalysis.Rate(M(100, 30, 20)));
        Assert.Equal(FrameAnalysis.Smoothness.Good, FrameAnalysis.Rate(FrameMetrics.Empty));      // pouca amostra: não acusa
    }

    // ---- séries

    [Fact]
    public void Reducao_para_o_grafico_guarda_os_picos()
    {
        var values = Enumerable.Repeat(10.0, 1000).ToList();
        values[537] = 100;
        var reduced = FrameAnalysis.DownsampleMax(values, 100);
        Assert.Equal(100, reduced.Length);
        Assert.Contains(100.0, reduced);
        Assert.Equal(5, FrameAnalysis.DownsampleMax(new List<double> { 1, 2, 3, 4, 5 }, 100).Length);
    }

    [Fact]
    public void Fps_por_segundo_conta_os_quadros_de_cada_segundo()
    {
        var series = FrameAnalysis.FpsPerSecond(Times(501, 10));
        Assert.Equal(4, series.Length);                  // 5 s de dados -> 4 segundos completos
        Assert.All(series, v => Assert.InRange(v, 99, 101));
    }

    [Fact]
    public void Histograma_junta_o_que_passa_do_limite_na_ultima_faixa()
    {
        var counts = FrameAnalysis.Histogram(new List<double> { 3, 4, 12, 14, 99 }, 5, 20);
        Assert.Equal(new[] { 2, 0, 2, 1 }, counts);
    }

    [Fact]
    public void Csv_tem_cabecalho_e_uma_linha_por_quadro()
    {
        var csv = FrameAnalysis.ToCsv(new List<double> { 10, 20 });
        Assert.Equal("frame,tempo_ms,fps_instantaneo\n1,10,100\n2,20,50\n", csv);
    }

    // ---- escolha do jogo

    private static FrameCapture NewCapture(string? wanted = null) =>
        new(wanted, pid => pid == 4, pid => pid switch { 100 => "jogo", 200 => "launcher", 4 => "dwm", _ => "outro" });

    [Fact]
    public void Escolhe_o_programa_que_mais_apresenta_quadros_e_ignora_o_desktop()
    {
        var capture = NewCapture();
        foreach (double t in Times(300, 10)) capture.Record(100, t);          // 100 FPS
        foreach (double t in Times(60, 50)) capture.Record(200, t);           // 20 FPS
        foreach (double t in Times(600, 5)) capture.Record(4, t);             // dwm, ignorado
        var live = capture.Live()!;
        Assert.Equal(100, live.ProcessId);
        Assert.Equal("jogo", live.Name);
        Assert.InRange(live.CurrentFps, 95, 105);
        Assert.Equal(300, live.Frames);
        var result = capture.Finish()!;
        Assert.Equal(300, result.PresentSeconds.Count);
    }

    [Fact]
    public void Nome_pedido_tem_prioridade_e_a_escolha_nao_pula_entre_janelas()
    {
        var capture = NewCapture("launcher");
        foreach (double t in Times(300, 10)) capture.Record(100, t);
        foreach (double t in Times(60, 50)) capture.Record(200, t);
        Assert.Equal(200, capture.Live()!.ProcessId);

        var sticky = NewCapture();
        foreach (double t in Times(200, 10)) sticky.Record(100, t);
        Assert.Equal(100, sticky.Live()!.ProcessId);
        foreach (double t in Times(400, 2, start: 2.0)) sticky.Record(200, t);                          // outro programa apresenta mais
        Assert.Equal(100, sticky.Live()!.ProcessId);                                                      // mas o jogo continua apresentando e fica escolhido
    }

    [Fact]
    public void Sem_jogo_nao_ha_resultado()
    {
        var capture = NewCapture();
        capture.Record(100, 1);
        capture.Record(100, 2);
        Assert.Null(capture.Live());
        Assert.Null(capture.Finish());
    }

    [Fact]
    public void Quadros_recentes_para_o_grafico_ao_vivo_tem_limite()
    {
        var capture = NewCapture();
        foreach (double t in Times(2000, 5)) capture.Record(100, t);
        var live = capture.Live(300)!;
        Assert.Equal(300, live.RecentFrameMs.Count);
        Assert.All(live.RecentFrameMs, v => Assert.Equal(5, v, 4));
    }

    // ---- sessões

    private static SensorSnapshot Snap(double? gpuLoad, double? gpuTemp, double? cpuLoad = null) =>
        new(true, null, null, cpuLoad, null, null, gpuTemp, gpuLoad, null, null, Array.Empty<FanReading>());

    [Fact]
    public void Gravador_de_sensores_so_cria_curva_de_quem_tem_leitura()
    {
        var recorder = new SensorRecorder();
        for (int i = 0; i < 5; i++) recorder.Add(Snap(90 + i, 60 + i));
        recorder.Add(SensorSnapshot.Unavailable("sem sensores"));
        var tracks = recorder.Tracks();
        Assert.Equal(new[] { "Uso da GPU", "Temperatura da GPU" }, tracks.Select(t => t.Name));
        Assert.Equal(94, tracks[0].Max);
        Assert.Equal(5, tracks[0].Values.Length);
        Assert.Equal(92, tracks[0].Average);
    }

    [Fact]
    public void Sessao_e_montada_e_guarda_e_recupera_do_historico()
    {
        var session = FpsSessionBuilder.Build("Jogo <X>", "antes", Times(3001, 10), Array.Empty<SensorTrack>(), warmupSeconds: 5, when: new DateTime(2026, 10, 24, 20, 0, 0), gpu: "RTX")!;
        Assert.InRange(session.Metrics.Frames, 2498, 2500);
        Assert.InRange(session.FrameMs.Length, 1, FpsSessionBuilder.GraphPoints);
        Assert.Null(FpsSessionBuilder.Build("x", null, Times(10, 10), Array.Empty<SensorTrack>(), 0, DateTime.Now));    // poucos quadros

        string path = Path.Combine(Path.GetTempPath(), "mbx-fps-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Assert.Empty(FpsSessionHistory.Load(path));
            FpsSessionHistory.Add(session, path);
            FpsSessionHistory.Add(session with { When = session.When.AddMinutes(1) }, path);
            var loaded = FpsSessionHistory.Load(path);
            Assert.Equal(2, loaded.Count);
            Assert.Equal("Jogo <X>", loaded[0].Game);
            Assert.Equal(session.Metrics.AverageFps, loaded[0].Metrics.AverageFps, 6);
            FpsSessionHistory.Remove(session.When, path);
            Assert.Single(FpsSessionHistory.Load(path));
            for (int i = 0; i < 40; i++) FpsSessionHistory.Add(session with { When = session.When.AddHours(i + 1) }, path, keep: 30);
            Assert.Equal(30, FpsSessionHistory.Load(path).Count);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Historico_corrompido_vira_lista_vazia()
    {
        string path = Path.Combine(Path.GetTempPath(), "mbx-fps-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{ não é json");
        try { Assert.Empty(FpsSessionHistory.Load(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Comparacao_mostra_melhoria_e_piora_conforme_o_tipo_da_medida()
    {
        var a = FrameAnalysis.Compute(Enumerable.Repeat(10.0, 200).ToList());                         // 100 FPS
        var b = FrameAnalysis.Compute(Enumerable.Repeat(8.0, 200).ToList());                          // 125 FPS
        var rows = SessionComparison.Compare(a, b);
        var avg = rows.Single(r => r.Label == "FPS médio");
        Assert.Equal(25, avg.DeltaPercent, 4);
        Assert.True(avg.Improved);
        var ms = rows.Single(r => r.Label == "Tempo de quadro médio");
        Assert.Equal(-20, ms.DeltaPercent, 4);
        Assert.True(ms.Improved);                                                                      // menos ms é melhor
        var worse = SessionComparison.Compare(b, a).Single(r => r.Label == "FPS médio");
        Assert.True(worse.Worsened);
    }

    [Fact]
    public void Relatorio_html_e_texto_trazem_os_numeros_e_escapam_o_nome()
    {
        var session = FpsSessionBuilder.Build("Jogo <script>", "rodada 1", Times(3001, 10), new[] { new SensorTrack("Uso da GPU", "%", new double[] { 90, 95, 99 }) }, 5, new DateTime(2026, 10, 24, 20, 0, 0), "NVIDIA RTX")!;
        string html = SessionReport.ToHtml(session);
        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("Jogo &lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("polyline", html);
        Assert.Contains("Uso da GPU", html);
        Assert.Contains("NVIDIA RTX", html);
        Assert.Contains("100", html);
        string text = SessionReport.ToText(session);
        Assert.Contains("FPS médio 100", text);
        Assert.Contains("Uso da GPU: média 94,7", text.Replace('.', ','));
    }
}
