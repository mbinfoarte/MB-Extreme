using MBExtreme.Core.Apps;
using MBExtreme.Core.Devices;
using MBExtreme.Core.Diagnostics;
using MBExtreme.Core.Drivers;
using MBExtreme.Core.Network;
using MBExtreme.Core.Startup;
using MBExtreme.Core.Storage;
using MBExtreme.Core.Windows;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Visual novo (Painel com nota) e as telas novas: Windows 11, Mapa do disco, pontos de restauração, backup, pendrive falso, drivers, rede e inicialização.</summary>
public class NovoVisualHubTests
{
    private static Finding F(string id, Severity s) => new(id, id, "", s);

    // ---------------- nota de saúde

    [Fact]
    public void Nota_de_saude_desconta_por_gravidade()
    {
        Assert.Equal(100, HealthScore.Of(Array.Empty<Finding>()));
        Assert.Equal(100 - 25 - 12 - 5, HealthScore.Of(new[] { F("a", Severity.Critical), F("b", Severity.Problem), F("c", Severity.Warning), F("d", Severity.Info) }));
        Assert.Equal(0, HealthScore.Of(Enumerable.Range(0, 10).Select(i => F("x" + i, Severity.Critical))));
    }

    [Fact]
    public void Itens_ignorados_nao_tiram_nota()
    {
        var findings = new[] { F("junk", Severity.Problem), F("startup", Severity.Warning) };
        Assert.Equal(95, HealthScore.Of(findings, new HashSet<string> { "junk" }));
    }

    [Theory]
    [InlineData(95, 2)]
    [InlineData(80, 2)]
    [InlineData(60, 1)]
    [InlineData(35, 0)]
    public void Faixa_de_cor_da_nota(int score, int level) => Assert.Equal(level, HealthScore.Level(score));

    [Fact]
    public void Painel_recebe_cada_verificacao_quando_termina()
    {
        var checks = new IDiagnosticCheck[]
        {
            new DelegateCheck("a", "A", CheckDepth.Quick, _ => Array.Empty<Finding>()),
            new DelegateCheck("b", "B", CheckDepth.Quick, _ => new[] { F("b1", Severity.Warning) }),
            new DelegateCheck("c", "C", CheckDepth.Deep, _ => Array.Empty<Finding>()),
        };
        var seen = new List<string>();
        var report = new DiagnosticRunner().Run(checks, deep: false, onCheck: r => seen.Add(r.CheckId));
        Assert.Equal(new[] { "a", "b" }, seen);
        Assert.Equal(2, report.Checks.Count);
    }

    // ---------------- Windows 11

    [Theory]
    [InlineData("Intel(R) Core(TM) i5-7400 CPU @ 3.00GHz", false)]
    [InlineData("Intel(R) Core(TM) i7-8700K CPU @ 3.70GHz", true)]
    [InlineData("Intel(R) Core(TM) i5-10400F CPU @ 2.90GHz", true)]
    [InlineData("12th Gen Intel(R) Core(TM) i7-12700H", true)]
    [InlineData("Intel(R) Core(TM) Ultra 7 155H", true)]
    [InlineData("AMD Ryzen 7 9700X 8-Core Processor", true)]
    [InlineData("AMD Ryzen 5 1600 Six-Core Processor", false)]
    [InlineData("AMD Ryzen 5 2400G with Radeon Vega Graphics", false)]
    [InlineData("AMD Ryzen 5 2600 Six-Core Processor", true)]
    [InlineData("Intel(R) Core(TM)2 Duo CPU E8400", false)]
    [InlineData("AMD FX-8350 Eight-Core Processor", false)]
    public void Processador_na_lista_do_Windows_11(string name, bool supported) => Assert.Equal(supported, Win11Check.CpuSupported(name));

    [Fact]
    public void Processador_desconhecido_fica_para_conferir() => Assert.Null(Win11Check.CpuSupported("Processador Genérico X"));

    private static Win11Inputs Pc(string cpu = "AMD Ryzen 7 9700X 8-Core Processor", string? tpm = "2.0", bool? uefi = true) =>
        new(cpu, 16, 3.8, true, true, true, 32L << 30, 1000L << 30, uefi, true, tpm, true, 12, 3.2, "NVIDIA GeForce RTX 4070", 3440, 1440, 26200);

    [Fact]
    public void PC_moderno_atende_tudo()
    {
        var list = Win11Check.Evaluate(Pc());
        Assert.Equal(10, list.Count);
        Assert.All(list, r => Assert.Equal(ReqState.Ok, r.State));
    }

    [Fact]
    public void Sem_TPM_e_BIOS_antiga_falham_com_dica()
    {
        var list = Win11Check.Evaluate(Pc(tpm: null, uefi: false));
        var tpm = list.Single(r => r.Title == "TPM 2.0");
        Assert.Equal(ReqState.Fail, tpm.State);
        Assert.Contains("fTPM", tpm.Fix);
        Assert.Equal(ReqState.Fail, list.Single(r => r.Title == "Firmware UEFI").State);
    }

    [Fact]
    public void Le_DirectX_e_WDDM_do_dxdiag()
    {
        const string text = "------------------\nDisplay Devices\n---------------\n           Card name: NVIDIA GeForce RTX 4070\n        DDI Version: 12\n       Driver Model: WDDM 3.1\n";
        var (dx, wddm, gpu) = Win11Check.ParseDxDiag(text);
        Assert.Equal(12, dx);
        Assert.Equal(3.1, wddm);
        Assert.Equal("NVIDIA GeForce RTX 4070", gpu);
    }

    [Fact]
    public void Nome_do_Windows_11_corrigido_pela_compilacao()
    {
        Assert.Equal("Windows 11 Pro 25H2 (compilação 26200.9168)", SystemSummary.Describe("Windows 10 Pro", "25H2", "26200", 9168));
        Assert.Equal("Windows 10 Home 22H2 (compilação 19045)", SystemSummary.Describe("Windows 10 Home", "22H2", "19045", 0));
    }

    // ---------------- mapa do disco

    [Fact]
    public void Mapa_ocupa_a_area_toda_proporcional_aos_tamanhos()
    {
        var sizes = new long[] { 600, 300, 60, 40 };
        var area = new MapRect(0, 0, 400, 200);
        var rects = DiskMap.Layout(sizes, area);
        double total = rects.Sum(r => r.W * r.H);
        Assert.Equal(400 * 200, total, 3);
        for (int i = 0; i < sizes.Length; i++)
            Assert.Equal(sizes[i] / 1000.0, rects[i].W * rects[i].H / (400 * 200), 3);
        Assert.All(rects, r => Assert.True(r.X >= -0.001 && r.Y >= -0.001 && r.X + r.W <= 400.001 && r.Y + r.H <= 200.001));
    }

    [Fact]
    public void Mapa_com_tamanho_zero_nao_quebra()
    {
        var rects = DiskMap.Layout(new long[] { 0, 0 }, new MapRect(0, 0, 100, 100));
        Assert.All(rects, r => Assert.Equal(0, r.W * r.H));
        Assert.Empty(DiskMap.Layout(Array.Empty<long>(), new MapRect(0, 0, 100, 100)));
    }

    [Theory]
    [InlineData("filme.MKV", FileKind.Video)]
    [InlineData("foto.jpg", FileKind.Image)]
    [InlineData("musica.flac", FileKind.Music)]
    [InlineData("contrato.pdf", FileKind.Document)]
    [InlineData("backup.7z", FileKind.Archive)]
    [InlineData("jogo.exe", FileKind.Program)]
    [InlineData("sem-extensao", FileKind.Other)]
    public void Tipo_do_arquivo_pela_extensao(string name, FileKind kind) => Assert.Equal(kind, DiskMap.KindOf(name));

    [Fact]
    public void Varredura_soma_pastas_e_junta_arquivos_pequenos()
    {
        string root = Path.Combine(Path.GetTempPath(), "mbx-map-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "videos"));
        File.WriteAllBytes(Path.Combine(root, "videos", "a.mp4"), new byte[5000]);
        for (int i = 0; i < DiskMap.FilesShown + 3; i++) File.WriteAllBytes(Path.Combine(root, $"f{i}.txt"), new byte[10 + i]);
        try
        {
            var node = DiskMap.Scan(root, null, CancellationToken.None);
            Assert.Equal(DiskMap.FilesShown + 4, node.Files);
            Assert.Equal(5000 + Enumerable.Range(0, DiskMap.FilesShown + 3).Sum(i => 10 + i), node.Size);
            Assert.Equal("videos", node.Children[0].Name);
            Assert.Equal(FileKind.Video, node.Children[0].MainKind);
            Assert.Contains(node.Children, c => c.Name == "3 outros arquivos" && c.Files == 3);
        }
        finally { Directory.Delete(root, true); }
    }

    // ---------------- pontos de restauração

    [Fact]
    public void Data_do_WMI_e_tipo_do_ponto()
    {
        var date = RestorePoints.ParseWmiDate("20261106141100.000000-180");
        Assert.Equal(new DateTime(2026, 11, 6, 14, 11, 0, DateTimeKind.Utc).ToLocalTime(), date);
        Assert.Equal("Instalação de driver", RestorePoints.TypeName(10, ""));
        Assert.Equal("Automático do Windows", RestorePoints.TypeName(12, "Ponto de Verificação Agendado"));
        Assert.Equal("Mudança de configurações", RestorePoints.TypeName(12, "MB Extreme - antes"));
    }

    [Fact]
    public void Espaco_dos_pontos_pelo_vssadmin_em_portugues()
    {
        const string output = "vssadmin 1.1\r\n\r\nAssociação de Armazenamento de Sombra\r\n   Para volume: (C:)\\\\?\\Volume{x}\\\r\n   Volume de Armazenamento: (C:)\\\\?\\Volume{x}\\\r\n" +
                              "   Espaço de Armazenamento de Sombra Usado: 12,0 GB (1%)\r\n   Espaço de Armazenamento de Sombra Alocado: 12,5 GB (1%)\r\n   Espaço Máximo de Armazenamento de Sombra: 18,6 GB (2%)\r\n";
        var s = RestorePoints.ParseShadowStorage(output, "C:\\");
        Assert.NotNull(s);
        Assert.Equal(12.0, s!.Used / (double)(1L << 30), 1);
        Assert.Equal(18.6, s.Max / (double)(1L << 30), 1);
    }

    // ---------------- backup para formatar

    [Fact]
    public void Backup_tem_pasta_propria_e_e_achado_depois()
    {
        string root = Path.Combine(Path.GetTempPath(), "mbx-bkp-" + Guid.NewGuid().ToString("N"));
        string folder = FormatBackup.NewFolder(root, "PC:DO*BRUNO", new DateTime(2026, 11, 6, 19, 30, 0));
        Assert.EndsWith(Path.Combine(FormatBackup.RootFolder, "PC_DO_BRUNO-2026-11-06-1930"), folder);
        Directory.CreateDirectory(folder);
        try
        {
            var info = new FormatBackupInfo("PC-DO-BRUNO", new DateTime(2026, 11, 6), 42, 3, "Windows 11 Pro");
            File.WriteAllText(Path.Combine(folder, FormatBackup.Manifest), FormatBackup.ToJson(info));
            var found = FormatBackup.Find(new[] { root, Path.Combine(root, "nao-existe") });
            Assert.Single(found);
            Assert.Equal(42, found[0].Drivers);
            Assert.Equal(folder, found[0].Folder);
            Assert.Null(FormatBackup.FromJson("{quebrado", folder));
        }
        finally { Directory.Delete(root, true); }
    }

    // ---------------- pendrive falso

    [Fact]
    public void Padrao_do_teste_muda_por_posicao_e_e_conferido()
    {
        var a = new byte[FakeCapacity.BlockSize];
        var b = new byte[FakeCapacity.BlockSize];
        var scratch = new byte[FakeCapacity.BlockSize];
        FakeCapacity.Fill(a, 0);
        FakeCapacity.Fill(b, FakeCapacity.BlockSize);
        Assert.False(a.AsSpan().SequenceEqual(b));
        Assert.True(FakeCapacity.Check(a, a.Length, 0, scratch));
        Assert.False(FakeCapacity.Check(a, a.Length, FakeCapacity.BlockSize, scratch));   // bloco repetido (pendrive falso) é pego
        a[500] ^= 1;
        Assert.False(FakeCapacity.Check(a, a.Length, 0, scratch));
    }

    [Fact]
    public void Teste_de_capacidade_escreve_confere_e_apaga()
    {
        string root = Path.Combine(Path.GetTempPath(), "mbx-cap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var r = FakeCapacity.Run(root, null, CancellationToken.None, limitBytes: 6L * FakeCapacity.BlockSize);
            Assert.True(r.Genuine);
            Assert.Equal(6L * FakeCapacity.BlockSize, r.Written);
            Assert.Empty(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }

    // ---------------- programas

    [Fact]
    public void Catalogo_sem_repetidos_com_ids_validos_e_kits_cheios()
    {
        Assert.Equal(AppCatalog.All.Count, AppCatalog.All.Select(a => a.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(AppCatalog.All, a => Assert.True(WingetService.IsValidId(a.Id), a.Id));
        Assert.All(AppCatalog.All, a => Assert.Contains(a.Category, AppCatalog.Categories));
        Assert.All(AppCatalog.Kits, k => Assert.True(AppCatalog.InKit(k.Key).Count >= 4, k.Key));
        Assert.All(AppCatalog.All.SelectMany(a => a.Kits), k => Assert.Contains(AppCatalog.Kits, kit => kit.Key == k));
    }

    [Fact]
    public void Ja_instalados_pela_saida_do_winget()
    {
        const string output = "Nome            ID              Versão\n---------------------------------\nGoogle Chrome   Google.Chrome   131.0\n7-Zip           7zip.7zip       24.08\n";
        var found = AppCatalog.IdsIn(output, new[] { "Google.Chrome", "7zip.7zip", "VideoLAN.VLC" });
        Assert.True(found.SetEquals(new[] { "Google.Chrome", "7zip.7zip" }));
    }

    // ---------------- drivers

    [Theory]
    [InlineData(22, "Habilitar")]
    [InlineData(28, "Sem driver")]
    [InlineData(43, "porta")]
    [InlineData(999, "999")]
    public void Codigo_do_gerenciador_explicado(int code, string part) => Assert.Contains(part, DeviceDrivers.Explain(code));

    [Fact]
    public void Suporte_do_fabricante_da_placa_mae()
    {
        Assert.Equal("MSI", DeviceDrivers.Brand("Micro-Star International Co., Ltd."));
        Assert.Contains("msi.com", DeviceDrivers.SupportUrl("Micro-Star International Co., Ltd.", "PRO B650-VC WIFI (MS-7D78)"));
        Assert.Contains("asus.com", DeviceDrivers.SupportUrl("ASUSTeK COMPUTER INC.", "TUF GAMING B550M-PLUS"));
        Assert.Null(DeviceDrivers.SupportUrl("Fabricante Desconhecido", "X"));
    }

    [Fact]
    public void Driver_generico_do_Windows()
    {
        Assert.True(DeviceDrivers.IsGeneric("Microsoft", "machine.inf"));
        Assert.True(DeviceDrivers.IsGeneric("(Standard system devices)", "machine.inf"));
        Assert.False(DeviceDrivers.IsGeneric("Microsoft", "oem12.inf"));
        Assert.False(DeviceDrivers.IsGeneric("Realtek", "oem3.inf"));
        Assert.Equal(new DateTime(2025, 10, 6), DeviceDrivers.ParseWmiDate("20251006000000.******+***"));
    }

    // ---------------- som, rede e inicialização

    [Fact]
    public void Nivel_do_microfone_em_decibeis()
    {
        Assert.Equal(0, AudioTests.LevelOf(0));
        Assert.Equal(1, AudioTests.LevelOf(1), 3);
        Assert.Equal(0.5, AudioTests.LevelOf((float)Math.Pow(10, -30 / 20.0)), 3);
        var buffer = new byte[4];
        BitConverter.TryWriteBytes(buffer.AsSpan(2), (short)-16384);
        Assert.Equal(0.5f, AudioTests.Peak16(buffer, 4), 3);
    }

    [Theory]
    [InlineData(20, 22, "A+")]
    [InlineData(20, 45, "A")]
    [InlineData(20, 70, "B")]
    [InlineData(20, 150, "C")]
    [InlineData(20, 400, "F")]
    public void Nota_do_ping_sob_carga(double idle, double loaded, string grade) => Assert.Equal(grade, SpeedMath.Bufferbloat(idle, loaded).Grade);

    [Fact]
    public void Ping_sob_carga_sem_medicao() => Assert.Equal("?", SpeedMath.Bufferbloat(20, null).Grade);

    [Theory]
    [InlineData("GoogleUpdaterTaskUser", "C:\\Users\\x\\AppData\\Local\\Google\\GoogleUpdater\\updater.exe --wake", StartupAdvice.CanDisable)]
    [InlineData("SecurityHealth", "%windir%\\system32\\SecurityHealthSystray.exe", StartupAdvice.KeepOn)]
    [InlineData("Discord", "Update.exe --processStart Discord.exe", StartupAdvice.DisableIfUnused)]
    [InlineData("XyzDesconhecido", "C:\\xyz.exe", StartupAdvice.CheckFirst)]
    public void O_que_e_cada_programa_da_inicializacao(string name, string command, StartupAdvice advice) =>
        Assert.Equal(advice, StartupAdvisor.Describe(name, command).Advice);

    [Fact]
    public void Tempo_para_ligar_pelo_evento_100()
    {
        const string xml = "<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><EventID>100</EventID><TimeCreated SystemTime='2026-11-06T17:10:00.0000000Z'/></System>" +
                           "<EventData><Data Name='BootTime'>34567</Data><Data Name='MainPathBootTime'>12000</Data></EventData></Event>" +
                           "<Event xmlns='x'><System><TimeCreated SystemTime='2026-11-05T10:00:00.0000000Z'/></System><EventData><Data Name='BootTime'>40000</Data></EventData></Event>";
        var boots = StartupAdvisor.ParseBootEvents(xml);
        Assert.Equal(2, boots.Count);
        Assert.Equal(34.567, boots[0].Boot.TotalSeconds, 3);
        Assert.Equal(12, boots[0].MainPath!.Value.TotalSeconds, 3);
        Assert.Null(boots[1].MainPath);
    }
}

/// <summary>Ícones da tela de instalar programas.</summary>
public class IconesProgramasTests
{
    private static readonly System.Text.RegularExpressions.Regex Token =
        new(@"^(?:[MmLlHhVvCcSsQqTtAaZz]|-?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)$");

    [Fact]
    public void Todo_icone_e_de_um_programa_do_catalogo_e_tem_cor_e_desenho_validos()
    {
        Assert.True(AppIcons.Known.Count >= 40);
        foreach (var (id, (color, path)) in AppIcons.Known)
        {
            Assert.Contains(AppCatalog.All, a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
            Assert.Matches("^#[0-9A-Fa-f]{6}$", color);
            Assert.True(path[0] is 'M' or 'm', id);
            Assert.All(path.Split(' '), t => Assert.True(Token.IsMatch(t), $"{id}: \"{t}\""));
        }
    }

    [Theory]
    [InlineData("Visual Studio Code", "VS")]
    [InlineData("WinRAR", "WR")]
    [InlineData("7-Zip", "7Z")]
    [InlineData("Rufus", "Ru")]
    [InlineData(".NET 8 Desktop Runtime", "N8")]
    public void Iniciais_do_icone_de_reserva(string title, string initials) => Assert.Equal(initials, AppIcons.Initials(title));

    [Fact]
    public void Cor_de_reserva_e_sempre_a_mesma_para_o_mesmo_programa()
    {
        Assert.Equal(AppIcons.FallbackColor("RARLab.WinRAR"), AppIcons.FallbackColor("rarlab.winrar"));
        Assert.Matches("^#[0-9A-F]{6}$", AppIcons.FallbackColor("Rufus.Rufus"));
    }
}
