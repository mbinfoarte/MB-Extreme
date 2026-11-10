using MBExtreme.Core.Games;
using MBExtreme.Core.Windows;
using Xunit;

namespace MBExtreme.Core.Tests;

public class ModoJogoPainelTests
{
    [Fact]
    public void Nunca_mexe_no_sistema_antitrapaca_lojas_voz_e_captura()
    {
        foreach (var name in new[] { "csrss", "explorer", "MsMpEng", "EasyAntiCheat", "vgc", "BEService", "steam", "EpicGamesLauncher", "Discord", "obs64", "Spotify", "audiodg", "nvcontainer", "MBExtreme" })
        {
            Assert.False(GameModeRules.CanDeprioritize(name, 1, 1, false), name);
            Assert.False(GameModeRules.CanAutoClose(name), name);
        }
        Assert.False(GameModeRules.AutoClose.Any(a => GameModeRules.NeverTouch.Contains(a)), "lista de fechar não pode cruzar com a de nunca mexer");
    }

    [Fact]
    public void Segundo_plano_so_na_sessao_do_usuario_e_nunca_o_jogo()
    {
        Assert.True(GameModeRules.CanDeprioritize("chrome", 1, 1, false));
        Assert.False(GameModeRules.CanDeprioritize("chrome", 0, 0, false));            // serviços (sessão 0)
        Assert.False(GameModeRules.CanDeprioritize("chrome", 2, 1, false));            // outro usuário
        Assert.False(GameModeRules.CanDeprioritize("cs2", 1, 1, true));                // o jogo
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Telegram" };
        Assert.False(GameModeRules.CanDeprioritize("telegram", 1, 1, false, keep));    // "nunca mexer" do usuário
    }

    [Fact]
    public void Fecha_so_ajudantes_e_respeita_o_nunca_mexer()
    {
        Assert.True(GameModeRules.CanAutoClose("OneDrive"));
        Assert.True(GameModeRules.CanAutoClose("jusched"));
        Assert.False(GameModeRules.CanAutoClose("chrome"));                           // programa do usuário: só segundo plano
        Assert.False(GameModeRules.CanAutoClose("OneDrive", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "onedrive" }));
        Assert.All(GameModeRules.ReopenAfter, r => Assert.Contains(r, GameModeRules.AutoClose));
        Assert.Contains(GameModeRules.PausableServices, s => s.Service == "WSearch");
        Assert.DoesNotContain(GameModeRules.PausableServices, s => s.Service is "wuauserv" or "WinDefend" or "Audiosrv");
    }

    [Fact]
    public void Resumo_do_modo_jogo()
    {
        Assert.Equal("Modo Jogo: fechou 3 programa(s) de fundo, deixou 12 em segundo plano, pausou 2 tarefa(s) do Windows, liberou 1,5 GB de RAM.",
            GameModeRules.Summary(3, 12, 2, (long)(1.5 * 1024 * 1024 * 1024)).Replace("1.5", "1,5"));
        Assert.StartsWith("Nada para aliviar", GameModeRules.Summary(0, 0, 0, 0));
        var r = new BoostReport(new[] { "OneDrive" }, null, true, 0, 5, new[] { "Indexação" });
        Assert.Contains("deixou 5", r.Summary);
    }

    [Fact]
    public void Botao_direito_do_windows_chama_o_programa_com_a_acao()
    {
        var plan = ShellActions.RegistryPlan(@"C:\Program Files\MB Extreme\MBExtreme.exe");
        Assert.Contains(plan, p => p.Key == @"DesktopBackground\Shell\MBExtreme" && p.Name == "SubCommands" && p.Value == "");
        Assert.Contains(plan, p => p.Key.StartsWith(@"DesktopBackground\Shell\MBExtreme\shell\") && p.Key.EndsWith(@"otimizar\command")
            && p.Value == "\"C:\\Program Files\\MB Extreme\\MBExtreme.exe\" --acao otimizar");
        Assert.Contains(plan, p => p.Key.StartsWith(@"Directory\shell\MBExtreme") && p.Value.EndsWith("--acao mapa \"%1\""));
        Assert.Contains(plan, p => p.Key.StartsWith(@"SystemFileAssociations\.exe\shell\MBExtreme") && p.Value.EndsWith("--acao fps \"%1\""));
        Assert.All(plan, p => Assert.DoesNotContain("..", p.Key));
    }

    [Fact]
    public void Le_a_acao_dos_argumentos()
    {
        var a = ShellActions.Parse(new[] { "--acao", "mapa", @"D:\Jogos" });
        Assert.NotNull(a);
        Assert.Equal("diskmap", a!.Value.Action.Page);
        Assert.Equal(@"D:\Jogos", a.Value.Path);
        Assert.Null(ShellActions.Parse(new[] { "--acao", "formatar-tudo" }));   // ação desconhecida: ignora
        Assert.Null(ShellActions.Parse(new[] { "--acao" }));
        Assert.Null(ShellActions.Parse(Array.Empty<string>()));
        Assert.True(ShellActions.Find("otimizar")!.AutoStart);
    }
}
