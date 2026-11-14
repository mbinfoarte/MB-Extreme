using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MBExtreme.Core.Licensing;

namespace MBExtreme.UI.Controls;

/// <summary>Atalhos para falar com o servidor de indicações/avaliações com a licença deste PC.</summary>
internal static class InviteApi
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static bool Ready => AppServices.License.Config.Configured && AppServices.License.CurrentToken is not null;

    public static InviteClient Client => new(Http, AppServices.License.Config.ServerUrl);

    public static string License => AppServices.License.CurrentToken ?? "";

    public static string Device => AppServices.License.DeviceId;

    public static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    public static readonly Color Yellow = Color.FromRgb(0xFF, 0xC4, 0x00);

    public static ToggleButton Chip(string text, Action<bool> changed)
    {
        var b = new ToggleButton { Content = text, Style = (Style)Application.Current.FindResource("ChipToggle"), Margin = new Thickness(0, 0, 6, 6) };
        b.Click += (_, _) => changed(b.IsChecked == true);
        return b;
    }

    public static Button Button(string text, bool primary = false) => new()
    {
        Content = text, Style = (Style)Application.Current.FindResource(primary ? "PrimaryButton" : "GhostButton"),
        Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0), FontSize = 12.5,
    };

    public static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    public static void Copy(string text)
    {
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException) { }
    }
}

/// <summary>
/// Cartão "O que você está achando do MB Extreme?" do Painel: 5 estrelas; 4-5 leva ao Indique um amigo, 1-3 pergunta o que faltou
/// e oferece o suporte. Aparece uma vez, a partir do 2º dia de uso com a conta.
/// </summary>
internal sealed class RatingCard : Border
{
    private static readonly string[] Likes = { "Limpeza", "Otimização de jogos", "Benchmark", "MB Control", "Gravação e live", "Painel ao vivo" };
    private static readonly string[] Issues = { "Ficou mais lento", "Jogo travou", "Difícil de usar", "Faltou uma função", "Erro ou travamento", "Preço" };
    private static readonly string[] Labels = { "", "Ruim", "Fraco", "Regular", "Bom", "Excelente!" };

    private readonly Action _close;
    private readonly StackPanel _root = new();
    private readonly StackPanel _stars = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _label = new() { FontSize = 13.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    private readonly StackPanel _follow = new();
    private readonly HashSet<string> _picked = new();
    private int _rating;

    public static bool ShouldShow()
    {
        var s = AppServices.Settings;
        bool signedIn = InviteApi.Ready;
        if (signedIn && s.RatingFirstUseUtc is null) { s.RatingFirstUseUtc = DateTime.UtcNow; s.Save(); }
        return RatingPrompt.ShouldShow(DateTime.UtcNow, s.RatingFirstUseUtc, s.RatingSnoozeUtc, s.RatingDone, signedIn);
    }

    public RatingCard(Action close)
    {
        _close = close;
        CornerRadius = new CornerRadius(14);
        Padding = new Thickness(14, 12, 14, 12);
        Margin = new Thickness(0, 0, 0, 8);
        BorderThickness = new Thickness(1);
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xC4, 0x00));
        Background = InviteApi.Res("SurfaceBrush");

        var head = new DockPanel();
        var later = new Button { Content = "✕", Style = (Style)FindResource("GhostButton"), Padding = new Thickness(8, 2, 8, 2), FontSize = 11, ToolTip = "Agora não (pergunta de novo em 3 dias)" };
        later.Click += (_, _) => { AppServices.Settings.RatingSnoozeUtc = DateTime.UtcNow.AddDays(3); AppServices.Settings.Save(); _close(); };
        DockPanel.SetDock(later, Dock.Right);
        head.Children.Add(later);
        var badge = GlassFx.Badge("", InviteApi.Yellow, 32, breathe: true);
        badge.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(badge, Dock.Left);
        head.Children.Add(badge);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "SUA OPINIÃO", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(InviteApi.Yellow) });
        titles.Children.Add(new TextBlock { Text = "O que você está achando do MB Extreme?", FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        head.Children.Add(titles);
        _root.Children.Add(head);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        for (int i = 1; i <= 5; i++)
        {
            int n = i;
            var star = new Button
            {
                Style = (Style)FindResource("GhostButton"), Width = 38, Height = 38, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 0),
                Content = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 18 },
            };
            System.Windows.Automation.AutomationProperties.SetName(star, n == 1 ? "1 estrela" : $"{n} estrelas");
            star.Click += (_, _) => Pick(n);
            _stars.Children.Add(star);
        }
        row.Children.Add(_stars);
        row.Children.Add(_label);
        _root.Children.Add(row);
        _root.Children.Add(new TextBlock { Text = "Toque nas estrelas. Leva 5 segundos e a gente lê cada resposta.", FontSize = 11, Foreground = InviteApi.Res("MutedBrush"), Margin = new Thickness(0, 6, 0, 0) });
        _root.Children.Add(_follow);
        Child = _root;
    }

    private void Pick(int n)
    {
        bool sideChanged = _rating == 0 || (_rating >= 4) != (n >= 4);
        _rating = n;
        for (int i = 0; i < _stars.Children.Count; i++)
        {
            var t = (TextBlock)((Button)_stars.Children[i]).Content;
            bool on = i < n;
            t.Text = on ? "" : "";
            t.Foreground = on ? new SolidColorBrush(InviteApi.Yellow) : InviteApi.Res("MutedBrush");
            if (on && i == n - 1) Motion.Pop(t);
        }
        _label.Text = Labels[n];
        _label.Foreground = new SolidColorBrush(n >= 4 ? InviteApi.Yellow : Color.FromRgb(0xFF, 0xA9, 0x4D));
        if (_root.Children.Count > 3 && _root.Children[3] is TextBlock hint) hint.Visibility = Visibility.Collapsed;
        if (!sideChanged) return;
        _picked.Clear();
        _follow.Children.Clear();
        if (n >= 4) BuildGood(); else BuildBad();
    }

    private WrapPanel Chips(IEnumerable<string> options)
    {
        var wrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (string o in options) wrap.Children.Add(InviteApi.Chip(o, on => { if (on) _picked.Add(o); else _picked.Remove(o); }));
        return wrap;
    }

    private void BuildGood()
    {
        _follow.Children.Add(new TextBlock { Text = "Do que você mais gostou? (opcional)", FontSize = 12, Foreground = InviteApi.Res("MutedBrush"), Margin = new Thickness(0, 10, 0, 0) });
        _follow.Children.Add(Chips(Likes));
        var invite = new Border
        {
            CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 6, 0, 0),
            Background = new LinearGradientBrush(Color.FromArgb(0x30, 0xFF, 0xC4, 0x00), Color.FromArgb(0x0C, 0xFF, 0x8A, 0x00), 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xC4, 0x00)), BorderThickness = new Thickness(1),
        };
        var g = new DockPanel();
        var go = InviteApi.Button("Indicar agora", true);
        go.Margin = new Thickness(8, 0, 0, 0);
        go.Click += async (_, _) => { await SendAsync(); MainWindow.Current?.Go("license"); };
        DockPanel.SetDock(go, Dock.Right);
        g.Children.Add(go);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = "Indique um amigo e ganhe +7 dias grátis", FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = "Seu amigo testa por 7 dias (em vez de 3). Se ele assinar, você ganha mais 7 dias. Sem limite.", FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = InviteApi.Res("MutedBrush") });
        g.Children.Add(text);
        invite.Child = g;
        _follow.Children.Add(invite);
        var send = InviteApi.Button("Só enviar a avaliação");
        send.HorizontalAlignment = HorizontalAlignment.Left;
        send.Margin = new Thickness(0, 8, 0, 0);
        send.Click += async (_, _) => await SendAsync();
        _follow.Children.Add(send);
    }

    private readonly TextBox _comment = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60, MaxLength = 1500, FontSize = 12.5, Margin = new Thickness(0, 4, 0, 0) };

    private void BuildBad()
    {
        _follow.Children.Add(new TextBlock { Text = "O que faltou ou deu errado? (ajuda a gente a melhorar)", FontSize = 12, Foreground = InviteApi.Res("MutedBrush"), Margin = new Thickness(0, 10, 0, 0) });
        _follow.Children.Add(Chips(Issues));
        _follow.Children.Add(new TextBlock { Text = "Conte com suas palavras (opcional)", FontSize = 12, Foreground = InviteApi.Res("MutedBrush"), Margin = new Thickness(0, 4, 0, 0) });
        System.Windows.Automation.AutomationProperties.SetName(_comment, "Comentário");
        _follow.Children.Add(_comment);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var send = InviteApi.Button("Enviar", true);
        send.Click += async (_, _) => await SendAsync();
        var help = InviteApi.Button("Falar com o suporte agora");
        help.Click += async (_, _) => { await SendAsync(); MainWindow.Current?.Go("support"); };
        row.Children.Add(send);
        row.Children.Add(help);
        _follow.Children.Add(row);
    }

    private bool _sent;

    private async Task SendAsync()
    {
        if (_sent || _rating == 0) return;
        _sent = true;
        var s = AppServices.Settings;
        s.RatingDone = true;
        s.Save();
        var good = _rating >= 4;
        var data = new FeedbackData(_rating, good ? _picked.ToList() : new(), good ? new() : _picked.ToList(), good ? "" : _comment.Text.Trim(),
            MBExtreme.Core.AppInfo.Version, AppServices.License.Status.Plan ?? "");
        try
        {
            await InviteApi.Client.SendFeedbackAsync(InviteApi.License, InviteApi.Device, data);
            _root.Children.Clear();
            _root.Children.Add(new TextBlock { Text = "Obrigado pela avaliação! ★", FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(InviteApi.Yellow) });
            _root.Children.Add(new TextBlock { Text = good ? "Que bom que está gostando." : "A gente vai olhar com carinho o que você contou.", FontSize = 11.5, Foreground = InviteApi.Res("MutedBrush") });
            await Task.Delay(2500);
        }
        catch (LicenseNetworkException) { }
        _close();
    }
}

/// <summary>"Indique um amigo" na tela Conta e licença: link, código, mensagem pronta, WhatsApp, números e a lista; e o campo "tenho um código".</summary>
internal sealed class InviteCard : Border
{
    private readonly StackPanel _body = new();
    private readonly TextBlock _status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };

    public InviteCard()
    {
        Style = (Style)Application.Current.FindResource("Card");
        Padding = new Thickness(16, 14, 16, 14);
        Margin = new Thickness(0, 10, 0, 8);
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xC4, 0x00));
        var root = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var badge = GlassFx.Badge("", InviteApi.Yellow, 36, breathe: true);
        badge.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(badge, Dock.Left);
        head.Children.Add(badge);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "Indique um amigo e ganhe +7 dias grátis", FontSize = 16, FontWeight = FontWeights.SemiBold });
        titles.Children.Add(new TextBlock { Text = "Seu amigo testa o MB Extreme por 7 dias (em vez de 3). Quando ele assinar, você ganha mais 7 dias no seu plano. Sem limite.", FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = InviteApi.Res("MutedBrush") });
        head.Children.Add(titles);
        root.Children.Add(head);
        root.Children.Add(_body);
        root.Children.Add(Claim());
        root.Children.Add(_status);
        Child = root;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _body.Children.Clear();
        if (!InviteApi.Ready)
        {
            _body.Children.Add(new TextBlock { Text = "Entre com a sua conta para ver o seu link de indicação.", FontSize = 12.5, Foreground = InviteApi.Res("MutedBrush") });
            return;
        }
        _body.Children.Add(new TextBlock { Text = "Carregando o seu link...", FontSize = 12, Foreground = InviteApi.Res("MutedBrush") });
        try
        {
            var me = await InviteApi.Client.MeAsync(InviteApi.License, InviteApi.Device);
            _body.Children.Clear();
            Render(me);
        }
        catch (LicenseNetworkException ex)
        {
            _body.Children.Clear();
            _body.Children.Add(new TextBlock { Text = "Não deu para carregar as indicações: " + ex.Message, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = InviteApi.Res("WarnBrush") });
        }
    }

    private void Render(InviteSummary me)
    {
        // link + botões
        var link = new TextBox { Text = me.Link, IsReadOnly = true, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 6, 8, 6) };
        System.Windows.Automation.AutomationProperties.SetName(link, "Seu link de indicação");
        var linkRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var copy = InviteApi.Button("Copiar link");
        copy.Margin = new Thickness(8, 0, 0, 0);
        copy.Click += (_, _) => { InviteApi.Copy(me.Link); copy.Content = "Copiado ✓"; };
        DockPanel.SetDock(copy, Dock.Right);
        linkRow.Children.Add(copy);
        linkRow.Children.Add(link);
        _body.Children.Add(linkRow);

        var code = new TextBlock { FontSize = 12, Foreground = InviteApi.Res("MutedBrush"), Margin = new Thickness(0, 0, 0, 8) };
        code.Inlines.Add("ou o código ");
        code.Inlines.Add(new System.Windows.Documents.Run(me.Code) { FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(InviteApi.Yellow) });
        code.Inlines.Add(" (o amigo digita no programa, em Conta e licença)");
        _body.Children.Add(code);

        var share = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        var wa = InviteApi.Button("Enviar pelo WhatsApp", true);
        wa.Click += (_, _) => InviteApi.Open("https://wa.me/?text=" + Uri.EscapeDataString(me.ShareText));
        var msg = InviteApi.Button("Copiar mensagem pronta");
        msg.Click += (_, _) => { InviteApi.Copy(me.ShareText); msg.Content = "Mensagem copiada ✓"; };
        share.Children.Add(wa);
        share.Children.Add(msg);
        _body.Children.Add(share);
        _body.Children.Add(new Border
        {
            Background = InviteApi.Res("Surface2Brush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 10),
            Child = new TextBlock { Text = "\"" + me.ShareText + "\"", FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = InviteApi.Res("MutedBrush") },
        });

        // números
        var stats = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (k, v, color) in new[]
        {
            ("Amigos convidados", me.Invited.ToString(), "TextBrush"), ("Testando agora", me.Testing.ToString(), "TextBrush"),
            ("Assinaram", me.Subscribed.ToString(), "OkBrush"), ("Dias que você ganhou", "+" + me.DaysEarned, "AccentBrush"),
        })
        {
            var st = new StackPanel();
            st.Children.Add(new TextBlock { Text = k, FontSize = 11, Foreground = InviteApi.Res("MutedBrush") });
            st.Children.Add(new TextBlock { Text = v, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = InviteApi.Res(color) });
            stats.Children.Add(new Border { Background = InviteApi.Res("Surface2Brush"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 0), Child = st });
        }
        _body.Children.Add(stats);

        // lista
        if (me.List.Count > 0)
        {
            _body.Children.Add(new TextBlock { Text = "Suas indicações", FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 6) });
            foreach (var f in me.List.Take(30))
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var days = new TextBlock { Text = f.Days > 0 ? $"+{f.Days} dias" : "—", Width = 70, TextAlignment = TextAlignment.Right, FontWeight = FontWeights.SemiBold, Foreground = f.Days > 0 ? InviteApi.Res("AccentBrush") : InviteApi.Res("MutedBrush"), VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(days, Dock.Right);
                row.Children.Add(days);
                string brush = f.State switch { "assinou" => "OkBrush", "testando" => "AccentBrush", _ => "MutedBrush" };
                var state = new Border
                {
                    CornerRadius = new CornerRadius(999), Padding = new Thickness(9, 2, 9, 2), BorderThickness = new Thickness(1), BorderBrush = InviteApi.Res(brush), VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = f.State, FontSize = 11, Foreground = InviteApi.Res(brush) },
                };
                DockPanel.SetDock(state, Dock.Right);
                row.Children.Add(state);
                var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                who.Children.Add(new TextBlock { Text = f.Name, FontSize = 12.5, FontWeight = FontWeights.SemiBold });
                string when = f.AtMs is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("dd/MM") : "";
                who.Children.Add(new TextBlock { Text = (f.State == "testando" ? $"faltam {f.DaysLeft} dias · " : "") + (when.Length > 0 ? "entrou em " + when : ""), FontSize = 11, Foreground = InviteApi.Res("MutedBrush") });
                row.Children.Add(who);
                _body.Children.Add(new Border { Background = InviteApi.Res("Surface2Brush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 6), Child = row });
            }
        }
        _body.Children.Add(new TextBlock
        {
            Text = "Os dias entram sozinhos quando o pagamento do amigo é confirmado. Se ele pedir reembolso, os dias saem. Indicar a si mesmo (mesma conta ou mesmo PC) não vale.",
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = InviteApi.Res("MutedBrush"), Margin = new Thickness(0, 2, 0, 10),
        });
    }

    private UIElement Claim()
    {
        var box = new TextBox { Width = 160, FontSize = 13, MaxLength = 16, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 5, 8, 5), CharacterCasing = CharacterCasing.Upper };
        System.Windows.Automation.AutomationProperties.SetName(box, "Código de indicação");
        var use = InviteApi.Button("Usar código");
        use.Margin = new Thickness(8, 0, 0, 0);
        use.Click += async (_, _) =>
        {
            if (!InviteApi.Ready) { Say("Entre com a sua conta primeiro.", "WarnBrush"); return; }
            string code = InviteClient.NormalizeCode(box.Text);
            if (code.Length < 4) { Say("Digite o código que o seu amigo mandou.", "WarnBrush"); return; }
            use.IsEnabled = false;
            try
            {
                var (ok, message) = await InviteApi.Client.ClaimAsync(InviteApi.License, InviteApi.Device, code);
                Say(message, ok ? "OkBrush" : "WarnBrush");
                if (ok) _ = AppServices.License.RefreshAsync(silent: true);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { Say("Sem conexão com o servidor.", "WarnBrush"); }
            finally { use.IsEnabled = true; }
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = "Ganhou um código de um amigo?", FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        row.Children.Add(box);
        row.Children.Add(use);
        return row;
    }

    private void Say(string text, string brush)
    {
        _status.Text = text;
        _status.Foreground = InviteApi.Res(brush);
        _status.Margin = new Thickness(0, 6, 0, 0);
    }
}
