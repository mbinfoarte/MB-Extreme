using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MBExtreme.Core.Social;

namespace MBExtreme.UI.Pages;

/// <summary>Nossas redes: o canal da MB InfoArt, as redes sociais, grupos de ofertas e parceiros com cupom (tudo do arquivo online).</summary>
public sealed class SocialPage : UserControl
{
    private readonly StackPanel _root = new() { Margin = new Thickness(40, 28, 40, 32), MaxWidth = 980 };

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    public SocialPage()
    {
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _root };
        Build(AppServices.Channel);
        Loaded += async (_, _) =>
        {
            var fresh = await MBExtreme.Core.Social.ChannelConfig.RefreshAsync(AppServices.Web);
            if (fresh != AppServices.Channel) { AppServices.Channel = fresh; Build(fresh); }
        };
    }

    private static void Open(string url)
    {
        if (!ChannelConfig.IsHttps(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static (string Glyph, Color Color) IconOf(string icon) => icon.ToLowerInvariant() switch
    {
        "youtube" => ("▶", Color.FromRgb(0xFF, 0x30, 0x30)),
        "instagram" => ("◎", Color.FromRgb(0xE1, 0x30, 0x6C)),
        "tiktok" => ("♪", Color.FromRgb(0x25, 0xF4, 0xEE)),
        "facebook" => ("f", Color.FromRgb(0x18, 0x77, 0xF2)),
        "threads" => ("@", Colors.White),
        "whatsapp" => ("✆", Color.FromRgb(0x25, 0xD3, 0x66)),
        "telegram" => ("➤", Color.FromRgb(0x29, 0xA9, 0xEB)),
        _ => ("🌐", Color.FromRgb(0xFF, 0xC4, 0x00)),
    };

    /// <summary>Ícone da rede: o desenho da marca (Simple Icons) na cor dela, num quadrado com a cor clarinha; sem desenho, o símbolo.</summary>
    private static FrameworkElement BrandTile(string icon, double size)
    {
        var (glyph, color) = IconOf(icon);
        FrameworkElement inner = new TextBlock { Text = glyph, FontSize = size * 0.42, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (SocialIcons.Known.TryGetValue(icon, out var brand))
        {
            color = (Color)ColorConverter.ConvertFromString(brand.Color);
            if (0.299 * color.R + 0.587 * color.G + 0.114 * color.B < 60) color = Colors.White;   // marcas pretas (TikTok, Threads) em branco
            try
            {
                var shape = Geometry.Parse("F1 " + brand.Path);
                inner = new System.Windows.Shapes.Path { Data = shape, Fill = new SolidColorBrush(color), Stretch = Stretch.Uniform, Width = size * 0.5, Height = size * 0.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            }
            catch (FormatException) { }
        }
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.26), Background = new SolidColorBrush(Color.FromArgb(0x26, color.R, color.G, color.B)),
            HorizontalAlignment = HorizontalAlignment.Center, Child = inner,
        };
    }

    private void Build(ChannelConfig c)
    {
        _root.Children.Clear();

        // topo: o canal
        var hero = new Grid();
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hero.ColumnDefinitions.Add(new ColumnDefinition());
        var logo = new Border
        {
            Width = 104, Height = 104, CornerRadius = new CornerRadius(52), BorderBrush = Res("ButtonBrush"), BorderThickness = new Thickness(3),
            Background = new ImageBrush(new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/logo.png"))) { Stretch = Stretch.UniformToFill },
        };
        hero.Children.Add(logo);
        var text = new StackPanel { Margin = new Thickness(22, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xC4, 0x00)), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 8, 2),
            Child = new TextBlock { Text = "★ CANAL OFICIAL", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Res("ButtonBrush") },
        });
        text.Children.Add(new TextBlock { Text = c.Name, FontSize = 30, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 0) });
        text.Children.Add(new TextBlock { Text = c.Tagline, FontSize = 14, Foreground = Res("ButtonBrush") });
        text.Children.Add(new TextBlock { Text = "Se o MB Extreme te ajudou, apoie o canal: inscreva-se, ative o sininho e acompanhe as redes.", FontSize = 12.5, Foreground = Res("MutedBrush"), Margin = new Thickness(0, 6, 0, 10), TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Big("▶  Inscrever-se", "é grátis", Color.FromRgb(0xE5, 0x22, 0x22), Colors.White, () => Open(c.Subscribe)));
        buttons.Children.Add(Big("♥  Seja membro", "apoie e ganhe benefícios", Color.FromRgb(0xFF, 0xC4, 0x00), Color.FromRgb(0x0B, 0x0B, 0x0B), () => Open(c.Membership)));
        text.Children.Add(buttons);
        Grid.SetColumn(text, 1);
        hero.Children.Add(text);
        _root.Children.Add(Card(hero, new LinearGradientBrush(Color.FromRgb(0x1F, 0x1A, 0x08), Color.FromRgb(0x12, 0x12, 0x14), 0)));

        // redes
        _root.Children.Add(Title("Nossas redes", "Siga o canal onde você mais usa."));
        var grid = new UniformGrid { Columns = Math.Max(1, Math.Min(5, c.Links.Count)) };
        foreach (var link in c.Links)
        {
            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(BrandTile(link.Icon, 46));
            content.Children.Add(new TextBlock { Text = link.Name, FontSize = 13.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });
            content.Children.Add(new TextBlock { Text = link.Description, FontSize = 11, Foreground = Res("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
            var url = link.Url;
            var b = new Button { Style = (Style)Application.Current.FindResource("ToolTileButton"), Content = content, Margin = new Thickness(0, 0, 10, 10), ToolTip = url };
            System.Windows.Automation.AutomationProperties.SetName(b, "Abrir " + link.Name);
            b.Click += (_, _) => Open(url);
            grid.Children.Add(b);
        }
        _root.Children.Add(grid);
        Motion.Cascade(grid, 40);

        // ofertas
        if (c.Offers.Count > 0)
        {
            var offers = new StackPanel();
            offers.Children.Add(new TextBlock { Text = "⚡ Ofertas de tecnologia", FontSize = 16, FontWeight = FontWeights.SemiBold });
            offers.Children.Add(new TextBlock { Text = "Promoções de peças, periféricos e eletrônicos garimpadas pelo canal.", FontSize = 12, Foreground = Res("MutedBrush"), Margin = new Thickness(0, 2, 0, 10) });
            var row = new UniformGrid { Columns = Math.Min(2, c.Offers.Count) };
            foreach (var offer in c.Offers)
            {
                var g = new Grid { Margin = new Thickness(0, 0, 10, 10) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var tile = BrandTile(offer.Icon, 40);
                tile.Margin = new Thickness(0, 0, 12, 0);
                g.Children.Add(tile);
                var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                t.Children.Add(new TextBlock { Text = offer.Name, FontWeight = FontWeights.SemiBold });
                t.Children.Add(new TextBlock { Text = offer.Description, FontSize = 11.5, Foreground = Res("MutedBrush"), TextWrapping = TextWrapping.Wrap });
                Grid.SetColumn(t, 1);
                g.Children.Add(t);
                var url = offer.Url;
                var join = new Button { Content = "Entrar →", Style = (Style)Application.Current.FindResource("PrimaryButton"), Padding = new Thickness(14, 5, 14, 5), FontSize = 12 };
                join.Click += (_, _) => Open(url);
                Grid.SetColumn(join, 2);
                g.Children.Add(join);
                row.Children.Add(new Border { Background = Res("Surface2Brush"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 8, 8), Child = g });
            }
            offers.Children.Add(row);
            offers.Children.Add(new TextBlock { Text = "Alguns links podem gerar comissão para o canal, sem custo extra para você.", FontSize = 10.5, Foreground = Res("MutedBrush") });
            _root.Children.Add(Card(offers, new LinearGradientBrush(Color.FromRgb(0x24, 0x10, 0x14), Color.FromRgb(0x12, 0x12, 0x14), 0)));
        }

        // links de afiliados por grupo (lojas, games, IA): vêm do canal.json online
        if (c.Affiliates.Count > 0)
        {
            foreach (var (key, title, subtitle) in ChannelConfig.AffiliateGroups)
            {
                var items = c.AffiliatesIn(key);
                if (items.Count == 0) continue;
                _root.Children.Add(Title(title, subtitle));
                var row = new UniformGrid { Columns = Math.Max(1, Math.Min(3, items.Count)) };
                foreach (var a in items) row.Children.Add(AffiliateCard(a));
                _root.Children.Add(row);
            }
            _root.Children.Add(new TextBlock { Text = "Comprando ou assinando pelos links do canal você apoia a MB InfoArt, sem pagar nada a mais.", FontSize = 11, Foreground = Res("MutedBrush"), Margin = new Thickness(2, 0, 0, 6), TextWrapping = TextWrapping.Wrap });
        }

        // parceiros com cupom
        _root.Children.Add(Title("Parceiros em destaque", c.Partners.Count > 0 ? "Use o cupom do canal na hora de pagar." : "Em breve: parceiros com cupom de desconto do canal."));
        if (c.Partners.Count > 0)
        {
            var row = new UniformGrid { Columns = Math.Min(3, c.Partners.Count) };
            foreach (var p in c.Partners) row.Children.Add(PartnerCard(p));
            _root.Children.Add(row);
        }

        // ideia
        var idea = new Grid();
        idea.ColumnDefinitions.Add(new ColumnDefinition());
        idea.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var it = new StackPanel();
        it.Children.Add(new TextBlock { Text = "💡 Tem uma ideia para o MB Extreme?", FontSize = 15, FontWeight = FontWeights.SemiBold });
        it.Children.Add(new TextBlock { Text = "Sugira uma função, avise de um problema ou mande um elogio. Lemos tudo.", FontSize = 12, Foreground = Res("MutedBrush") });
        idea.Children.Add(it);
        var send = new Button { Content = "Enviar sugestão →", Style = (Style)Application.Current.FindResource("PrimaryButton"), Padding = new Thickness(16, 7, 16, 7), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        send.Click += (_, _) => MainWindow.Current?.Go("suggest");
        Grid.SetColumn(send, 1);
        idea.Children.Add(send);
        _root.Children.Add(Card(idea, Res("SurfaceBrush")));
    }

    private UIElement PartnerCard(Partner p)
    {
        var color = TryColor(p.Color);
        var panel = new StackPanel();
        panel.Children.Add(new Border
        {
            Height = 70, CornerRadius = new CornerRadius(10, 10, 0, 0), Background = new SolidColorBrush(Color.FromArgb(0x40, color.R, color.G, color.B)), Margin = new Thickness(-14, -12, -14, 10),
            Child = new TextBlock { Text = p.Name, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        panel.Children.Add(new TextBlock { Text = p.Description, FontSize = 12, Foreground = Res("MutedBrush"), TextWrapping = TextWrapping.Wrap });
        if (p.Discount.Length > 0)
            panel.Children.Add(new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left, BorderBrush = new SolidColorBrush(color), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 8, 0, 0),
                Child = new TextBlock { Text = "🏷 " + p.Discount, FontSize = 11.5, Foreground = new SolidColorBrush(color) },
            });
        if (p.Coupon.Length > 0)
        {
            var coupon = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            coupon.ColumnDefinitions.Add(new ColumnDefinition());
            coupon.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            coupon.Children.Add(new TextBlock { Text = p.Coupon, FontSize = 16, FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Consolas"), Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center });
            var copy = new Button { Content = "Copiar", Style = (Style)Application.Current.FindResource("GhostButton"), Padding = new Thickness(12, 4, 12, 4), FontSize = 12 };
            copy.Click += (_, _) => { try { Clipboard.SetText(p.Coupon); copy.Content = "Copiado ✔"; } catch (System.Runtime.InteropServices.COMException) { } };
            Grid.SetColumn(copy, 1);
            coupon.Children.Add(copy);
            panel.Children.Add(new Border { BorderBrush = new SolidColorBrush(color), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6, 6, 6), Margin = new Thickness(0, 10, 0, 0), Child = coupon });
        }
        var see = new Button { Content = "Ver oferta ↗", Style = (Style)Application.Current.FindResource("PrimaryButton"), Padding = new Thickness(14, 6, 14, 6), FontSize = 12.5, Margin = new Thickness(0, 10, 0, 0) };
        string url = p.Url;
        see.Click += (_, _) => Open(url);
        panel.Children.Add(see);
        return new Border { Background = Res("SurfaceBrush"), BorderBrush = Res("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 10, 10), Child = panel, ClipToBounds = true };
    }

    private UIElement AffiliateCard(AffiliateLink a)
    {
        var color = TryColor(a.Color);
        var panel = new DockPanel();
        var badge = new Border
        {
            Width = 44, Height = 44, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(0x33, color.R, color.G, color.B)),
            Child = new TextBlock { Text = a.Name.Length > 0 ? a.Name[..1].ToUpperInvariant() : "?", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        DockPanel.SetDock(badge, Dock.Left);
        panel.Children.Add(badge);
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = a.Name, FontSize = 14, FontWeight = FontWeights.SemiBold });
        if (a.Description.Length > 0) text.Children.Add(new TextBlock { Text = a.Description, FontSize = 12, Foreground = Res("MutedBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var open = new Button { Content = "Abrir ↗", Style = (Style)Application.Current.FindResource("PrimaryButton"), Padding = new Thickness(14, 5, 14, 5), FontSize = 12.5, Margin = new Thickness(0, 0, 8, 0) };
        string url = a.Url;
        open.Click += (_, _) => Open(url);
        System.Windows.Automation.AutomationProperties.SetName(open, "Abrir " + a.Name);
        buttons.Children.Add(open);
        if (a.Coupon.Length > 0)
        {
            var copy = new Button { Content = "Cupom " + a.Coupon, Style = (Style)Application.Current.FindResource("GhostButton"), Padding = new Thickness(12, 5, 12, 5), FontSize = 12.5, ToolTip = "Copiar o cupom" };
            copy.Click += (_, _) => { try { Clipboard.SetText(a.Coupon); copy.Content = "Copiado ✔"; } catch (System.Runtime.InteropServices.COMException) { } };
            buttons.Children.Add(copy);
        }
        text.Children.Add(buttons);
        panel.Children.Add(text);
        return new Border { Background = Res("SurfaceBrush"), BorderBrush = Res("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 10, 10), Child = panel };
    }

    private static Color TryColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch (FormatException) { return Color.FromRgb(0xFF, 0xC4, 0x00); }
    }

    private static Button Big(string title, string subtitle, Color background, Color foreground, Action click)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(foreground) });
        content.Children.Add(new TextBlock { Text = subtitle, FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromArgb(0xC0, foreground.R, foreground.G, foreground.B)) });
        var b = new Button { Content = content, Background = new SolidColorBrush(background), Style = (Style)Application.Current.FindResource("PrimaryButton"), Padding = new Thickness(18, 7, 18, 7), Margin = new Thickness(0, 0, 10, 0) };
        b.Click += (_, _) => click();
        return b;
    }

    private static TextBlock Title(string title, string subtitle)
    {
        var t = new TextBlock { Margin = new Thickness(2, 22, 0, 10), TextWrapping = TextWrapping.Wrap };
        t.Inlines.Add(new System.Windows.Documents.Run(title + "\n") { FontSize = 17, FontWeight = FontWeights.SemiBold });
        t.Inlines.Add(new System.Windows.Documents.Run(subtitle) { FontSize = 12, Foreground = Res("MutedBrush") });
        return t;
    }

    private static Border Card(UIElement child, Brush background) => new()
    {
        Background = background, BorderBrush = Res("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(22, 18, 22, 18),
        Margin = new Thickness(0, 8, 0, 8), Child = child,
    };
}
