using System.Windows;

namespace MBExtreme.UI.Pages;

/// <summary>Tamanho da interface e como usar o programa pelo teclado e com o Narrador.</summary>
public sealed class AccessibilityPage : ToolPageBase
{
    public AccessibilityPage() : base("Acessibilidade e tamanho",
        "Deixe a interface maior ou menor e veja como usar o programa só pelo teclado ou com o Narrador do Windows.")
    {
        Body.Children.Add(Heading("Tamanho da interface"));
        int current = Math.Max(0, Array.FindIndex(Accessibility.Scales, s => Math.Abs(s - AppServices.Settings.UiScale) < 0.01));
        Body.Children.Add(Choice(Accessibility.Scales.Select(s => $"{s * 100:0}%").ToList(), current, i =>
        {
            MainWindow.Current?.SetScale(Accessibility.Scales[i]);
            SetStatus($"Interface em {Accessibility.Scales[i] * 100:0}%.", "OkBrush");
        }));
        Body.Children.Add(Muted("Atalhos: Ctrl + aumenta, Ctrl − diminui e Ctrl 0 volta ao normal. Em telas pequenas, 90% mostra mais coisas de uma vez.", 12));

        Body.Children.Add(Heading("Animações"));
        var animations = new System.Windows.Controls.CheckBox
        {
            Content = "Mostrar animações (telas entrando, botões, anel do Painel, seções do menu)",
            IsChecked = AppServices.Settings.Animations, Foreground = Brush("TextBrush"), Margin = new Thickness(0, 4, 0, 4),
        };
        animations.Click += (_, _) =>
        {
            AppServices.Settings.Animations = animations.IsChecked == true;
            AppServices.Settings.Save();
            SetStatus(animations.IsChecked == true
                ? "Animações ligadas. O efeito dos botões ao passar o mouse volta quando o programa abrir de novo."
                : "Animações desligadas: tudo aparece na hora. O efeito dos botões some quando o programa abrir de novo.", "OkBrush");
        };
        Body.Children.Add(animations);
        Body.Children.Add(Muted("Se o Windows estiver com \"Efeitos de animação\" desligado (Configurações → Acessibilidade → Efeitos visuais), o MB Extreme também não anima.", 12));

        Body.Children.Add(Heading("Efeito cristal"));
        var glass = new System.Windows.Controls.CheckBox
        {
            Content = "Fundo de vidro (o que está atrás da janela aparece desfocado, como no Aero do Windows Vista)",
            IsChecked = AppServices.Settings.GlassEffect, Foreground = Brush("TextBrush"), Margin = new Thickness(0, 4, 0, 4),
        };
        glass.Click += (_, _) =>
        {
            AppServices.Settings.GlassEffect = glass.IsChecked == true;
            AppServices.Settings.Save();
            MainWindow.Current?.SetGlass(glass.IsChecked == true);
            SetStatus(glass.IsChecked != true ? "Efeito cristal desligado: fundo sólido." :
                GlassEffect.SystemAllows() ? "Efeito cristal ligado." : "Ligado aqui, mas o Windows está com \"Efeitos de transparência\" desligado (Configurações → Personalização → Cores).", "OkBrush");
        };
        Body.Children.Add(glass);
        Body.Children.Add(Muted("Se o PC for mais fraco ou a janela ficar lenta ao arrastar, desligue. Com \"Efeitos de transparência\" desligado no Windows, o fundo fica sólido.", 12));

        Body.Children.Add(Heading("Bandeja e widget"));
        var tray = new System.Windows.Controls.CheckBox
        {
            Content = "Ao clicar no X, esconder na bandeja (ao lado do relógio) em vez de fechar",
            IsChecked = AppServices.Settings.MinimizeToTray, Foreground = Brush("TextBrush"), Margin = new Thickness(0, 4, 0, 4),
        };
        tray.Click += (_, _) =>
        {
            AppServices.Settings.MinimizeToTray = tray.IsChecked == true;
            AppServices.Settings.Save();
            SetStatus(tray.IsChecked == true ? "O X vai esconder na bandeja. Use \"Sair\" no menu da bandeja para fechar de vez." : "O X fecha o programa.", "OkBrush");
        };
        Body.Children.Add(tray);
        var widget = new System.Windows.Controls.CheckBox
        {
            Content = "Mostrar o widget flutuante (barrinha com RAM, CPU, disco e rede na área de trabalho)",
            IsChecked = AppServices.Settings.WidgetEnabled, Foreground = Brush("TextBrush"), Margin = new Thickness(0, 4, 0, 4),
        };
        widget.Click += (_, _) =>
        {
            MainWindow.Current?.SetWidget(widget.IsChecked == true);
            SetStatus(widget.IsChecked == true ? "Widget ligado: arraste para onde quiser. Para esconder, clique no X do widget ou aqui." : "Widget escondido.", "OkBrush");
        };
        Body.Children.Add(widget);
        Body.Children.Add(Muted("O widget fica sempre no topo e mostra o uso em tempo real. Clique no foguete e arraste para mover; o botão de abrir leva ao programa.", 12));
        Body.Children.Add(Muted("Um clique no ícone do MB Extreme ao lado do relógio abre o Painel MB (temperaturas, RAM, ping e Modo jogo); dois cliques abrem o programa.", 12));
        var shell = new System.Windows.Controls.CheckBox
        {
            Content = "Mostrar \"MB Extreme\" no botão direito do Windows (área de trabalho, pastas e programas)",
            IsChecked = AppServices.Settings.ShellMenuEnabled, Foreground = Brush("TextBrush"), Margin = new Thickness(0, 8, 0, 4),
        };
        shell.Click += (_, _) =>
        {
            bool on = shell.IsChecked == true;
            try
            {
                if (on && Environment.ProcessPath is { } exe) MBExtreme.Core.Windows.ShellActions.Install(exe);
                else MBExtreme.Core.Windows.ShellActions.Uninstall();
                AppServices.Settings.ShellMenuEnabled = on;
                AppServices.Settings.Save();
                SetStatus(on ? "Pronto: clique com o botão direito na área de trabalho (no Windows 11, em \"Mostrar mais opções\")." : "Itens do MB Extreme removidos do botão direito.", "OkBrush");
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
            {
                shell.IsChecked = !on;
                SetStatus("Não foi possível mudar: " + ex.Message, "BadBrush");
            }
        };
        Body.Children.Add(shell);

        Body.Children.Add(Heading("Pelo teclado"));
        Body.Children.Add(Row("Tab e Shift+Tab", "Andam entre os botões e caixas. O item com o foco ganha um contorno amarelo."));
        Body.Children.Add(Row("Espaço ou Enter", "Apertam o botão ou marcam a caixa com o foco."));
        Body.Children.Add(Row("Alt + seta para a esquerda", "Volta para a tela anterior (o botão lateral do mouse faz o mesmo)."));

        Body.Children.Add(Heading("Narrador e leitores de tela"));
        Body.Children.Add(Muted("Todos os botões, caixas de marcar e campos têm nome para o Narrador do Windows (Ctrl + Windows + Enter liga e desliga). " +
                                "Botões que só mostram um ícone usam a dica que aparece ao passar o mouse.", 12));
    }
}
