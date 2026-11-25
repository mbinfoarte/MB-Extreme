using MBExtreme.Core.Memory;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Raio-X da memória: quem ocupa a RAM, o que vem marcado e o que nunca pode ser tocado.</summary>
public class MemoriaRaioXTests
{
    private const long MB = 1024 * 1024;

    private static XrayProcess P(int pid, string name, long mb, bool window = false, string? path = null) => new(pid, name, mb * MB, window, path);

    private static XrayReport Build(IReadOnlyList<XrayProcess> procs, IReadOnlyList<XrayService>? svcs = null, IReadOnlyList<XrayStoreApp>? store = null,
        string[]? startup = null, string[]? allow = null, string? fg = null) =>
        XrayRules.Build(procs, svcs ?? Array.Empty<XrayService>(), store ?? Array.Empty<XrayStoreApp>(), startup ?? Array.Empty<string>(), allow ?? Array.Empty<string>(), fg);

    [Fact]
    public void Servicos_do_mesmo_svchost_dividem_a_memoria_e_cada_um_aparece()
    {
        var r = Build(new[] { P(100, "svchost", 300) },
            new[] { new XrayService("DiagTrack", "Connected User Experiences", 100, 2), new XrayService("WSearch", "Windows Search", 100, 2), new XrayService("Dhcp", "DHCP Client", 100, 2) });
        Assert.Equal(3, r.Items.Count);
        Assert.All(r.Items, i => Assert.Equal(100 * MB, i.Bytes));
        var diag = r.Items.Single(i => i.ServiceName == "DiagTrack");
        Assert.Equal(XrayAction.StopService, diag.Action);
        Assert.True(diag.Preselected);                                   // telemetria: seguro
        Assert.False(r.Items.Single(i => i.ServiceName == "WSearch").Preselected);   // busca: só se não usa
        Assert.True(r.Items.Single(i => i.ServiceName == "Dhcp").Protected);         // rede: nunca
        Assert.DoesNotContain(r.Items, i => i.Name == "svchost");                    // o svchost some: aparece por serviço
    }

    [Fact]
    public void Programa_com_janela_nunca_vem_marcado_e_ajudante_de_fundo_vem()
    {
        var r = Build(new[] { P(1, "chrome", 2000, window: true), P(2, "chrome", 1400), P(3, "OneDrive", 350), P(4, "GoogleUpdate", 40) }, startup: new[] { "OneDrive" });
        var chrome = r.Items.Single(i => i.Name == "chrome");
        Assert.Equal(XrayKind.App, chrome.Kind);
        Assert.Equal(3400 * MB, chrome.Bytes);
        Assert.False(chrome.Preselected);
        var update = r.Items.Single(i => i.Name == "GoogleUpdate");
        Assert.Equal(XrayKind.Background, update.Kind);
        Assert.True(update.Preselected);
    }

    [Fact]
    public void Ajudante_que_abre_com_o_Windows_sai_tambem_da_inicializacao_menos_a_nuvem()
    {
        var r = Build(new[] { P(3, "OneDrive", 350), P(4, "AdobeARM", 60) }, startup: new[] { "OneDrive", "AdobeARM" });
        var adobe = r.Items.Single(i => i.Name == "AdobeARM");
        Assert.True(adobe.Preselected);
        Assert.Equal(XrayAction.CloseAndStartup, adobe.Action);
        Assert.Equal("AdobeARM", adobe.StartupName);
        var od = r.Items.Single(i => i.Name == "OneDrive");
        Assert.True(od.Preselected);
        Assert.Equal(XrayAction.Close, od.Action);      // fecha agora, mas a sincronização continua abrindo com o Windows
        Assert.Null(od.StartupName);
    }

    [Fact]
    public void Windows_seguranca_driver_e_o_proprio_MB_ficam_protegidos()
    {
        var r = Build(new[] { P(1, "explorer", 200, true), P(2, "MsMpEng", 300), P(3, "NVDisplay.Container", 90), P(4, "MBExtreme", 150, true),
            P(5, "Memory Compression", 900), P(6, "steam", 200) });
        Assert.All(r.Items, i => Assert.True(i.Protected, i.Name));
        Assert.All(r.Items, i => Assert.False(i.Preselected));
    }

    [Fact]
    public void Lista_de_permissoes_protege_programa_e_servico()
    {
        var r = Build(new[] { P(1, "MeuSistema", 500), P(9, "svchost", 50) }, new[] { new XrayService("DiagTrack", "Telemetria", 9, 2) },
            allow: new[] { "MeuSistema", "DiagTrack" });
        Assert.All(r.Items, i => Assert.True(i.Protected, i.Name));
    }

    [Fact]
    public void App_da_Store_em_segundo_plano_aparece_com_nome_amigavel_e_bloqueio()
    {
        string path = @"C:\Program Files\WindowsApps\MSTeams_24295.605.3225.8804_x64__8wekyb3d8bbwe\ms-teams.exe";
        var r = Build(new[] { P(10, "ms-teams", 900, path: path), P(11, "msedgewebview2", 300, path: path) },
            store: new[] { new XrayStoreApp("MSTeams_8wekyb3d8bbwe", false), new XrayStoreApp("Microsoft.BingNews_8wekyb3d8bbwe", false), new XrayStoreApp("Microsoft.XboxApp_8wekyb3d8bbwe", false) });
        var teams = r.Items.Single(i => i.PackageFamily == "MSTeams_8wekyb3d8bbwe");
        Assert.Equal("Microsoft Teams", teams.Name);
        Assert.Equal(1200 * MB, teams.Bytes);
        Assert.Equal(XrayAction.BlockBackground, teams.Action);
        Assert.True(teams.Preselected);
        Assert.Contains(r.Items, i => i.PackageFamily == "Microsoft.BingNews_8wekyb3d8bbwe" && i.Preselected && i.Bytes == 0);   // fechado, mas pode abrir sozinho
        Assert.DoesNotContain(r.Items, i => i.PackageFamily?.StartsWith("Microsoft.Xbox") == true);                            // Xbox: jogos, fica
    }

    [Fact]
    public void App_da_Store_aberto_com_janela_nao_vem_marcado()
    {
        string path = @"C:\Program Files\WindowsApps\5319275A.WhatsAppDesktop_2.2450.6.0_x64__cv1g1gvanyjgm\WhatsApp.exe";
        var r = Build(new[] { P(10, "WhatsApp", 600, window: true, path: path) }, fg: "WhatsApp");
        var w = r.Items.Single();
        Assert.False(w.Preselected);
        Assert.Equal("Você está usando", w.Reason);
    }

    [Theory]
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.YourPhone_1.24.0.0_x64__8wekyb3d8bbwe\PhoneExperienceHost.exe", "Microsoft.YourPhone_8wekyb3d8bbwe")]
    [InlineData(@"C:\Program Files\Google\Chrome\chrome.exe", null)]
    [InlineData(null, null)]
    public void Familia_do_pacote_pelo_caminho(string? path, string? family) => Assert.Equal(family, XrayRules.PackageFamilyFromPath(path));

    [Theory]
    [InlineData("\"C:\\Users\\x\\AppData\\Local\\Microsoft\\OneDrive\\OneDrive.exe\" /background", "OneDrive")]
    [InlineData("C:\\Program Files\\Steam\\steam.exe -silent", "steam")]
    [InlineData("C:\\Users\\x\\AppData\\Roaming\\Microsoft\\Windows\\Start Menu\\Programs\\Startup\\Discord.lnk", "Discord")]
    public void Executavel_da_inicializacao(string command, string exe) => Assert.Equal(exe, XrayRules.StartupExe(command));

    [Fact]
    public void Limpeza_rapida_so_pega_ajudante_e_servico_sem_mudar_a_Store()
    {
        string path = @"C:\Program Files\WindowsApps\MSTeams_1.0.0.0_x64__8wekyb3d8bbwe\ms-teams.exe";
        var r = Build(new[] { P(1, "chrome", 2000, true), P(10, "ms-teams", 900, path: path), P(9, "svchost", 60) },
            new[] { new XrayService("DiagTrack", "Telemetria", 9, 2) });
        var quick = XrayRules.QuickPick(r);
        Assert.Contains(quick, i => i.ServiceName == "DiagTrack");
        Assert.DoesNotContain(quick, i => i.Kind == XrayKind.StoreApp || i.Kind == XrayKind.App);
    }

    [Fact]
    public void Total_marcado_e_aviso_de_memoria()
    {
        var r = Build(new[] { P(9, "svchost", 60) }, new[] { new XrayService("DiagTrack", "Telemetria", 9, 2) });
        Assert.Equal(60 * MB, r.PreselectedBytes);
        Assert.True(XrayRules.ShouldWarn(97, 95));
        Assert.False(XrayRules.ShouldWarn(97, 70));    // a limpeza comum resolveu
        Assert.False(XrayRules.ShouldWarn(80, 80));
    }
}
