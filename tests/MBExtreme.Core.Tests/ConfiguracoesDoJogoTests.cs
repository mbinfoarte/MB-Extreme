using System.Text;
using MBExtreme.Core.Games;
using Xunit;

namespace MBExtreme.Core.Tests;

public class ConfiguracoesDoJogoTests
{
    // ------------------------------------------------------------ tipo de janela

    [Theory]
    [InlineData(GameEngine.Unity, "UnityPlayer.dll", "Jogo_Data\\globalgamemanagers")]
    [InlineData(GameEngine.Unreal, "Jogo\\Binaries\\Win64\\Jogo-Win64-Shipping.exe", "Engine\\Binaries\\ThirdParty\\x.dll")]
    [InlineData(GameEngine.Source2, "game\\bin\\win64\\cs2.exe", "game\\csgo\\gameinfo.gi")]
    [InlineData(GameEngine.Source, "hl2.exe", "hl2\\gameinfo.txt")]
    [InlineData(GameEngine.Desconhecido, "jogo.exe", "dados.pak")]
    public void Motor_do_jogo_pelos_arquivos(GameEngine esperado, string a, string b) => Assert.Equal(esperado, GameWindow.DetectEngine(new[] { a, b }));

    [Fact]
    public void Opcoes_de_janela_por_motor()
    {
        Assert.Equal("", GameWindow.LaunchArgs(GameEngine.Unity, GameWindowMode.Padrao));
        Assert.Equal("-screen-fullscreen 1 -window-mode borderless", GameWindow.LaunchArgs(GameEngine.Unity, GameWindowMode.SemBordas));
        Assert.Equal("-screen-fullscreen 0", GameWindow.LaunchArgs(GameEngine.Unity, GameWindowMode.Janela));
        Assert.Equal("-windowed -noborder", GameWindow.LaunchArgs(GameEngine.Source2, GameWindowMode.SemBordas));
        Assert.Equal("-fullscreen", GameWindow.LaunchArgs(GameEngine.Unreal, GameWindowMode.TelaCheia));
        Assert.Equal("", GameWindow.LaunchArgs(GameEngine.Unreal, GameWindowMode.SemBordas));    // sem bordas vai pelo .ini
        Assert.Null(GameWindow.LaunchArgs(GameEngine.Desconhecido, GameWindowMode.Janela));     // não inventa opção
        Assert.Equal(1, GameWindow.UnrealFullscreenMode(GameWindowMode.SemBordas));
    }

    [Fact]
    public void Ini_do_unreal_troca_so_o_modo_de_tela_e_mantem_o_resto()
    {
        string ini = "[/Script/Engine.GameUserSettings]\r\nbUseVSync=False\r\nFullscreenMode=0\r\nResolutionSizeX=1920\r\n\r\n[ScalabilityGroups]\r\nsg.ShadowQuality=3\r\n";
        string novo = GameWindow.SetUnrealFullscreenMode(ini, 1);
        Assert.Contains("FullscreenMode=1\r\n", novo);
        Assert.Contains("LastConfirmedFullscreenMode=1", novo);
        Assert.Contains("PreferredFullscreenMode=1", novo);
        Assert.Contains("bUseVSync=False", novo);
        Assert.Contains("sg.ShadowQuality=3", novo);
        Assert.True(novo.IndexOf("PreferredFullscreenMode", StringComparison.Ordinal) < novo.IndexOf("[ScalabilityGroups]", StringComparison.Ordinal));
        Assert.DoesNotContain("FullscreenMode=0", novo);
        // sem a seção: cria
        Assert.Contains("[/Script/Engine.GameUserSettings]\nFullscreenMode=2", GameWindow.SetUnrealFullscreenMode("[Outra]\nx=1", 2));
    }

    [Fact]
    public void Compatibilidade_do_windows_mantem_as_outras_bandeiras()
    {
        Assert.Equal("~ DISABLEDXMAXIMIZEDWINDOWEDMODE", GameWindow.MergeLayers(null, GameWindow.DisableFsoFlag, true));
        Assert.Equal("~ RUNASADMIN DISABLEDXMAXIMIZEDWINDOWEDMODE", GameWindow.MergeLayers("~ RUNASADMIN", GameWindow.DisableFsoFlag, true));
        Assert.Equal("~ RUNASADMIN", GameWindow.MergeLayers("~ RUNASADMIN DISABLEDXMAXIMIZEDWINDOWEDMODE", GameWindow.DisableFsoFlag, false));
        Assert.Null(GameWindow.MergeLayers("~ DISABLEDXMAXIMIZEDWINDOWEDMODE", GameWindow.DisableFsoFlag, false));
    }

    [Fact]
    public void Opcoes_de_inicializacao_sem_caracteres_perigosos()
    {
        Assert.Equal("-novid -high +fps_max 0", GameWindow.CleanArgs("  -novid   -high +fps_max 0 "));
        Assert.Equal("-novid calc.exe", GameWindow.CleanArgs("-novid & calc.exe"));
        Assert.Equal("-x", GameWindow.CleanArgs("-x | \"\" < >"));
        Assert.Equal("", GameWindow.CleanArgs(null));
        Assert.True(GameWindow.CleanArgs(new string('a', 500)).Length <= 300);
    }

    [Fact]
    public void Abrir_com_opcoes_steam_usa_applaunch_e_epic_nao_passa()
    {
        string steamExe = Path.Combine(Path.GetTempPath(), $"steam-{Guid.NewGuid():N}.exe");
        File.WriteAllText(steamExe, "x");
        try
        {
            var steam = new InstalledGame("steam:730", "CS2", GameLauncher.Steam, "C:\\Jogos\\cs2", "cs2.exe", "steam://rungameid/730", "730");
            var plan = GameWindow.LaunchPlan(steam, steamExe, "-windowed -noborder", null);
            Assert.NotNull(plan);
            Assert.Equal("-applaunch 730 -windowed -noborder", plan!.Value.Arguments);
            var epic = new InstalledGame("epic:x", "Jogo", GameLauncher.Epic, "C:\\Jogos\\x", "x.exe", "com.epicgames.launcher://apps/x?action=launch", null);
            Assert.Null(GameWindow.LaunchPlan(epic, steamExe, "-windowed", null));
            Assert.Null(GameWindow.LaunchPlan(steam, steamExe, "", null));
        }
        finally { File.Delete(steamExe); }
    }

    // ------------------------------------------------------------ DLSS

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070 Laptop GPU", 40)]
    [InlineData("NVIDIA GeForce RTX 5090", 50)]
    [InlineData("NVIDIA GeForce RTX 2060 SUPER", 20)]
    [InlineData("NVIDIA RTX PRO 6000 Blackwell", 60)]
    public void Geracao_da_rtx_pelo_nome(string nome, int geracao) => Assert.Equal(geracao, RtxCapability.FromName(nome)!.Generation);

    [Theory]
    [InlineData("NVIDIA GeForce GTX 1660")]
    [InlineData("AMD Radeon RX 7800 XT")]
    [InlineData(null)]
    public void Sem_rtx_sem_dlss(string? nome) => Assert.Null(RtxCapability.FromName(nome));

    [Fact]
    public void Rtx40_tem_geracao_2x_e_rtx50_ate_4x()
    {
        Assert.Equal(2, new RtxCapability(40).MaxMultiplier);
        Assert.Equal(4, new RtxCapability(50).MaxMultiplier);
        Assert.False(new RtxCapability(30).FrameGeneration);
    }

    private static readonly DlssFiles Tudo = new("3.7.10.0", "3.7.10.0", "3.7.10.0");

    [Fact]
    public void Plano_do_dlss_mais_recente_com_4x_na_rtx50()
    {
        var (write, remove) = DlssTuning.Plan("latest", "dlaa", 4, true, new RtxCapability(50), Tudo);
        uint V(uint id) => write.Single(w => w.Id == id).Value;
        Assert.Equal(1u, V(DlssIds.SrOverride));
        Assert.Equal(DlssIds.PresetLatest, V(DlssIds.SrPreset));
        Assert.Equal(4u, V(DlssIds.SrMode));
        Assert.Equal(1u, V(DlssIds.FgOverride));
        Assert.Equal(3u, V(DlssIds.MultiFrameCount));     // 4x = 3 quadros gerados
        Assert.Equal(1u, V(DlssIds.RrOverride));
        Assert.Empty(remove);
    }

    [Fact]
    public void Plano_respeita_a_placa_e_o_que_o_jogo_tem()
    {
        // RTX 30: sem geração de quadros mesmo pedindo
        var rtx30 = DlssTuning.Plan("M", "game", 4, false, new RtxCapability(30), Tudo);
        Assert.Equal(13u, rtx30.Write.Single(w => w.Id == DlssIds.SrPreset).Value);   // preset M
        Assert.DoesNotContain(rtx30.Write, w => w.Id == DlssIds.FgOverride);
        Assert.Contains(DlssIds.FgOverride, rtx30.Remove);
        Assert.Contains(DlssIds.SrMode, rtx30.Remove);
        // RTX 40 pedindo 4x: liga a geração 2x, sem multiplicador
        var rtx40 = DlssTuning.Plan("latest", "game", 4, false, new RtxCapability(40), Tudo);
        Assert.Contains(rtx40.Write, w => w.Id == DlssIds.FgOverride);
        Assert.DoesNotContain(rtx40.Write, w => w.Id == DlssIds.MultiFrameCount);
        // jogo sem Ray Reconstruction: não liga
        var semRr = DlssTuning.Plan("latest", "game", 0, true, new RtxCapability(40), new DlssFiles("3.5", null, null));
        Assert.DoesNotContain(semRr.Write, w => w.Id == DlssIds.RrOverride);
        // tudo no padrão: só remove
        var padrao = DlssTuning.Plan("game", "game", 0, false, new RtxCapability(40), Tudo);
        Assert.Empty(padrao.Write);
        Assert.Contains(DlssIds.SrOverride, padrao.Remove);
        // sem RTX: remove tudo
        Assert.Equal(DlssIds.All.Length, DlssTuning.Plan("latest", "dlaa", 2, true, null, Tudo).Remove.Count);
    }

    [Fact]
    public void Le_de_volta_o_que_esta_no_driver()
    {
        var driver = new Dictionary<uint, uint> { [DlssIds.SrOverride] = 1, [DlssIds.SrPreset] = 11, [DlssIds.SrMode] = 2, [DlssIds.FgOverride] = 1, [DlssIds.MultiFrameCount] = 2 };
        var now = DlssTuning.Current(id => driver.TryGetValue(id, out var v) ? v : null);
        Assert.Equal("K", now.Model);
        Assert.Equal("quality", now.Mode);
        Assert.Equal(3, now.FrameGen);
        Assert.False(now.Rr);
        Assert.Equal(("game", "game", 0, false), DlssTuning.Current(_ => null));
    }

    [Fact]
    public void Ids_do_dlss_sao_os_do_cabecalho_oficial_da_nvidia()
    {
        // NvApiDriverSettings.h (R620): NGX_DLSS_SR_OVERRIDE_ID, _SR_OVERRIDE_RENDER_PRESET_SELECTION_ID, _SR_MODE_ID, _FG_OVERRIDE_ID, NGX_DLSSG_MULTI_FRAME_COUNT_ID
        Assert.Equal(0x10E41E01u, DlssIds.SrOverride);
        Assert.Equal(0x10E41DF3u, DlssIds.SrPreset);
        Assert.Equal(0x10AFB768u, DlssIds.SrMode);
        Assert.Equal(0x10E41E03u, DlssIds.FgOverride);
        Assert.Equal(0x104D6667u, DlssIds.MultiFrameCount);
        Assert.Equal(13u, DlssTuning.PresetValue('M'));
    }

    // ------------------------------------------------------------ perfis e .nip

    private const string Nip = """
        <?xml version="1.0" encoding="utf-16"?>
        <ArrayOfProfile>
          <Profile>
            <ProfileName>Counter-strike 2</ProfileName>
            <Executeables><string>cs2.exe</string></Executeables>
            <Settings>
              <ProfileSetting><SettingNameInfo>Vertical Sync</SettingNameInfo><SettingID>11041231</SettingID><SettingValue>138504007</SettingValue><ValueType>Dword</ValueType></ProfileSetting>
              <ProfileSetting><SettingNameInfo>Algo novo</SettingNameInfo><SettingID>277041154</SettingID><SettingValue>237</SettingValue><ValueType>Dword</ValueType></ProfileSetting>
              <ProfileSetting><SettingNameInfo>Texto</SettingNameInfo><SettingID>123</SettingID><SettingValue>abc</SettingValue><ValueType>String</ValueType></ProfileSetting>
              <ProfileSetting><SettingNameInfo>Ruim</SettingNameInfo><SettingID>-5</SettingID><SettingValue>1</SettingValue><ValueType>Dword</ValueType></ProfileSetting>
            </Settings>
          </Profile>
        </ArrayOfProfile>
        """;

    [Fact]
    public void Nip_le_so_dword_valido_com_nome_em_portugues()
    {
        var profiles = DriverPresets.ParseNip(Nip.Replace("utf-16", "utf-8"));
        var p = Assert.Single(profiles);
        Assert.Equal("Counter-strike 2", p.Name);
        Assert.Equal(new[] { "cs2.exe" }, p.Executables);
        Assert.Equal(2, p.Settings.Count);
        Assert.Equal(2, p.Skipped);
        Assert.Equal("Sincronia vertical", p.Settings[0].Name);        // 0x00A879CF
        Assert.Equal(DriverPresets.VSyncOff, p.Settings[0].Value);
        Assert.Equal("Limite de FPS", p.Settings[1].Name);             // 0x10835002
        Assert.Same(p, DriverPresets.PickFor(profiles, "CS2.EXE"));
    }

    [Fact]
    public void Nip_com_entidade_externa_e_recusado()
    {
        string xxe = "<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY e SYSTEM \"file:///c:/windows/win.ini\">]><ArrayOfProfile><Profile><ProfileName>&e;</ProfileName></Profile></ArrayOfProfile>";
        Assert.Empty(DriverPresets.ParseNip(xxe));
        Assert.Empty(DriverPresets.ParseNip("isso não é xml"));
    }

    [Fact]
    public void Perfis_prontos_usam_valores_do_cabecalho_oficial()
    {
        Assert.Equal(3, DriverPresets.All.Count);
        var comp = DriverPresets.All.Single(p => p.Key == "competitive");
        Assert.Contains(comp.Settings, s => s.Id == 0x00A879CF && s.Value == 0x08416747);    // VSYNCMODE_FORCEOFF
        Assert.Contains(comp.Settings, s => s.Id == 0x00CE2691 && s.Value == 0x14);          // QUALITY_ENHANCEMENTS_HIGHPERFORMANCE
        Assert.Contains(comp.Settings, s => s.Id == 0x1057EB71 && s.Value == 1);             // PREFERRED_PSTATE_PREFER_MAX
        Assert.Equal(1023u, DriverPresets.FpsLimit(5000).Value);
    }

    // ------------------------------------------------------------ driver: voltar só alguns itens

    private sealed class FakeDriver : IDriverProfileStore
    {
        public bool Available => true;
        public Dictionary<(string, uint), uint> Own = new();
        public DriverValue? Read(string exe, uint id) => Own.TryGetValue((exe, id), out var v) ? new DriverValue(v, true) : null;
        public string? Write(string exe, uint id, uint value) { Own[(exe, id)] = value; return null; }
        public string? Remove(string exe, uint id) { Own.Remove((exe, id)); return null; }
    }

    private sealed class FakeGpu : IGpuPreferenceStore
    {
        public GpuPreferenceMode Get(string p) => GpuPreferenceMode.Default;
        public void Set(string p, GpuPreferenceMode m) { }
    }

    [Fact]
    public void Voltar_ao_padrao_devolve_o_valor_de_antes_e_nao_mexe_no_resto()
    {
        var d = new FakeDriver();
        d.Own[("g.exe", DlssIds.SrPreset)] = 11;   // o usuário já tinha o preset K pelo NVIDIA App
        var svc = new GameDriverService(d, new FakeGpu(), Path.Combine(Path.GetTempPath(), $"mbx-reset-{Guid.NewGuid():N}.json"));
        var plan = DlssTuning.Plan("latest", "dlaa", 0, false, new RtxCapability(40), Tudo);
        svc.Apply("g", "g.exe", null, new DriverPlan(1, null, null), plan.Write);
        Assert.Equal(DlssIds.PresetLatest, d.Own[("g.exe", DlssIds.SrPreset)]);
        Assert.Equal(1u, d.Own[("g.exe", DriverSettingIds.PowerMode)]);

        svc.Reset("g", "g.exe", DlssIds.All);
        Assert.Equal(11u, d.Own[("g.exe", DlssIds.SrPreset)]);             // voltou ao do usuário
        Assert.False(d.Own.ContainsKey(("g.exe", DlssIds.SrMode)));        // não existia: removido
        Assert.Equal(1u, d.Own[("g.exe", DriverSettingIds.PowerMode)]);    // energia não foi tocada
        svc.Undo("g");
        Assert.False(d.Own.ContainsKey(("g.exe", DriverSettingIds.PowerMode)));
    }

    // ------------------------------------------------------------ capas da internet

    [Theory]
    [InlineData("https://cdn.exemplo.com/capa.jpg", true)]
    [InlineData("http://cdn.exemplo.com/capa.jpg", false)]
    [InlineData("https://localhost/capa.jpg", false)]
    [InlineData("https://192.168.0.10/capa.jpg", false)]
    [InlineData("https://10.0.0.1/a.png", false)]
    [InlineData("https://[::1]/a.png", false)]
    [InlineData("https://8.8.8.8/a.png", true)]
    [InlineData("https://exemplo.com:8443/a.png", false)]
    [InlineData("file:///c:/a.png", false)]
    [InlineData("https://intranet/a.png", false)]
    [InlineData(null, false)]
    public void Link_de_capa_so_https_publico(string? url, bool ok) => Assert.Equal(ok, WebCoverSearch.IsSafeUrl(url));

    [Fact]
    public void Imagem_de_verdade_pelo_conteudo()
    {
        Assert.True(WebCoverSearch.LooksLikeImage(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0 }));
        Assert.True(WebCoverSearch.LooksLikeImage(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 }));
        Assert.True(WebCoverSearch.LooksLikeImage(Encoding.ASCII.GetBytes("RIFF1234WEBPVP8 ")));
        Assert.False(WebCoverSearch.LooksLikeImage(Encoding.ASCII.GetBytes("<html><body>nao</body>")));
        Assert.False(WebCoverSearch.LooksLikeImage(Encoding.ASCII.GetBytes("MZ\u0090\0executavel...")));
    }

    [Fact]
    public void Busca_na_internet_le_duckduckgo_e_bing()
    {
        Assert.Equal("4-1234567890-123", WebCoverSearch.ParseDuckToken("<script>vqd=\"4-1234567890-123\";</script>"));
        Assert.Equal("4-99-1", WebCoverSearch.ParseDuckToken("...,vqd:'4-99-1',..."));
        string json = """{"results":[{"image":"https://a.com/larga.jpg","thumbnail":"https://t.com/1.jpg","title":"Larga","width":1920,"height":1080},{"image":"https://a.com/capa.jpg","thumbnail":"https://t.com/2.jpg","title":"Capa","width":600,"height":900},{"image":"http://inseguro.com/x.jpg","thumbnail":"https://t.com/3.jpg","title":"x"}]}""";
        var duck = WebCoverSearch.ParseDuckResults(json);
        Assert.Equal(2, duck.Count);
        Assert.Equal("Capa", duck[0].Name);           // vertical primeiro
        Assert.True(duck[0].IsWeb);
        Assert.Equal("https://a.com/capa.jpg", duck[0].DownloadUrl);
        string html = "<a class=\"iusc\" style=\"x\" m=\"{&quot;murl&quot;:&quot;https://img.com/c.jpg&quot;,&quot;turl&quot;:&quot;https://tse.mm.bing.net/th?id=1&quot;,&quot;t&quot;:&quot;Capa &amp;amp; arte&quot;}\" href=\"/x\">";
        var bing = WebCoverSearch.ParseBing(html);
        Assert.Single(bing);
        Assert.Equal("https://img.com/c.jpg", bing[0].DownloadUrl);
        Assert.Empty(WebCoverSearch.ParseDuckResults("não é json"));
    }

    [Fact]
    public void Capa_de_arquivo_do_pc_so_se_for_imagem()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"mbx-capa-{Guid.NewGuid():N}");
        string fake = Path.Combine(Path.GetTempPath(), $"mbx-falsa-{Guid.NewGuid():N}.jpg");
        string real = Path.Combine(Path.GetTempPath(), $"mbx-real-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllText(fake, "não sou imagem");
            File.WriteAllBytes(real, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6 });
            Assert.Null(WebCoverSearch.SaveFromFile(fake, folder, "steam:1").File);
            var ok = WebCoverSearch.SaveFromFile(real, folder, "steam:1");
            Assert.NotNull(ok.File);
            Assert.Equal(CoverSearch.FindCustom(folder, "steam:1"), ok.File);
        }
        finally
        {
            File.Delete(fake); File.Delete(real);
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }
}
