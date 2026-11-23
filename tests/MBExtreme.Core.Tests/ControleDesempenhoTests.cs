using MBExtreme.Core.Gamepads;
using MBExtreme.Core.Hardware;
using Xunit;

namespace MBExtreme.Core.Tests;

public class AnalisadorControleTests
{
    private static List<double> Times(IEnumerable<double> gaps)
    {
        var list = new List<double> { 0 };
        foreach (double g in gaps) list.Add(list[^1] + g);
        return list;
    }

    [Fact]
    public void Cabo_estavel_a_1000hz_e_excelente()
    {
        var r = PadLatencyReport.From(PadLink.Usb, Times(Enumerable.Repeat(1.0, 2000)))!;
        Assert.Equal(1000, r.RateHz, 0);
        Assert.Equal(0, r.Spikes);
        Assert.Equal(0, r.Lost);
        Assert.Equal(0, r.Level);
        Assert.Equal("EXCELENTE", r.Quality);
    }

    [Fact]
    public void Bluetooth_com_picos_conta_picos_e_perdidos()
    {
        var gaps = Enumerable.Repeat(1.25, 1000).ToList();
        gaps[100] = 18; gaps[300] = 12; gaps[600] = 9;
        var r = PadLatencyReport.From(PadLink.Bluetooth, Times(gaps))!;
        Assert.Equal(3, r.Spikes);
        Assert.True(r.Lost >= 20);
        Assert.True(r.Level >= 1);
        Assert.Equal(18, r.WorstMs, 1);
    }

    [Fact]
    public void Xinput_nao_conta_perdidos()
    {
        var gaps = Enumerable.Repeat(4.0, 500).ToList();
        gaps[10] = 40;
        var r = PadLatencyReport.From(PadLink.Usb, Times(gaps), countLost: false)!;
        Assert.Equal(0, r.Lost);
    }

    [Fact]
    public void Pausa_longa_nao_entra_e_poucos_pacotes_da_nulo()
    {
        Assert.Null(PadLatencyReport.From(PadLink.Usb, Times(new[] { 1.0, 1.0 })));
        var gaps = Enumerable.Repeat(4.0, 200).Append(900).Concat(Enumerable.Repeat(4.0, 200));
        var r = PadLatencyReport.From(PadLink.Usb, Times(gaps))!;
        Assert.Equal(4, r.WorstMs, 1);
    }

    [Fact]
    public void Cabo_ganha_do_bluetooth_que_manda_mais_mas_engasga()
    {
        var usbGaps = Enumerable.Repeat(4.0, 1000);
        var btGaps = Enumerable.Range(0, 1000).Select(i => i % 50 == 0 ? 20.0 : 1.25);
        var usb = PadLatencyReport.From(PadLink.Usb, Times(usbGaps));
        var bt = PadLatencyReport.From(PadLink.Bluetooth, Times(btGaps));
        Assert.Equal(PadLink.Usb, PadAnalyzer.Best(usb, bt));
        string v = PadAnalyzer.Verdict(usb, bt);
        Assert.Contains("use o cabo USB", v);
        Assert.Contains("manda mais pacotes", v);
    }

    [Fact]
    public void Bluetooth_estavel_e_bem_melhor_ganha()
    {
        var usb = PadLatencyReport.From(PadLink.Usb, Times(Enumerable.Range(0, 1000).Select(i => i % 20 == 0 ? 12.0 : 4.0)));
        var bt = PadLatencyReport.From(PadLink.Bluetooth, Times(Enumerable.Repeat(1.25, 1000)));
        Assert.Equal(PadLink.Bluetooth, PadAnalyzer.Best(usb, bt));
    }

    [Fact]
    public void Uma_medicao_so_pede_a_outra()
    {
        var usb = PadLatencyReport.From(PadLink.Usb, Times(Enumerable.Repeat(4.0, 500)));
        Assert.Contains("no Bluetooth", PadAnalyzer.Verdict(usb, null));
        Assert.Null(PadAnalyzer.Best(null, null));
    }

    [Fact]
    public void Dicas_do_bluetooth_falam_de_fone_wifi_e_adaptador()
    {
        var gaps = Enumerable.Range(0, 500).Select(i => i % 40 == 0 ? 15.0 : 1.25);
        var bt = PadLatencyReport.From(PadLink.Bluetooth, Times(gaps))!;
        var tips = PadAnalyzer.Tips(bt, new PadLinkContext(BluetoothAudioDevices: 2, WifiOn24GHz: true, BluetoothRadios: 2));
        Assert.Contains(tips, t => t.Contains("fone"));
        Assert.Contains(tips, t => t.Contains("5 GHz"));
        Assert.Contains(tips, t => t.Contains("adaptador Bluetooth"));
    }

    [Fact]
    public void Dica_do_cabo_a_250hz_aponta_o_overclock()
    {
        var usb = PadLatencyReport.From(PadLink.Usb, Times(Enumerable.Repeat(4.0, 500)))!;
        Assert.Contains(PadAnalyzer.Tips(usb, new PadLinkContext()), t => t.Contains("1000 Hz"));
    }

    [Fact]
    public void Barras_guardam_o_pico_do_trecho()
    {
        var gaps = Enumerable.Repeat(1.0, 1000).ToList();
        gaps[503] = 30;
        var r = PadLatencyReport.From(PadLink.Usb, Times(gaps))!;
        var bars = r.Bars(50);
        Assert.Equal(50, bars.Count);
        Assert.Equal(30, bars.Max());
    }

    [Fact]
    public void Medicao_guardada_volta_igual()
    {
        var r = PadLatencyReport.From(PadLink.Bluetooth, Times(Enumerable.Repeat(1.25, 400)))!;
        var back = PadLatencyReport.FromRecord(PadLink.Bluetooth, r.ToRecord(DateTime.Now));
        Assert.Equal(r.RateHz, back.RateHz);
        Assert.Equal(r.JitterMs, back.JitterMs);
        Assert.Equal("0CE6|Bluetooth", PadLatencyReport.RecordKey(0x0CE6, PadLink.Bluetooth));
    }
}

public class PortasUsbTests
{
    private static UsbRawPort P(int index, bool usb3, int companion = 0, bool user = true, bool typeC = false, UsbPanel panel = UsbPanel.Unknown,
        string? device = null, UsbUse use = UsbUse.None, string hub = "ROOT1", UsbSource source = UsbSource.Chipset, bool tenGig = false, string controller = "HC1", bool behindHub = false) =>
        new(hub, index, !usb3, usb3, user, typeC, companion, controller, "Controlador", source, tenGig, behindHub, panel, device, use);

    [Theory]
    [InlineData(@"PCI\VEN_1022&DEV_43F7&SUBSYS_1", "", UsbSource.Chipset, true)]
    [InlineData(@"PCI\VEN_1022&DEV_15B6&SUBSYS_1", "", UsbSource.Cpu, true)]
    [InlineData(@"PCI\VEN_8086&DEV_7AE0", "Intel(R) USB 3.20 eXtensible Host Controller", UsbSource.Chipset, true)]
    [InlineData(@"PCI\VEN_8086&DEV_9A13", "Intel(R) USB 3.10 eXtensible Host Controller - 1.20 (Microsoft)", UsbSource.Cpu, true)]
    [InlineData(@"PCI\VEN_1B21&DEV_1142", "ASMedia USB 3.0", UsbSource.ExtraChip, false)]
    [InlineData(@"PCI\VEN_1B21&DEV_3242", "ASMedia USB 3.2", UsbSource.ExtraChip, true)]
    [InlineData(@"PCI\VEN_ABCD&DEV_0001", "", UsbSource.Unknown, false)]
    public void Controlador_pelo_fabricante(string id, string desc, UsbSource source, bool tenGig)
    {
        var (s, t) = UsbPorts.ClassifyController(id, desc);
        Assert.Equal(source, s);
        Assert.Equal(tenGig, t);
    }

    [Fact]
    public void Lado_pelo_pld()
    {
        var pld = new byte[20];
        pld[8] = 5 << 3;
        Assert.Equal(UsbPanel.Back, UsbPorts.PanelFromPld(pld));
        pld[8] = 4 << 3;
        Assert.Equal(UsbPanel.Front, UsbPorts.PanelFromPld(pld));
        pld[8] = 6 << 3;
        Assert.Equal(UsbPanel.Unknown, UsbPorts.PanelFromPld(pld));
        Assert.Equal(UsbPanel.Unknown, UsbPorts.PanelFromPld(null));
    }

    [Theory]
    [InlineData("Wireless Controller", "HIDClass", 0x054C, UsbUse.Controller)]
    [InlineData("Intel(R) Wireless Bluetooth(R)", "Bluetooth", 0x8087, UsbUse.BluetoothAdapter)]
    [InlineData("Game Capture HD60 X", "USB", 0x0FD9, UsbUse.CaptureCard)]
    [InlineData("HD Pro Webcam C920", "USB", 0x046D, UsbUse.Webcam)]
    [InlineData("USB Keyboard", "Keyboard", 0x1234, UsbUse.Keyboard)]
    [InlineData("USB Mass Storage Device", "USB", 0x0781, UsbUse.Storage)]
    public void Tipo_do_aparelho(string name, string cls, int vendor, UsbUse use) =>
        Assert.Equal(use, UsbPorts.Classify(name, cls, (ushort)vendor, false));

    [Fact]
    public void Junta_a_porta_3x_com_a_gemea_2_0_e_da_nome()
    {
        var raw = new[]
        {
            P(1, false, companion: 15, panel: UsbPanel.Back, device: "Wireless Controller", use: UsbUse.Controller),
            P(15, true, companion: 1, panel: UsbPanel.Unknown),
            P(2, false, companion: 16),
            P(16, true, companion: 2, panel: UsbPanel.Back),
            P(3, false, panel: UsbPanel.Back, device: "Bluetooth", use: UsbUse.BluetoothAdapter),
            P(17, true, typeC: true, panel: UsbPanel.Back),
        };
        var ports = UsbPorts.Build(raw);
        Assert.Equal(4, ports.Count);
        var ctrl = ports.Single(p => p.Use == UsbUse.Controller);
        Assert.Equal(UsbPortKind.Usb3, ctrl.Kind);
        Assert.StartsWith("Traseira", ctrl.Name);
        Assert.Contains(ports, p => p.Name == "Traseira C" && p.TypeC);
        Assert.Equal(UsbPortKind.Usb2, ports.Single(p => p.Use == UsbUse.BluetoothAdapter).Kind);
    }

    [Fact]
    public void Porta_interna_vazia_some_e_bluetooth_interno_fica_como_interna()
    {
        var raw = new[]
        {
            P(9, false, user: false),
            P(10, false, user: false, device: "Intel Wireless Bluetooth", use: UsbUse.BluetoothAdapter),
            P(11, false, user: false, device: "Hub USB", use: UsbUse.Hub),
            P(1, false, panel: UsbPanel.Front, hub: "HUB2", behindHub: true),
        };
        var ports = UsbPorts.Build(raw);
        Assert.Equal(2, ports.Count);
        Assert.Contains(ports, p => p.Panel == UsbPanel.Internal && p.Name == "Interna 1");
        Assert.Contains(ports, p => p.Name == "Frontal 1" && p.BehindHub);
    }

    [Fact]
    public void Velocidade_da_porta()
    {
        var ports = UsbPorts.Build(new[] { P(1, true, tenGig: true, panel: UsbPanel.Back), P(2, true, panel: UsbPanel.Back), P(3, false, panel: UsbPanel.Back) });
        Assert.Contains(ports, p => p.KindText == "USB 3.2 · 10 Gb/s");
        Assert.Contains(ports, p => p.KindText == "USB 3.2 · 5 Gb/s");
        Assert.Contains(ports, p => p.KindText == "USB 2.0");
    }

    [Fact]
    public void Recomenda_porta_direta_para_controle_2_0_para_bluetooth_e_a_mais_veloz_para_captura()
    {
        var raw = new[]
        {
            P(1, false, panel: UsbPanel.Back),                                       // traseira 2.0
            P(2, true, panel: UsbPanel.Back),                                        // traseira 5 Gb/s chipset
            P(3, true, panel: UsbPanel.Back, source: UsbSource.Cpu, tenGig: true, controller: "HC2"), // traseira 10 Gb/s processador
            P(1, true, panel: UsbPanel.Front, hub: "HUB2", behindHub: true, device: "Webcam C920", use: UsbUse.Webcam), // frontal por hub
        };
        var ports = UsbPorts.Build(raw);
        var advice = UsbPorts.Recommend(ports);
        var ctrl = advice.Single(a => a.For == UsbUse.Controller).Port!;
        var bt = advice.Single(a => a.For == UsbUse.BluetoothAdapter).Port!;
        var cap = advice.Single(a => a.For == UsbUse.CaptureCard).Port!;
        Assert.False(ctrl.BehindHub);
        Assert.Equal(UsbPortKind.Usb2, bt.Kind);
        Assert.Equal(UsbPortKind.Usb3Plus, cap.Kind);
        Assert.Equal(UsbSource.Cpu, cap.Source);
    }

    [Fact]
    public void Aparelho_ja_na_melhor_porta_fica()
    {
        var raw = new[] { P(1, false, panel: UsbPanel.Back, device: "Wireless Controller", use: UsbUse.Controller), P(2, false, panel: UsbPanel.Back) };
        var a = UsbPorts.Recommend(UsbPorts.Build(raw)).Single(x => x.For == UsbUse.Controller);
        Assert.True(a.AlreadyThere);
    }
}

public class ControleCompetitivoTests
{
    [Fact]
    public void Niveis_incluem_os_de_baixo()
    {
        var basic = PadCompetitive.TweakIds(CompetitiveLevel.Basico);
        var adv = PadCompetitive.TweakIds(CompetitiveLevel.Avancado);
        var ext = PadCompetitive.TweakIds(CompetitiveLevel.Extreme);
        Assert.All(basic, id => Assert.Contains(id, adv));
        Assert.All(adv, id => Assert.Contains(id, ext));
        Assert.DoesNotContain(PadCompetitive.UsbMsi, adv);
        Assert.Contains(PadCompetitive.UsbMsi, ext);
        Assert.Equal((false, false), PadCompetitive.ProgramOptions(CompetitiveLevel.Basico));
        Assert.Equal((true, true), PadCompetitive.ProgramOptions(CompetitiveLevel.Extreme));
    }

    [Fact]
    public void Ajustes_dos_niveis_existem_no_catalogo()
    {
        if (!OperatingSystem.IsWindows()) return;
        var ids = MBExtreme.Core.Tweaks.TweakCatalog.Create(new MBExtreme.Core.Tweaks.TweakStore(Path.GetTempFileName())).Select(t => t.Id).ToHashSet();
        Assert.All(PadCompetitive.TweakIds(CompetitiveLevel.Extreme), id => Assert.Contains(id, ids));
    }

    [Theory]
    [InlineData(0x054C, 0x0CE6, true)]
    [InlineData(0x054C, 0x05C4, true)]
    [InlineData(0x054C, 0x09CC, true)]
    [InlineData(0x054C, 0x0DF2, false)] // DualSense Edge fica de fora
    [InlineData(0x045E, 0x0B12, false)] // Xbox
    public void Overclock_so_dualsense_e_dualshock4(int vendor, int product, bool ok) =>
        Assert.Equal(ok, PadOverclock.IsSupported((ushort)vendor, (ushort)product));

    [Fact]
    public void Estado_do_overclock_pela_medicao()
    {
        Assert.Equal(OverclockState.DriverMissing, PadOverclock.StateFrom(true, false, 1000));
        Assert.Equal(OverclockState.NotActive, PadOverclock.StateFrom(true, true, 250));
        Assert.Equal(OverclockState.Partial, PadOverclock.StateFrom(true, true, 500));
        Assert.Equal(OverclockState.Active, PadOverclock.StateFrom(true, true, 990));
        Assert.Contains("1000", PadOverclock.StateText(OverclockState.NotActive, 250));
        Assert.Equal(1000, PadOverclock.TargetHz);
    }

    [Fact]
    public void Verificacao_nao_pede_desligar_secure_boot()
    {
        var checks = PadOverclock.Checks(false, true, true);
        Assert.Contains(checks, c => c.Text.Contains("Secure Boot continua ligado"));
        Assert.Contains(checks, c => !c.Ok && c.Text.Contains("não instalado"));
    }
}

public class SuporteAoVivoTests
{
    [Theory]
    [InlineData(0, "agora")]
    [InlineData(5 * 60_000, "há 5 min")]
    [InlineData(2 * 3_600_000, "há 2 h")]
    [InlineData(3 * 86_400_000L, "há 3 d")]
    public void Tempo_igual_ao_painel_do_site(long diff, string esperado) =>
        Assert.Equal(esperado, MBExtreme.Core.Licensing.InviteClient.Ago(1_000_000_000_000 - diff, 1_000_000_000_000));

    [Fact]
    public void Lista_do_servidor_e_lida()
    {
        string json = """{"items":[{"deviceId":"d1","uid":"u","name":"Ana","email":"a@b.com","rustdeskId":"123456789","pcName":"PC-ANA","os":"Windows 11","cpu":"i5","gpu":"RTX","plan":"gamer","planStatus":"active","lastSeen":1,"onlineSince":1,"online":true,"helpAt":5,"inSession":false,"sessionToday":true}],"now":10}""";
        var list = System.Text.Json.JsonSerializer.Deserialize<MBExtreme.Core.Licensing.SupportList>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Single(list.Items);
        Assert.Equal(5, list.Items[0].HelpAt);
        Assert.True(list.Items[0].Online);
        Assert.Equal(10, list.Now);
    }
}
