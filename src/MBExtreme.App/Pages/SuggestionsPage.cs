using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core;
using MBExtreme.Core.Info;
using MBExtreme.Core.Social;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Sugestões em duas colunas: à esquerda o formulário (tipo, mensagem com contador, nome, e-mail e o que mandar junto); à direita "Como funciona"
/// e "O que é enviado". Vai para o servidor; sem servidor, abre o WhatsApp ou o e-mail com o texto pronto.
/// </summary>
public sealed class SuggestionsPage : UserControl
{
    private string _kind = Suggestion.Kinds[0];
    private readonly TextBox _text = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 160, MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(10), FontSize = 13 };
    private readonly TextBlock _placeholder = new() { Margin = new Thickness(13, 11, 13, 0), IsHitTestVisible = false, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _counter = new() { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly TextBox _name = new() { Padding = new Thickness(10, 7, 10, 7), FontSize = 13 };
    private readonly TextBox _email = new() { Padding = new Thickness(10, 7, 10, 7), FontSize = 13 };
    private readonly CheckBox _version = new() { IsChecked = true };
    private readonly CheckBox _report = new() { IsChecked = false };
    private readonly TextBlock _status = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly WrapPanel _kinds = new();
    private readonly Button _send;
    private static readonly string Windows = MBExtreme.Core.Windows.SystemSummary.WindowsName();

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    public SuggestionsPage()
    {
        var root = new StackPanel { Margin = new Thickness(40, 28, 40, 28), MaxWidth = 1100 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
        head.Children.Add(new Border
        {
            Width = 54, Height = 54, CornerRadius = new CornerRadius(14), Background = Res("ButtonBrush"),
            Child = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 24, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        var titles = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "Sugestões", FontSize = 28, FontWeight = FontWeights.SemiBold });
        titles.Children.Add(new TextBlock { Text = "Sua mensagem vai direto para a equipe da MB InfoArt. Lemos todas.", Foreground = Res("MutedBrush") });
        head.Children.Add(titles);
        root.Children.Add(head);

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });

        // ---- formulário
        var form = new StackPanel();
        form.Children.Add(Label("Sobre o que é?"));
        BuildKinds();
        form.Children.Add(_kinds);

        var msgHead = new Grid { Margin = new Thickness(0, 14, 0, 6) };
        msgHead.Children.Add(Label("Mensagem *"));
        _counter.Foreground = Res("MutedBrush");
        msgHead.Children.Add(_counter);
        form.Children.Add(msgHead);
        var box = new Grid();
        System.Windows.Automation.AutomationProperties.SetName(_text, "Mensagem");
        _text.TextChanged += (_, _) => UpdateCounter();
        box.Children.Add(_text);
        _placeholder.Foreground = Res("MutedBrush");
        box.Children.Add(_placeholder);
        form.Children.Add(box);

        var people = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        people.ColumnDefinitions.Add(new ColumnDefinition());
        people.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        people.ColumnDefinitions.Add(new ColumnDefinition());
        var nameCol = new StackPanel();
        nameCol.Children.Add(Label("Nome (opcional)"));
        System.Windows.Automation.AutomationProperties.SetName(_name, "Nome");
        nameCol.Children.Add(_name);
        people.Children.Add(nameCol);
        var mailCol = new StackPanel();
        mailCol.Children.Add(Label("E-mail ou WhatsApp para resposta (opcional)"));
        System.Windows.Automation.AutomationProperties.SetName(_email, "E-mail ou WhatsApp para resposta");
        mailCol.Children.Add(_email);
        Grid.SetColumn(mailCol, 2);
        people.Children.Add(mailCol);
        form.Children.Add(people);

        form.Children.Add(Toggle(_version, "Enviar a versão do MB Extreme e do Windows", $"MB Extreme {AppInfo.Version} · {Windows}"));
        form.Children.Add(Toggle(_report, "Anexar o relatório do PC", "Peças, Windows e drivers (ajuda muito quando é um problema)"));

        var footer = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _status.Text = "🔒 Enviado com conexão segura.";
        _status.Foreground = Res("MutedBrush");
        footer.Children.Add(_status);
        _send = new Button { Content = "➤  Enviar", Style = (Style)Application.Current.FindResource("PrimaryButton"), Padding = new Thickness(26, 9, 26, 9) };
        _send.Click += async (_, _) => await SendAsync();
        Grid.SetColumn(_send, 1);
        footer.Children.Add(_send);
        form.Children.Add(new Border { BorderBrush = Res("BorderBrush"), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 16, 0, 0) });
        form.Children.Add(footer);
        columns.Children.Add(Card(form, new Thickness(0, 0, 16, 0)));

        // ---- ao lado: como funciona e o que é enviado
        var side = new StackPanel();
        var how = new StackPanel();
        how.Children.Add(new TextBlock { Text = "Como funciona", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        int n = 1;
        foreach (var step in new[] { "Você escreve aqui no MB Extreme e clica em Enviar.", "A equipe da MB InfoArt recebe e analisa a sua mensagem.", "Lemos todas. Se você deixou contato, a resposta chega por lá." })
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = Res("ButtonBrush"), VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = n++.ToString(), FontWeight = FontWeights.Bold, FontSize = 12, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            var t = new TextBlock { Text = step, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(10, 2, 0, 0) };
            Grid.SetColumn(t, 1);
            row.Children.Add(t);
            how.Children.Add(row);
        }
        side.Children.Add(Card(how, new Thickness(0, 0, 0, 14)));
        var what = new StackPanel();
        what.Children.Add(new TextBlock { Text = "🔒 O que é enviado", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        foreach (var line in new[] { "O assunto e a sua mensagem", "Nome e contato, se você preencher", "Versão do MB Extreme e do Windows, se a chave estiver ligada", "O relatório do PC, só se você marcar" })
            what.Children.Add(new TextBlock { Text = "•  " + line, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 3) });
        what.Children.Add(new TextBlock { Text = "Nada de arquivos, senhas ou fotos.", FontSize = 11.5, Foreground = Res("MutedBrush"), Margin = new Thickness(0, 8, 0, 0) });
        side.Children.Add(Card(what, new Thickness(0)));
        Grid.SetColumn(side, 1);
        columns.Children.Add(side);
        root.Children.Add(columns);

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
        UpdateCounter();
    }

    private void BuildKinds()
    {
        _kinds.Children.Clear();
        var icons = new Dictionary<string, string> { ["Sugestão de função"] = "", ["Encontrei um problema"] = "", ["Elogio"] = "", ["Outro"] = "" };
        foreach (var kind in Suggestion.Kinds)
        {
            string k = kind;
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = icons[k], FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            content.Children.Add(new TextBlock { Text = k, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Content = content, Style = (Style)Application.Current.FindResource(k == _kind ? "PrimaryButton" : "GhostButton"), Padding = new Thickness(14, 7, 14, 7), FontSize = 13, Margin = new Thickness(0, 0, 8, 8) };
            b.Click += (_, _) => { _kind = k; BuildKinds(); UpdateCounter(); };
            _kinds.Children.Add(b);
        }
    }

    private void UpdateCounter()
    {
        int len = _text.Text.Length;
        _counter.Text = $"{len} / {Suggestion.MaxText} · mínimo {Suggestion.MinText}";
        _counter.Foreground = len is > 0 and < Suggestion.MinText || len > Suggestion.MaxText ? Res("WarnBrush") : Res("MutedBrush");
        _placeholder.Text = Suggestion.Hint(_kind);
        _placeholder.Visibility = len == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static TextBlock Label(string text) => new() { Text = text, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };

    private static Border Card(UIElement child, Thickness margin) => new()
    {
        Background = Res("SurfaceBrush"), BorderBrush = Res("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(22, 18, 22, 18), Margin = margin, Child = child,
        VerticalAlignment = VerticalAlignment.Top,
    };

    private static Border Toggle(CheckBox box, string title, string detail)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        box.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(box, title);
        g.Children.Add(box);
        var t = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        t.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold });
        t.Children.Add(new TextBlock { Text = detail, FontSize = 11.5, Foreground = Res("MutedBrush"), TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        return new Border { BorderBrush = Res("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 14, 0, 0), Child = g };
    }

    private void Status(string text, string brush)
    {
        _status.Text = text;
        _status.Foreground = Res(brush);
        Motion.Flash(_status);
    }

    private async Task SendAsync()
    {
        string report = _report.IsChecked == true ? await Task.Run(() => SystemInfoService.ToText(SystemInfoService.Collect())) : "";
        bool withVersion = _version.IsChecked == true;
        var s = new Suggestion(_kind, _text.Text, _email.Text.Trim(), withVersion ? AppInfo.Version : "", withVersion ? Windows : "", report, _name.Text.Trim());
        if (s.Problem is { } problem) { Status(problem, "WarnBrush"); return; }
        _send.IsEnabled = false;
        Status("Enviando...", "MutedBrush");
        try
        {
            string server = AppServices.License.Config.ServerUrl;
            if (server.Length > 0 && await s.SendAsync(AppServices.Web, server))
            {
                Status("✔ Obrigado! Sua mensagem chegou na MB InfoArt.", "OkBrush");
                _text.Text = "";
                return;
            }
            // sem servidor: abre o WhatsApp (ou o e-mail) com o texto pronto
            var c = AppServices.Channel;
            string message = s.AsMessage();
            string? link = c.WhatsAppLink(message.Length > 1800 ? message[..1800] : message);
            if (link is null && c.SupportEmail.Contains('@'))
                link = $"mailto:{c.SupportEmail}?subject={Uri.EscapeDataString("[" + s.Kind + "] MB Extreme")}&body={Uri.EscapeDataString(message)}";
            if (link is not null)
            {
                try { Process.Start(new ProcessStartInfo(link) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { }
                Status("Abrimos a conversa com o texto pronto: é só enviar.", "OkBrush");
            }
            else
            {
                try { Clipboard.SetText(message); } catch (System.Runtime.InteropServices.COMException) { }
                Status("Ainda não dá para enviar direto daqui: o texto foi copiado. Mande para a MB InfoArt pelas redes (Nossas redes).", "WarnBrush");
            }
        }
        finally { _send.IsEnabled = true; }
    }
}
