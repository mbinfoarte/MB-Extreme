using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MBExtreme.Core.Licensing;
using MBExtreme.Core.Notices;

namespace MBExtreme.UI;

/// <summary>
/// Faixa da Black Friday no rodapé da janela (mesmas datas do site): texto, contagem regressiva e "Aproveitar" (abre os planos no site).
/// Só para quem está no grátis, no teste ou no plano mensal. Fechar no ✕ esconde até a próxima etapa (a das 48 horas volta a aparecer).
/// Também põe um aviso no sino (e no Windows, se ligado) uma vez por etapa. Fora das datas não aparece nada.
/// </summary>
internal sealed class BlackFridayStrip
{
    private const double Height = 46;
    private readonly ContentControl _content;
    private readonly Border _bar;
    private readonly TextBlock _title = new() { FontWeight = FontWeights.ExtraBold, FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _text = new() { FontSize = 13, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly TextBlock _left = new() { FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0xB4, 0xB4, 0xBA)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private PromoStep? _step;
    private int _checks;
    private static string DismissFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "black-friday-fechada.txt");

    public BlackFridayStrip(Grid host, ContentControl content)
    {
        _content = content;
        var yellow = Color.FromRgb(0xFF, 0xC4, 0x00);
        var go = new Button
        {
            Content = "Aproveitar", Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(16, 0, 0, 0), FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(yellow), Foreground = Brushes.Black, BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand,
        };
        go.Click += (_, _) => Open(MBExtreme.Core.AppInfo.SiteUrl + BlackFriday.SitePath);
        var close = new Button
        {
            Content = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(10, 0, 0, 0),
            Background = Brushes.Transparent, Foreground = Brushes.Gray, BorderThickness = new Thickness(0), ToolTip = "Fechar até a próxima etapa", Cursor = System.Windows.Input.Cursors.Hand,
        };
        close.Click += (_, _) => Dismiss();
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, Foreground = new SolidColorBrush(yellow), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        row.Children.Add(_title);
        row.Children.Add(_text);
        row.Children.Add(_left);
        row.Children.Add(go);
        row.Children.Add(close);
        _bar = new Border
        {
            Height = Height, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0x0B, 0x0B, 0x0D)), BorderBrush = new SolidColorBrush(yellow), BorderThickness = new Thickness(1.5),
            Child = row, Visibility = Visibility.Collapsed,
        };
        Grid.SetColumn(_bar, 1);
        Panel.SetZIndex(_bar, 5);
        host.Children.Add(_bar);
        _tick.Tick += (_, _) => Tick();
        _tick.Start();
        Tick();
    }

    private void Tick()
    {
        var now = DateTimeOffset.Now;
        if (_checks++ % 30 == 0) Evaluate(now);   // a cada 30 s: entra, troca de etapa e sai sozinha
        if (_step is not null) _left.Text = "acaba em " + BlackFriday.Countdown(_step, now);
    }

    private void Evaluate(DateTimeOffset now)
    {
        var step = BlackFriday.Active(now);
        var license = AppServices.License;
        var status = license.Status;
        bool paid = LicenseExpiry.IsPro(status) && status.Payload?.Status != "trialing" && !license.Trial.Active;
        bool eligible = step is not null && !license.IsVisitor && BlackFriday.ShouldShow(paid, status.Payload?.PeriodEnd, status.NoEndDate, now);
        if (eligible)
            AppServices.Notices.Add(new Notice(BlackFriday.NoticeId(step!), NoticeKind.News, $"{step!.Label}: {step.Percent}% OFF",
                $"{step.Percent}% de desconto no plano anual do MB Extreme (1º ano). Clique em Aproveitar na faixa da janela ou veja os planos no site.", DateTime.Now, PageKey: "license"));
        _step = eligible && !Dismissed(step!) ? step : null;
        if (_step is not null)
        {
            _title.Text = _step.Label.ToUpperInvariant();
            _text.Text = $"{_step.Percent}% OFF no plano anual · 1º ano";
        }
        SetVisible(_step is not null);
    }

    private void SetVisible(bool on)
    {
        if ((_bar.Visibility == Visibility.Visible) == on) return;
        _bar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        var m = _content.Margin;
        _content.Margin = new Thickness(m.Left, m.Top, m.Right, on ? Height + 12 : 0);
    }

    private static bool Dismissed(PromoStep step)
    {
        try { return File.Exists(DismissFile) && File.ReadAllText(DismissFile).Trim() == BlackFriday.NoticeId(step); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private void Dismiss()
    {
        if (_step is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DismissFile)!);
            File.WriteAllText(DismissFile, BlackFriday.NoticeId(_step));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        _step = null;
        SetVisible(false);
    }

    private static void Open(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
