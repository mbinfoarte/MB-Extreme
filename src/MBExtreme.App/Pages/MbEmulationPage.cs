using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Gamepads;

namespace MBExtreme.UI.Pages;

/// <summary>
/// MB Control · Emulação de controle: assistente que verifica o PC (drivers do controle virtual, HidHide, FakerInput),
/// ajuda a conectar o controle e mostra o que o motor de emulação vai fazer.
/// </summary>
internal sealed class MbEmulationPage : MbControlPageBase
{
    private readonly StackPanel _parts = new();
    private readonly TextBlock _summary = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };

    public MbEmulationPage() : base("Emulação de controle", "Faz os jogos enxergarem o seu controle PS4, PS5 ou Xbox como um controle nativo do Windows. Primeiro preparamos o PC; só instalamos o que for preciso, pelos sites oficiais.")
    {
        var step = new StackPanel();
        step.Children.Add(new TextBlock { Text = "PASSO 1 · PREPARAR O PC", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)) });
        step.Children.Add(_summary);
        step.Children.Add(_parts);
        var recheck = PadUi.Button("Verificar de novo");
        recheck.HorizontalAlignment = HorizontalAlignment.Left;
        recheck.Margin = new Thickness(0, 6, 0, 0);
        recheck.Click += (_, _) => Check();
        step.Children.Add(recheck);
        Body.Children.Add(PadUi.Card(step));

        var connect = new StackPanel();
        connect.Children.Add(new TextBlock { Text = "PASSO 2 · CONECTAR O CONTROLE", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)), Margin = new Thickness(0, 0, 0, 8) });
        connect.Children.Add(Line("Pelo cabo USB", "Use um cabo que passa dados (não só carga): micro-USB no PS4, USB-C no PS5 e no Xbox Series."));
        connect.Children.Add(Line("Pelo Bluetooth", "Com o controle desligado, segure PS + Share (PS4) ou PS + Create (PS5) até a luz piscar rápido. No Xbox, segure o botão de parear. Depois adicione o controle no Bluetooth do Windows."));
        var bt = PadUi.Button("Abrir o Bluetooth do Windows");
        bt.HorizontalAlignment = HorizontalAlignment.Left;
        bt.Margin = new Thickness(0, 8, 0, 0);
        bt.Click += (_, _) => PadUi.Open("ms-settings:bluetooth");
        connect.Children.Add(bt);
        Body.Children.Add(PadUi.Card(connect));

        var next = new StackPanel();
        next.Children.Add(new TextBlock { Text = "PASSO 3 · ESCOLHER O QUE O JOGO VÊ", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)), Margin = new Thickness(0, 0, 0, 8) });
        var outs = new WrapPanel();
        foreach (var (name, desc) in new[] { ("Nativo", "O jogo vê o controle de verdade."), ("Xbox 360", "Funciona em quase todo jogo de PC."), ("PS4 (DualShock 4)", "Ícones de PlayStation e touchpad."), ("PS5 (DualSense)", "Depende da licença do driver; em análise.") })
        {
            var card = new StackPanel { Width = 210 };
            card.Children.Add(new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeights.Bold });
            card.Children.Add(PadUi.Muted(desc, 11.5));
            outs.Children.Add(new Border { Background = PadUi.Res("Surface2Brush"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 8, 8), Child = card, Opacity = 0.7 });
        }
        next.Children.Add(outs);
        next.Children.Add(PadUi.Muted("O motor de emulação (controle virtual, mapeamento, zonas mortas e giroscópio valendo dentro do jogo) é a próxima etapa do MB Control. Os drivers verificados acima já deixam o PC pronto para ele.", 12));
        Body.Children.Add(PadUi.Card(next));
        Check();
    }

    private static UIElement Line(string title, string text)
    {
        var st = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        st.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold });
        st.Children.Add(PadUi.Muted(text, 12.5));
        return st;
    }

    private void Check()
    {
        var list = EmulationDrivers.CheckWindows();
        _summary.Text = EmulationDrivers.Ready(list) ? "Tudo pronto para o controle virtual." : "Falta instalar o controle virtual para emular.";
        _summary.Foreground = PadUi.Res(EmulationDrivers.Ready(list) ? "OkBrush" : "WarnBrush");
        _parts.Children.Clear();
        foreach (var c in list)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var dot = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 5, 12, 0), VerticalAlignment = VerticalAlignment.Top,
                Background = PadUi.Res(c.Installed ? "OkBrush" : c.Need == "opcional" ? "BorderBrush" : "WarnBrush") };
            grid.Children.Add(dot);
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = $"{c.Title} · {c.Need}", FontSize = 14, FontWeight = FontWeights.SemiBold });
            text.Children.Add(PadUi.Muted(c.Purpose, 12));
            text.Children.Add(new TextBlock { Text = c.Installed ? "Instalado" : c.Need == "opcional" ? "Não instalado · não é necessário para o controle" : "Falta instalar", FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = PadUi.Res(c.Installed ? "OkBrush" : c.Need == "opcional" ? "MutedBrush" : "WarnBrush") });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var b = PadUi.Button(c.Installed ? "Reinstalar / atualizar" : "Baixar do site oficial", primary: !c.Installed && c.Need != "opcional");
            b.VerticalAlignment = VerticalAlignment.Center;
            b.Margin = new Thickness(10, 0, 0, 0);
            string url = c.Url;
            b.Click += (_, _) => PadUi.Open(url);
            Grid.SetColumn(b, 2);
            grid.Children.Add(b);
            _parts.Children.Add(new Border { Background = PadUi.Res("Surface2Brush"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 8), Child = grid });
        }
    }
}
