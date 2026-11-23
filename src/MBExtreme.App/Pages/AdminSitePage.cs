using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Administrador › Site: atalhos para cada parte do painel do site (clientes, licenças, afiliados...). O que precisa de tela grande
/// (cadastro de produtos, relatórios) continua no site; aqui é um clique para chegar lá já na aba certa.
/// </summary>
public sealed class AdminSitePage : ToolPageBase
{
    /// <summary>Abas do painel do site, em ordem alfabética (chave da aba no endereço → nome e para que serve).</summary>
    internal static readonly (string Tab, string Title, string Text)[] Sections =
    {
        ("affiliates", "Afiliados e Pix", "Comissões, pedidos de saque e ranking."),
        ("feedback", "Avaliações e indicações", "O que os clientes avaliaram e quem indicou quem."),
        ("licenses", "Chaves de licença", "Criar, trocar o PC e cancelar chaves."),
        ("customers", "Clientes", "Contas, planos, WhatsApp e histórico."),
        ("sponsors", "Patrocinadores", "Anúncios e parceiros da página inicial."),
        ("games", "Perfis de jogos", "Configurações prontas por jogo."),
        ("settings", "Produtos e configurações", "Preços, produtos e textos do site."),
        ("overview", "Visão geral", "Vendas, assinaturas e números do dia."),
    };

    public AdminSitePage() : base("Site", "O painel do site, aberto direto na parte que você precisa. Só a conta de administrador entra.")
    {
        HideHeader();
        var grid = new WrapPanel();
        foreach (var (tab, title, text) in Sections)
        {
            var card = new StackPanel { Width = 250 };
            card.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold });
            card.Children.Add(Muted(text, 11.5));
            var open = SmallButton("Abrir no site", (_, _) => Open(tab), false);
            open.Margin = new Thickness(0, 8, 0, 0);
            open.HorizontalAlignment = HorizontalAlignment.Left;
            card.Children.Add(open);
            var border = CardOf(card);
            border.Margin = new Thickness(0, 0, 10, 10);
            grid.Children.Add(border);
        }
        Body.Children.Add(grid);
    }

    internal static string UrlOf(string tab) => AppInfo.SiteUrl + "/admin?aba=" + tab;

    private void Open(string tab)
    {
        try { Process.Start(new ProcessStartInfo(UrlOf(tab)) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception ex) { SetStatus("Não foi possível abrir o navegador: " + ex.Message, "BadBrush"); }
    }
}
