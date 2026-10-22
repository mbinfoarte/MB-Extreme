using System.Text;
using MBExtreme.Core.Repair;
using Xunit;

namespace MBExtreme.Core.Tests;

public class ReparoTests
{
    [Theory]
    [InlineData("Verification 45% complete.", 45.0)]
    [InlineData("Verificação 7% concluída.", 7.0)]
    [InlineData("[==========================100.0%==========================]", 100.0)]
    [InlineData("[=====                      10,5%                           ]", 10.5)]
    public void Porcentagem_e_lida_das_linhas(string line, double expected) =>
        Assert.Equal(expected, RepairOutput.TryPercent(line));

    [Theory]
    [InlineData("Nenhuma porcentagem aqui")]
    [InlineData("Versão 2024")]
    public void Linha_sem_porcentagem_retorna_nulo(string line) => Assert.Null(RepairOutput.TryPercent(line));

    [Fact]
    public void Utf16_do_sfc_e_decodificado_mesmo_em_pedacos()
    {
        byte[] bytes = Encoding.Unicode.GetBytes("Verificação do sistema iniciada.\r\n");
        var decoder = new OutputDecoder(Encoding.UTF8);
        // corta no meio de um caractere de 2 bytes
        string text = decoder.Decode(bytes.AsSpan(0, 5)) + decoder.Decode(bytes.AsSpan(5));
        Assert.Equal("Verificação do sistema iniciada.\r\n", text);
    }

    [Fact]
    public void Saida_de_console_usa_a_codificacao_de_reserva()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var oem = Encoding.GetEncoding(850);
        var decoder = new OutputDecoder(oem);
        Assert.Equal("Concluído", decoder.Decode(oem.GetBytes("Concluído")));
    }

    [Fact]
    public void Linhas_sao_separadas_por_retorno_de_carro_e_nova_linha()
    {
        var splitter = new LineSplitter();
        var lines = splitter.Feed("a\r\nb\rc").ToList();
        lines.AddRange(splitter.Feed("d\n"));
        Assert.Equal(new[] { "a", "b", "cd" }, lines);
        Assert.Null(splitter.Flush());
    }

    [Fact]
    public void Clean_remove_nulos_e_controles() =>
        Assert.Equal("ok", RepairOutput.Clean("\0o\0k\0 \u0007"));

    [Theory]
    [InlineData("A Proteção de Recursos do Windows não encontrou nenhuma violação de integridade.", true)]
    [InlineData("Windows Resource Protection did not find any integrity violations.", true)]
    [InlineData("A Proteção de Recursos do Windows encontrou arquivos corrompidos e os reparou com êxito.", true)]
    [InlineData("Windows Resource Protection found corrupt files and successfully repaired them.", true)]
    [InlineData("A Proteção de Recursos do Windows encontrou arquivos corrompidos, mas não pôde corrigir alguns deles.", false)]
    [InlineData("Windows Resource Protection found corrupt files but was unable to fix some of them.", false)]
    public void Sfc_e_interpretado_em_portugues_e_ingles(string text, bool ok) =>
        Assert.Equal(ok, RepairInterpreter.Interpret(StepKind.Sfc, 0, text).Ok);

    [Fact]
    public void Dism_traduz_codigos_conhecidos()
    {
        Assert.True(RepairInterpreter.Interpret(StepKind.Dism, 0, "").Ok);
        var missing = RepairInterpreter.Interpret(StepKind.Dism, unchecked((int)0x800F081F), "");
        Assert.False(missing.Ok);
        Assert.Contains("internet", missing.Message);
        Assert.Contains("administrador", RepairInterpreter.Interpret(StepKind.Dism, 740, "").Message);
    }

    [Theory]
    [InlineData("O repositório de componentes é reparável.", false)]
    [InlineData("No component store corruption detected.", true)]
    [InlineData("Nenhuma corrupção do repositório de componentes foi detectada.", true)]
    public void Dism_verificacao_avisa_quando_ha_corrupcao(string text, bool ok) =>
        Assert.Equal(ok, RepairInterpreter.Interpret(StepKind.DismCheck, 0, text).Ok);

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void Chkdsk_codigos_de_saida(int code, bool ok) =>
        Assert.Equal(ok, RepairInterpreter.Interpret(StepKind.Chkdsk, code, "").Ok);

    [Fact]
    public void Chkdsk_agendado_confirma_pelo_texto()
    {
        Assert.True(RepairInterpreter.Interpret(StepKind.ChkdskSchedule, 0, "Será verificado na próxima reinicialização").Ok);
        Assert.False(RepairInterpreter.Interpret(StepKind.ChkdskSchedule, 3, "Acesso negado").Ok);
    }

    [Fact]
    public void Catalogo_tem_ids_unicos_e_comandos_validos()
    {
        var tasks = RepairCatalog.Create("C:", "pt");
        Assert.Equal(tasks.Count, tasks.Select(t => t.Id).Distinct().Count());
        foreach (var task in tasks)
        {
            Assert.NotEmpty(task.Steps);
            Assert.All(task.Steps, s => Assert.False(string.IsNullOrWhiteSpace(s.File)));
        }
        Assert.Equal("full", tasks[0].Id);
    }

    [Fact]
    public void Reparo_completo_roda_dism_sfc_e_chkdsk_nessa_ordem()
    {
        var full = RepairCatalog.Create("D:", "en").First(t => t.Id == "full");
        Assert.Equal(new[] { "DISM.exe", "sfc.exe", "chkdsk.exe" }, full.Steps.Select(s => s.File));
        Assert.Contains("RestoreHealth", full.Steps[0].Arguments);
        Assert.Equal("D: /scan", full.Steps[2].Arguments);
    }

    [Theory]
    [InlineData("pt", "S")]
    [InlineData("en", "Y")]
    [InlineData("fr", "O")]
    public void Chkdsk_responde_sim_no_idioma_do_windows(string language, string answer)
    {
        var fix = RepairCatalog.Create("C:", language).First(t => t.Id == "chkdsk-fix");
        Assert.Equal(answer, fix.Steps[0].Input);
        Assert.True(fix.NeedsRestart);
    }

    [Fact]
    public void Comandos_manuais_mostram_cada_passo()
    {
        var sfc = RepairCatalog.Create("C:", "pt").First(t => t.Id == "sfc");
        Assert.Equal("sfc.exe /scannow", sfc.ManualCommands);
    }

    [Fact]
    public async Task Runner_executa_comando_e_le_a_saida()
    {
        if (!OperatingSystem.IsLinux()) return;
        var step = new RepairStep("eco", "/bin/sh", "-c \"printf 'ola\\n50%%\\n'\"");
        var task = new RepairTask("t", "g", "Teste", "", "1 s", new[] { step });
        var lines = new List<RepairUpdate>();
        var result = await new RepairRunner().RunAsync(task, new SyncProgress(lines.Add), CancellationToken.None);
        Assert.True(result.Success);
        Assert.Contains(lines, l => l.IsLog && l.Message == "ola");
        Assert.Contains(lines, l => !l.IsLog && l.Message == "50%");
    }

    [Fact]
    public async Task Runner_cancelado_encerra_o_processo()
    {
        if (!OperatingSystem.IsLinux()) return;
        var step = new RepairStep("dorme", "/bin/sh", "-c \"sleep 30\"");
        var task = new RepairTask("t", "g", "Teste", "", "1 s", new[] { step });
        using var cts = new CancellationTokenSource(300);
        var started = DateTime.UtcNow;
        var result = await new RepairRunner().RunAsync(task, new SyncProgress(_ => { }), cts.Token);
        Assert.True(result.Cancelled);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Sfc_com_violacoes_nao_vira_concluido()
    {
        Assert.False(RepairInterpreter.Interpret(StepKind.Sfc, 0, "Windows Resource Protection found integrity violations.").Ok);
        Assert.False(RepairInterpreter.Interpret(StepKind.Sfc, 0, "A Proteção de Recursos do Windows encontrou violações de integridade.").Ok);
    }

    [Fact]
    public void Chkdsk_agendado_nao_confunde_a_pergunta_com_a_confirmacao()
    {
        Assert.False(RepairInterpreter.Interpret(StepKind.ChkdskSchedule, 0, "Deseja agendar a verificação na próxima reinicialização (S/N)?").Ok);
        Assert.True(RepairInterpreter.Interpret(StepKind.ChkdskSchedule, 0, "Este volume será verificado na próxima reinicialização do sistema.").Ok);
    }

    [Fact]
    public void Reinicio_de_rede_avisa_que_apaga_ip_fixo() =>
        Assert.Contains("IP fixo", RepairCatalog.Create("C:", "pt").First(t => t.Id == "net-reset").Caution);

    [Fact]
    public void Wu_reset_religa_os_servicos_mesmo_se_renomear_falhar()
    {
        var task = RepairCatalog.Create("C:", "pt").First(t => t.Id == "wu-reset");
        Assert.All(task.Steps.Where(st => st.Arguments.StartsWith("start ")), st => Assert.True(st.AlwaysRun));
    }

    [Fact]
    public async Task Passos_obrigatorios_rodam_depois_de_uma_falha()
    {
        if (!OperatingSystem.IsLinux()) return;
        var task = new RepairTask("t", "g", "T", "", "1 s", new[]
        {
            new RepairStep("falha", "/bin/sh", "-c \"exit 3\""),
            new RepairStep("pula", "/bin/sh", "-c \"echo pulado\""),
            new RepairStep("religa", "/bin/sh", "-c \"echo religado\"", StepKind.Generic, true, null, true),
        });
        var lines = new List<RepairUpdate>();
        var result = await new RepairRunner().RunAsync(task, new SyncProgress(lines.Add), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains(lines, l => l.Message == "religado");
        Assert.DoesNotContain(lines, l => l.Message == "pulado");
    }

    private sealed class SyncProgress(Action<RepairUpdate> onReport) : IProgress<RepairUpdate>
    {
        public void Report(RepairUpdate value) => onReport(value);
    }
}

public class RedeStatusTests
{
    [Fact]
    public void Ping_calcula_media_jitter_e_perda()
    {
        var w = new MBExtreme.Core.Network.PingWindow(10);
        foreach (double? v in new double?[] { 10, 20, null, 10, 30 }) w.Add(v);
        Assert.Equal(17.5, w.Average!.Value, 3);
        Assert.Equal((10 + 10 + 20) / 3.0, w.Jitter!.Value, 3);
        Assert.Equal(20, w.LossPercent, 3);
        Assert.Equal(30, w.Last);
    }

    [Fact]
    public void Ping_mantem_so_as_ultimas_medicoes()
    {
        var w = new MBExtreme.Core.Network.PingWindow(3);
        for (int i = 1; i <= 10; i++) w.Add(i);
        Assert.Equal(3, w.Count);
        Assert.Equal(9, w.Average!.Value, 3);
    }

    [Fact]
    public void Dns_conhecido_ganha_o_nome_do_provedor()
    {
        Assert.Equal("Cloudflare (1.1.1.1)", MBExtreme.Core.Network.NetworkStatus.DnsLabel("1.1.1.1"));
        Assert.Equal("roteador (192.168.0.1)", MBExtreme.Core.Network.NetworkStatus.DnsLabel("192.168.0.1", "192.168.0.1"));
        Assert.Equal("não identificado", MBExtreme.Core.Network.NetworkStatus.DescribeDns(Array.Empty<string>(), null));
    }

    private static MBExtreme.Core.Network.HopStats Hop(int n, string address, params double?[] samples)
    {
        var h = new MBExtreme.Core.Network.HopStats { Hop = n, Address = address };
        foreach (var s in samples) h.Add(s);
        return h;
    }

    [Fact]
    public void Rota_estavel_e_reconhecida()
    {
        var report = MBExtreme.Core.Network.RouteSummary.Summarize(new[]
        {
            Hop(1, "192.168.0.1", 1, 1, 1), Hop(2, "10.0.0.1", 8, 9, 8), Hop(3, "1.1.1.1", 12, 11, 12),
        });
        Assert.True(report.Healthy);
        Assert.Equal(3, report.Hops);
    }

    [Fact]
    public void Rota_com_salto_grande_aponta_o_ponto()
    {
        var report = MBExtreme.Core.Network.RouteSummary.Summarize(new[]
        {
            Hop(1, "192.168.0.1", 1, 1, 1), Hop(2, "10.0.0.1", 8, 9, 8), Hop(3, "200.1.1.1", 90, 95, 92), Hop(4, "1.1.1.1", 95, 96, 94),
        });
        Assert.False(report.Healthy);
        Assert.Equal(3, report.WorstJumpHop);
        Assert.Contains("200.1.1.1", report.Text);
    }

    [Fact]
    public void Rota_sem_resposta_nao_e_saudavel() =>
        Assert.False(MBExtreme.Core.Network.RouteSummary.Summarize(new[] { Hop(1, "???", null, null, null) }).Healthy);
}

public class LimpezaAutomaticaTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Limpa_quando_passa_do_limite_e_nunca_limpou()
    {
        Assert.True(MBExtreme.Core.Memory.MemoryAutoPolicy.ShouldClean(90, 85, null, Now));
        Assert.False(MBExtreme.Core.Memory.MemoryAutoPolicy.ShouldClean(80, 85, null, Now));
    }

    [Fact]
    public void Respeita_o_intervalo_entre_limpezas()
    {
        Assert.False(MBExtreme.Core.Memory.MemoryAutoPolicy.ShouldClean(95, 85, Now.AddMinutes(-1), Now));
        Assert.True(MBExtreme.Core.Memory.MemoryAutoPolicy.ShouldClean(95, 85, Now.AddMinutes(-4), Now));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(99)]
    public void Limite_fora_da_faixa_desliga_a_limpeza(int threshold) =>
        Assert.False(MBExtreme.Core.Memory.MemoryAutoPolicy.ShouldClean(100, threshold, null, Now));
}

public class UnidadesTests
{
    [Fact]
    public void Mapa_de_unidades_le_um_ou_varios_itens()
    {
        var one = MBExtreme.Core.Storage.DriveMap.ParseMap("{\"Letter\":\"E\",\"Model\":\"SanDisk Ultra\",\"Bus\":\"USB\",\"Media\":\"Unspecified\"}");
        Assert.Equal("SanDisk Ultra", one["E"].Model);
        var many = MBExtreme.Core.Storage.DriveMap.ParseMap("[{\"Letter\":\"C\",\"Model\":\"Samsung 980\",\"Bus\":\"NVMe\",\"Media\":\"SSD\"},{\"Letter\":\"D\",\"Model\":\"WD\",\"Bus\":\"SATA\",\"Media\":\"HDD\"}]");
        Assert.Equal(2, many.Count);
        Assert.Equal("NVMe", many["c"].Bus);
        Assert.Empty(MBExtreme.Core.Storage.DriveMap.ParseMap("lixo"));
        Assert.Empty(MBExtreme.Core.Storage.DriveMap.ParseMap(""));
    }

    private static MBExtreme.Core.Storage.DriveEntry Drive(string letter, string bus, string media, bool system = false) =>
        new(letter, "x", "NTFS", 100, 40, "m", media, bus, system);

    [Fact]
    public void So_unidades_externas_podem_ser_removidas()
    {
        Assert.True(MBExtreme.Core.Storage.DriveMap.CanEject(Drive("E", "USB", "Unspecified")));
        Assert.True(MBExtreme.Core.Storage.DriveMap.CanEject(Drive("F", "", "Removable")));
        Assert.False(MBExtreme.Core.Storage.DriveMap.CanEject(Drive("D", "SATA", "HDD")));
        Assert.False(MBExtreme.Core.Storage.DriveMap.CanEject(Drive("C", "USB", "SSD", system: true)));
    }

    [Fact]
    public void Porcentagem_usada_da_unidade() =>
        Assert.Equal(60, Drive("E", "USB", "").UsedPercent, 3);
}
