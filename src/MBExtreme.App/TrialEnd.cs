using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Licensing;

namespace MBExtreme.UI;

/// <summary>
/// "Seu teste do Pro acabou": aparece uma vez quando os 7 dias terminam (com a licença aplicada e sem assinatura).
/// Duas escolhas: Assinar o Pro (abre os planos) ou Continuar no Grátis (os ajustes Pro voltam ao original pela Central Desfazer).
/// </summary>
internal static class TrialEnd
{
    public static void ShowIfNeeded(Window owner)
    {
        var license = AppServices.License;
        license.RefreshTrial();
        if (!license.Config.Enforce || LicenseExpiry.IsPro(license.Status) || license.Trial.Active || AppServices.Settings.TrialEndShown) return;
        AppServices.Settings.TrialEndShown = true;
        AppServices.Settings.Save();

        var root = new StackPanel { Margin = new Thickness(28, 24, 28, 22) };
        root.Children.Add(new TextBlock { Text = "⏳", FontSize = 40, HorizontalAlignment = HorizontalAlignment.Center });
        root.Children.Add(new TextBlock { Text = "Seu teste do Pro acabou", FontSize = 22, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 6), Foreground = Brushes.White });
        root.Children.Add(new TextBlock
        {
            Text = $"Você usou os {MBExtreme.Core.AppInfo.TrialDays} dias do MB Extreme Pro. Escolha como quer continuar:", TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(0xB4, 0xB4, 0xBC)), Margin = new Thickness(0, 0, 0, 16),
        });
        var choices = new Grid();
        choices.ColumnDefinitions.Add(new ColumnDefinition());
        choices.ColumnDefinitions.Add(new ColumnDefinition());
        var window = new Window
        {
            Title = "MB Extreme", Width = 600, SizeToContent = SizeToContent.Height, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize, Background = (Brush)Application.Current.FindResource("SurfaceBrush"), Content = root,
        };
        choices.Children.Add(Choice("⭐ Assinar o Pro", "Tudo liberado: otimizações de desempenho e jogos, benchmark, Modo Jogo, gravação e live, e mais.", true, () =>
        {
            window.Close();
            string server = license.Config.ServerUrl;
            if (server.StartsWith("https://", StringComparison.Ordinal))
                try { Process.Start(new ProcessStartInfo(server + "/planos") { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { }
            MainWindow.Current?.Go("license");
        }));
        var free = Choice("Continuar no Grátis", "Painel, limpeza, programas, discos, rede, periféricos, suporte e mais. Os ajustes do Pro voltam ao original.", false, () =>
        {
            window.Close();
            _ = AppServices.ProLock.CheckAsync();   // desfaz os ajustes Pro (Central Desfazer)
        });
        Grid.SetColumn(free, 1);
        choices.Children.Add(free);
        root.Children.Add(choices);
        root.Children.Add(new TextBlock { Text = "Você pode assinar quando quiser em Licença e conta.", FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x96)) });
        window.ShowDialog();
    }

    private static Button Choice(string title, string text, bool primary, Action click)
    {
        var content = new StackPanel { Width = 230 };
        content.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Opacity = 0.85 });
        var b = new Button { Content = content, Style = (Style)Application.Current.FindResource(primary ? "PrimaryButton" : "GhostButton"), Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(6), HorizontalContentAlignment = HorizontalAlignment.Left };
        b.Click += (_, _) => click();
        return b;
    }
}
