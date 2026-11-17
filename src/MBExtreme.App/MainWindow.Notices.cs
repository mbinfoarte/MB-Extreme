using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MBExtreme.Core.Notices;

namespace MBExtreme.UI;

/// <summary>Sino de avisos no alto da janela: lista dos últimos avisos, marcar como lidos e escolher quais tipos receber.</summary>
public partial class MainWindow
{
    private readonly TextBlock _bellBadge = new()
    {
        FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Brushes.Black, Padding = new Thickness(4, 0, 4, 0),
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 6, 0), IsHitTestVisible = false,
    };
    private readonly Border _bellBadgeBox = new() { CornerRadius = new CornerRadius(7), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 6, 0), IsHitTestVisible = false };
    private Popup? _bellPopup;
    private string? _lastNoticePage;

    private void SetupNotices()
    {
        var bell = new Button { Style = (Style)FindResource("CaptionButton"), Content = "", ToolTip = "Avisos do MB Extreme" };
        _bellBadgeBox.Background = (Brush)FindResource("AccentBrush");
        _bellBadgeBox.Child = _bellBadge;
        var host = new Grid();
        host.Children.Add(bell);
        host.Children.Add(_bellBadgeBox);
        CaptionButtons.Children.Insert(0, host);
        bell.Click += (_, _) => ShowNotices(bell);
        AppServices.Notices.Changed += () => Dispatcher.BeginInvoke(new Action(PaintBell));
        AppServices.Notices.Added += notice => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!AppServices.Settings.NoticesToast || _tray is null) return;
            _lastNoticePage = notice.PageKey;
            _tray.ShowBalloon(notice.Title, notice.Text);
        }));
        if (_tray is not null) _tray.BalloonClicked = () => { ShowFromTray(); if (_lastNoticePage is { } key) Go(key); };
        PaintBell();
    }

    private void PaintBell()
    {
        int n = AppServices.Notices.Unread;
        _bellBadge.Text = n > 9 ? "9+" : n.ToString();
        _bellBadgeBox.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowNotices(FrameworkElement anchor)
    {
        if (_bellPopup is { IsOpen: true }) { _bellPopup.IsOpen = false; return; }
        var panel = new StackPanel { Width = 360 };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var read = new Button { Content = "Marcar como lidos", Style = (Style)FindResource("GhostButton"), Padding = new Thickness(8, 2, 8, 2), FontSize = 11.5 };
        read.Click += (_, _) => { AppServices.Notices.MarkAllRead(); _bellPopup!.IsOpen = false; };
        DockPanel.SetDock(read, Dock.Right);
        head.Children.Add(read);
        head.Children.Add(new TextBlock { Text = "Avisos", FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(head);

        var items = AppServices.Notices.Items;
        var list = new StackPanel();
        if (items.Count == 0) list.Children.Add(new TextBlock { Text = "Nenhum aviso por enquanto.", Foreground = (Brush)FindResource("MutedBrush"), FontSize = 12, Margin = new Thickness(0, 4, 0, 8) });
        foreach (var notice in items.Take(20))
        {
            var n = notice;
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
            row.Children.Add(new TextBlock { Text = (n.Read ? "" : "● ") + n.Title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource(n.Read ? "TextBrush" : "AccentBrush") });
            row.Children.Add(new TextBlock { Text = n.Text, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("MutedBrush") });
            row.Children.Add(new TextBlock { Text = n.At.ToString("dd/MM HH:mm") + " · " + NoticeCenter.Label(n.Kind), FontSize = 10.5, Foreground = (Brush)FindResource("MutedBrush"), Opacity = 0.8 });
            var card = new Border { Padding = new Thickness(10, 7, 10, 7), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 6), Background = (Brush)FindResource("SurfaceBrush"), Child = row, Cursor = n.PageKey is null ? null : System.Windows.Input.Cursors.Hand };
            if (n.PageKey is { } key) card.MouseLeftButtonUp += (_, _) => { _bellPopup!.IsOpen = false; Go(key); };
            list.Children.Add(card);
        }
        panel.Children.Add(new ScrollViewer { MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list });

        // preferências: quais tipos receber e se aparecem também no canto da tela
        panel.Children.Add(new TextBlock { Text = "RECEBER", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("MutedBrush"), Margin = new Thickness(0, 8, 0, 4) });
        var prefs = new UniformGrid { Columns = 2 };
        foreach (NoticeKind kind in Enum.GetValues(typeof(NoticeKind)))
        {
            var k = kind;
            var box = new CheckBox { Content = NoticeCenter.Label(kind), FontSize = 11.5, Foreground = (Brush)FindResource("TextBrush"), Margin = new Thickness(0, 2, 6, 2), IsChecked = !AppServices.Settings.NoticesOff.Contains(kind.ToString()) };
            box.Click += (_, _) =>
            {
                AppServices.Settings.NoticesOff.Remove(k.ToString());
                if (box.IsChecked != true) AppServices.Settings.NoticesOff.Add(k.ToString());
                AppServices.Settings.Save();
            };
            prefs.Children.Add(box);
        }
        panel.Children.Add(prefs);
        var toast = new CheckBox { Content = "Mostrar também no canto da tela (aviso do Windows)", FontSize = 11.5, Foreground = (Brush)FindResource("TextBrush"), Margin = new Thickness(0, 6, 0, 0), IsChecked = AppServices.Settings.NoticesToast };
        toast.Click += (_, _) => { AppServices.Settings.NoticesToast = toast.IsChecked == true; AppServices.Settings.Save(); };
        panel.Children.Add(toast);

        _bellPopup = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade,
            Child = new Border
            {
                Background = (Brush)FindResource("Surface2Brush"), BorderBrush = (Brush)FindResource("BorderBrush"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(14), Child = panel, Margin = new Thickness(0, 4, 0, 0),
            },
        };
        _bellPopup.IsOpen = true;
    }
}

public partial class MainWindow
{
    /// <summary>
    /// Ao ligar o PC (MB aberto pelo Windows, já com conta): um aviso discreto por dia, lembrando que o MB está cuidando do PC;
    /// e o aviso de que o teste grátis acaba em menos de um dia.
    /// </summary>
    private void StartupNotice()
    {
        var s = AppServices.Settings;
        var status = AppServices.License.Status;
        if (status.Payload is { Status: "trialing", PeriodEnd: { } end } && end - DateTimeOffset.Now < TimeSpan.FromDays(1) && end > DateTimeOffset.Now)
            AppServices.Notices.Add(new Notice("trial-end-" + end.ToString("yyyyMMdd"), NoticeKind.Trial, "Seu teste grátis acaba hoje",
                $"Os recursos do plano Gamer ficam liberados até {end.LocalDateTime:HH:mm}. Assine para continuar.", DateTime.Now, PageKey: "license"));

        if (!App.StartInTray || status.Payload is null) return;
        if (s.LastStartupNotice is { } last && last.Date == DateTime.Today) return;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            s.LastStartupNotice = DateTime.Now;
            s.Save();
            string plan = FeaturePolicyTitle(status.Plan);
            AppServices.Notices.Add(new Notice("startup-" + DateTime.Today.ToString("yyyyMMdd"), NoticeKind.Health, "MB Extreme ativo",
                $"Plano {plan}. Clique para ver a análise do seu PC e o que dá para melhorar hoje.", DateTime.Now, PageKey: "home"));
        };
        timer.Start();
    }

    private static string FeaturePolicyTitle(string? plan) => MBExtreme.Core.Licensing.FeaturePolicy.Title(plan);
}
