using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core;
using MBExtreme.Core.Update;

namespace MBExtreme.UI.Pages;

/// <summary>Sobre: versão, licença, como o programa protege o PC, créditos e "O que há de novo".</summary>
public sealed class AboutPage : ToolPageBase
{
    public AboutPage() : base("Sobre", $"{AppInfo.Name} · versão {AppInfo.Version} · compilação {AppUpdates.CurrentBuild()} · feito pela MB InfoArt.")
    {
        AddButton("O que há de novo", (_, _) => WhatsNew.Show(Window.GetWindow(this)), false);
        AddButton("Verificar se há atualização", (_, _) => MainWindow.Current?.CheckForUpdates());
        AddButton("Abrir pasta do histórico e dos registros", (_, _) => OpenData(), false);

        var license = new StackPanel();
        license.Children.Add(new TextBlock { Text = "Licença: " + AppServices.License.Summary, FontSize = 15, FontWeight = FontWeights.SemiBold });
        license.Children.Add(Muted("No plano grátis o selo \"Licença: Grátis\" fica no alto do menu. Os recursos Pro liberam quando você assina, em Licença e conta."));
        Body.Children.Add(CardOf(license));

        Body.Children.Add(Heading("Como o MB Extreme protege você"));
        foreach (var line in new[]
        {
            "Antes de aplicar um ajuste, guarda como ele estava: dá para voltar exatamente ao original na Central Desfazer.",
            "Cria um ponto de restauração do Windows antes de correções e otimizações maiores.",
            "Nunca desliga sozinho as proteções do Windows (Defender, Firewall, Windows Update, BitLocker, UAC e SmartScreen).",
            "A Limpeza pula arquivos em uso e nunca entra em atalhos de pasta, para não apagar nada fora do lugar.",
            "A análise do Painel só lê; nada muda até você clicar em Corrigir.",
            "Tudo o que o programa faz fica anotado no diário (Central Desfazer), com data e hora.",
        })
            Body.Children.Add(Muted("•  " + line, 12.5));

        Body.Children.Add(Heading("Créditos"));
        foreach (var line in new[]
        {
            "Programas instalados pelo winget, o instalador oficial da Microsoft, direto dos sites de cada fabricante. O MB Extreme não redistribui esses programas.",
            "Sensores por LibreHardwareMonitor (MPL 2.0). Gravação e transmissão por FFmpeg (LGPL/GPL). Áudio por NAudio (MIT). QR Code por QRCoder (MIT).",
            "Drivers e redes Wi-Fi do backup pelas ferramentas do próprio Windows (pnputil e netsh).",
            "Teste de internet pelo servidor público de velocidade da Cloudflare.",
        })
            Body.Children.Add(Muted("•  " + line, 12));
    }

    private static void OpenData()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme");
        Directory.CreateDirectory(folder);
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }
}

/// <summary>Janela "O que há de novo" (abre sozinha uma vez depois de cada atualização).</summary>
internal static class WhatsNew
{
    public static readonly string[] Items =
    {
        "Visual novo: menu lateral em seções (Meu PC, Testes, Otimizar, Segurança) com cada tela a um clique, e o rodapé com tamanho da letra e versão.",
        "Painel: botão ANALISAR com a nota de saúde do PC (0 a 100). Os itens aparecem conforme são conferidos e cada um tem Corrigir, Resolver e Ignorar.",
        "Novas telas: Windows 11 (requisitos), Mapa do disco, Pontos de restauração, Backup para formatar (drivers e Wi-Fi) e Pendrive original? (capacidade falsa).",
        "Otimizações: todos os ajustes numa tela só, separados por assunto. Desinstalador: programas e aplicativos do Windows em abas.",
        "Programas: instalar e atualizar na mesma tela.",
        "Sobre: versão, proteções e créditos; \"Verificar atualização\" no rodapé do menu.",
    };

    public static void Show(Window? owner)
    {
        var list = new StackPanel { Margin = new Thickness(22, 16, 22, 10) };
        foreach (var item in Items)
            list.Children.Add(new TextBlock { Text = "•  " + item, TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 0, 0, 8), Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextBrush") });
        var head = new Border
        {
            Background = (System.Windows.Media.Brush)Application.Current.FindResource("ButtonBrush"), Padding = new Thickness(22, 16, 22, 14),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "O que há de novo", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.Black },
                    new TextBlock { Text = $"Você está na versão {AppInfo.Version}, compilação {AppUpdates.CurrentBuild()}.", Foreground = System.Windows.Media.Brushes.Black },
                },
            },
        };
        var close = new Button { Content = "Fechar", Style = (Style)Application.Current.FindResource("PrimaryButton"), Padding = new Thickness(22, 7, 22, 7), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(22, 0, 22, 16) };
        var root = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(head);
        root.Children.Add(close);
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var window = new Window
        {
            Title = "O que há de novo", Width = 560, Height = 470, Owner = owner, WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Background = (System.Windows.Media.Brush)Application.Current.FindResource("SurfaceBrush"), ResizeMode = ResizeMode.NoResize, Content = root,
        };
        close.Click += (_, _) => window.Close();
        window.ShowDialog();
    }

    /// <summary>Mostra uma vez por compilação nova (não na primeira instalação).</summary>
    public static void ShowOnceAfterUpdate(Window owner)
    {
        string build = AppUpdates.CurrentBuild().ToString();
        string seen = AppServices.Settings.LastWhatsNewBuild;
        AppServices.Settings.LastWhatsNewBuild = build;
        AppServices.Settings.Save();
        if (seen.Length > 0 && seen != build) Show(owner);
    }
}
