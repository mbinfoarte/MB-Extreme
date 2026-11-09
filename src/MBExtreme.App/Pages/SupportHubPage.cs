using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MBExtreme.Core;
using MBExtreme.Core.Info;
using MBExtreme.Core.Remote;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Suporte em uma tela simples, com três cartões grandes: falar com o suporte, deixar o técnico acessar (ID e senha em letras grandes,
/// sempre com o seu aceite) e enviar o relatório do PC. O acesso remoto completo e o AnyDesk ficam em "Modo técnico".
/// </summary>
public sealed class SupportHubPage : ToolPageBase
{
    private readonly TextBlock _id = new() { FontFamily = new FontFamily("Consolas"), FontSize = 34, FontWeight = FontWeights.Bold, Text = "— — —", HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _password = new() { FontFamily = new FontFamily("Consolas"), FontSize = 17, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _state = new() { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 8) };
    private readonly Button _toggle;
    private readonly TextBox _problem = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60, MaxHeight = 120, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 6, 0, 8) };

    public SupportHubPage() : base("Suporte", "Precisa de ajuda? Fale com o suporte da MB InfoArt, deixe o técnico acessar o seu PC (só com o seu aceite) ou mande o relatório do computador.")
    {
        AddButton("Modo técnico (acesso remoto completo)", (_, _) => MainWindow.Current?.Go("remote"), false);
        AddButton("AnyDesk", (_, _) => MainWindow.Current?.Go("support-anydesk"), false);
        var cards = new UniformGrid { Columns = 3 };
        cards.Children.Add(Card("💬", "Falar com o suporte", TalkCard()));
        _toggle = SmallButton("Permitir acesso", async (_, _) => await ToggleAsync(), true);
        cards.Children.Add(Card("🖥", "Deixar o técnico acessar", AccessCard()));
        cards.Children.Add(Card("📋", "Enviar relatório do PC", ReportCard()));
        Body.Children.Add(cards);
        Motion.Cascade(cards, 70);

        Body.Children.Add(Heading("Perguntas frequentes"));
        foreach (var (q, a) in new[]
        {
            ("O técnico consegue entrar sem eu deixar?", "Não. Cada acesso aparece na sua tela e só começa depois que você clica em Aceitar. Você pode encerrar a qualquer momento."),
            ("O relatório leva minhas senhas ou arquivos?", "Não. Ele tem o modelo das peças, a versão do Windows e dos drivers. Nada de senhas, fotos ou documentos."),
            ("O PC ficou lento depois de um ajuste. E agora?", "Abra a Central Desfazer: tudo que o MB Extreme mudou volta ao valor original com um clique."),
            ("Como atualizo o MB Extreme?", "Ele procura versão nova sozinho. Em Sobre → Atualizações você escolhe atualizar sozinho ou só ser avisado."),
        })
            Body.Children.Add(Row(q, a));

        Loaded += async (_, _) =>
        {
            AppServices.RemoteSupport.Changed += OnChanged;
            Refresh();
            var (ok, message, _) = await AppServices.RemoteSupport.EnsureRegisteredAsync();
            if (!ok) _state.Text = message;
            Refresh();
        };
        Unloaded += (_, _) => AppServices.RemoteSupport.Changed -= OnChanged;
    }

    private Border Card(string icon, string title, UIElement body)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = icon, FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 8) });
        panel.Children.Add(body);
        var card = CardOf(panel);
        card.Margin = new Thickness(0, 0, 10, 10);
        return card;
    }

    private UIElement TalkCard()
    {
        var c = AppServices.Channel;
        var p = new StackPanel();
        p.Children.Add(Muted("Conte o que está acontecendo:", 12));
        System.Windows.Automation.AutomationProperties.SetName(_problem, "Descreva o problema");
        p.Children.Add(_problem);
        string Message() => $"Olá! Preciso de ajuda com o meu PC (MB Extreme {AppInfo.Version})." + (_problem.Text.Trim().Length > 0 ? "\n\n" + _problem.Text.Trim() : "")
                            + (AppServices.RemoteSupport.Id is { Length: 9 } id ? $"\n\nMeu ID de acesso: {RemoteIds.FormatId(id)}" : "");
        if (c.WhatsAppLink() is not null)
            p.Children.Add(SmallButton("Chamar no WhatsApp", (_, _) => Launch(c.WhatsAppLink(Message())!), true));
        if (c.SupportEmail.Contains('@'))
            p.Children.Add(SmallButton("Mandar e-mail", (_, _) => Launch($"mailto:{c.SupportEmail}?subject={Uri.EscapeDataString("Suporte MB Extreme")}&body={Uri.EscapeDataString(Message())}")));
        p.Children.Add(SmallButton("Copiar mensagem", (_, _) => { if (TryCopy(Message())) SetStatus("Mensagem copiada. Cole na conversa com o suporte.", "OkBrush"); }));
        if (c.SupportHours.Length > 0) p.Children.Add(Muted("Atendimento: " + c.SupportHours, 11.5));
        if (c.WhatsAppLink() is null && !c.SupportEmail.Contains('@'))
            p.Children.Add(Muted("O número do WhatsApp do suporte ainda não foi configurado; copie a mensagem e envie pelo canal que você já usa.", 11));
        return p;
    }

    private UIElement AccessCard()
    {
        var p = new StackPanel();
        p.Children.Add(Muted("Passe este número ao técnico:", 12));
        p.Children.Add(_id);
        p.Children.Add(_password);
        p.Children.Add(_state);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(SmallButton("Copiar", (_, _) =>
        {
            var r = AppServices.RemoteSupport;
            if (r.Id is null) return;
            if (TryCopy(RemoteIds.FormatId(r.Id) + (r.RequirePassword && r.Password is { } pw ? $"\nSenha: {RemoteIds.FormatPassword(pw)}" : ""))) SetStatus("ID copiado.", "OkBrush");
        }));
        row.Children.Add(_toggle);
        p.Children.Add(row);
        p.Children.Add(Muted("Quando o técnico pedir para entrar, aparece um aviso na sua tela. Só aceite se foi você quem chamou.", 11));
        return p;
    }

    private UIElement ReportCard()
    {
        var p = new StackPanel();
        p.Children.Add(Muted("Peças, Windows, drivers e a última análise do Painel, para o técnico já chegar sabendo. Sem senhas nem arquivos pessoais.", 12));
        p.Children.Add(SmallButton("Copiar relatório", async (_, _) =>
        {
            SetStatus("Montando o relatório...");
            string report = await Task.Run(() => SystemInfoService.ToText(SystemInfoService.Collect()));
            if (TryCopy(report)) SetStatus("Relatório copiado. Cole na conversa com o suporte.", "OkBrush");
        }, true));
        p.Children.Add(SmallButton("Enviar junto com uma sugestão", (_, _) => MainWindow.Current?.Go("suggest")));
        return p;
    }

    private void OnChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(Refresh));

    private void Refresh()
    {
        var r = AppServices.RemoteSupport;
        _id.Text = r.Id is { Length: 9 } id ? RemoteIds.FormatId(id) : "— — —";
        _password.Text = r.RequirePassword && r.Running && r.Password is { } pw ? "Senha: " + RemoteIds.FormatPassword(pw) : "";
        _password.Visibility = _password.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _state.Text = r.Running ? "● Disponível: o técnico pode pedir acesso" : "○ Acesso bloqueado";
        _state.Foreground = Brush(r.Running ? "OkBrush" : "MutedBrush");
        _toggle.Content = r.Running ? "Bloquear acesso" : "Permitir acesso";
    }

    private async Task ToggleAsync()
    {
        var r = AppServices.RemoteSupport;
        if (r.Running) r.Stop();
        else
        {
            var (ok, message) = await r.StartAsync();
            if (!ok) SetStatus(message, "BadBrush");
        }
        Refresh();
    }

    private static void Launch(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
