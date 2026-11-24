using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MBExtreme.UI;

/// <summary>
/// Faixa "MODO ATENDIMENTO TÉCNICO" no rodapé da janela enquanto há um atendimento aberto neste PC: a empresa, o PC, a hora de início,
/// quantas coisas já foram anotadas e o botão Finalizar (abre a tela de Atendimento técnico).
/// </summary>
internal sealed class ServiceStrip
{
    private const double Height = 46;
    private readonly ContentControl _content;
    private readonly Border _bar;
    private readonly TextBlock _text = new() { FontSize = 13, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };

    public ServiceStrip(Grid host, ContentControl content)
    {
        _content = content;
        var yellow = Color.FromRgb(0xFF, 0xC4, 0x00);
        var finish = new Button
        {
            Content = "Finalizar atendimento", Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(16, 0, 0, 0), FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(yellow), Foreground = Brushes.Black, BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand,
        };
        finish.Click += (_, _) => MainWindow.Current?.Go("service");
        var row = new DockPanel { Margin = new Thickness(16, 0, 10, 0) };
        DockPanel.SetDock(finish, Dock.Right);
        row.Children.Add(finish);
        row.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(yellow), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = "MODO ATENDIMENTO TÉCNICO", FontWeight = FontWeights.ExtraBold, FontSize = 13, Foreground = new SolidColorBrush(yellow), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
        row.Children.Add(_text);
        _bar = new Border
        {
            Height = Height, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x2A, 0x24, 0x10)), BorderBrush = new SolidColorBrush(yellow), BorderThickness = new Thickness(1.5),
            Child = row, Visibility = Visibility.Collapsed,
        };
        Grid.SetColumn(_bar, 1);
        Panel.SetZIndex(_bar, 7);
        host.Children.Add(_bar);
        ServiceMode.Changed += () => _bar.Dispatcher.BeginInvoke(new Action(Update));
        Update();
    }

    private void Update()
    {
        var v = ServiceMode.Active;
        bool on = v is not null;
        if (v is not null)
        {
            int n = v.ReportItems().Count;
            _text.Text = $"{v.CompanyName} · {v.PcName} · desde {v.StartedAt:HH:mm} · {(n == 1 ? "1 item anotado" : $"{n} itens anotados")}";
        }
        if ((_bar.Visibility == Visibility.Visible) == on) return;
        _bar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        var m = _content.Margin;
        _content.Margin = new Thickness(m.Left, m.Top, m.Right, on ? Height + 12 : 0);
    }
}
