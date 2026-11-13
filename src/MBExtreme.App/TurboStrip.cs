using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MBExtreme.UI;

/// <summary>Faixa "Turbo" no alto da Limpeza: liga a limpeza automática da memória quando o uso passa de 70%.</summary>
internal static class TurboStrip
{
    public const int TurboThreshold = 70;

    public static UIElement Build()
    {
        var auto = AppServices.AutoMemory;
        var status = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        var toggle = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        System.Windows.Automation.AutomationProperties.SetName(toggle, "Turbo: limpeza automática da memória");

        void Show()
        {
            bool on = auto.Enabled;
            toggle.IsChecked = on;
            status.Foreground = (Brush)Application.Current.FindResource(on ? "OkBrush" : "MutedBrush");
            status.Text = on
                ? $"Ligado: quando a memória passar de {AppServices.Settings.MemoryAutoCleanThreshold}% de uso, o MB libera sozinho a memória dos programas em segundo plano (sem mexer no programa aberto nem nos jogos)."
                : $"Desligado. Ligue para o MB limpar a memória sozinho sempre que o uso passar de {TurboThreshold}%, sem deixar o PC sobrecarregar.";
        }

        toggle.Click += (_, _) =>
        {
            if (toggle.IsChecked == true) auto.SetThreshold(TurboThreshold);
            auto.SetEnabled(toggle.IsChecked == true);
            Show();
        };
        auto.Changed += (_, _) => Application.Current.Dispatcher.BeginInvoke(new Action(Show));

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = "⚡ TURBO · limpeza automática", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)) });
        text.Children.Add(status);

        var row = new DockPanel();
        DockPanel.SetDock(toggle, Dock.Left);
        row.Children.Add(toggle);
        row.Children.Add(text);
        Show();

        return new Border
        {
            Margin = new Thickness(40, 18, 40, 0), Padding = new Thickness(16, 10, 16, 10), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)), Background = (Brush)Application.Current.FindResource("Surface2Brush"),
            Child = row,
        };
    }
}
