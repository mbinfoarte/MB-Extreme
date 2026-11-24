using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MBExtreme.UI;

/// <summary>
/// Faixa do modo visitante no rodapé da janela (quem pulou o cadastro): "Modo visitante · Crie sua conta grátis e ganhe 3 dias do
/// plano Gamer", com Criar conta grátis e Entrar. Some sozinha quando a conta entra.
/// </summary>
internal sealed class VisitorStrip
{
    private const double Height = 46;
    private readonly ContentControl _content;
    private readonly Border _bar;

    public VisitorStrip(Grid host, ContentControl content)
    {
        _content = content;
        var yellow = Color.FromRgb(0xFF, 0xC4, 0x00);
        var create = new Button
        {
            Content = "Criar conta grátis", Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(16, 0, 0, 0), FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(yellow), Foreground = Brushes.Black, BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand,
        };
        create.Click += (_, _) => SignIn(signUp: true);
        var enter = new Button
        {
            Content = "Entrar", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(8, 0, 0, 0), FontWeight = FontWeights.SemiBold,
            Background = Brushes.Transparent, Foreground = new SolidColorBrush(yellow), BorderBrush = new SolidColorBrush(yellow), BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        enter.Click += (_, _) => SignIn(signUp: false);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, Foreground = new SolidColorBrush(yellow), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        row.Children.Add(new TextBlock { Text = "MODO VISITANTE", FontWeight = FontWeights.ExtraBold, FontSize = 13, Foreground = new SolidColorBrush(yellow), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = "Crie sua conta grátis e ganhe 3 dias do plano Gamer", FontSize = 13, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
        row.Children.Add(create);
        row.Children.Add(enter);
        _bar = new Border
        {
            Height = Height, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x0B, 0x0B, 0x0D)), BorderBrush = new SolidColorBrush(yellow), BorderThickness = new Thickness(1.5),
            Child = row, Visibility = Visibility.Collapsed,
        };
        Grid.SetColumn(_bar, 1);
        Panel.SetZIndex(_bar, 6);
        host.Children.Add(_bar);
        AppServices.License.Changed += (_, _) => _bar.Dispatcher.BeginInvoke(new Action(Update));
        Update();
    }

    /// <summary>Abre a janela de conta (criar ou entrar). A janela principal se atualiza sozinha quando a licença muda.</summary>
    public static void SignIn(bool signUp) => Login.LoginWindow.Prompt(Application.Current.MainWindow, signUp);

    private void Update()
    {
        bool on = AppServices.License.IsVisitor;
        if ((_bar.Visibility == Visibility.Visible) == on) return;
        _bar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        var m = _content.Margin;
        _content.Margin = new Thickness(m.Left, m.Top, m.Right, on ? Height + 12 : 0);
    }
}
