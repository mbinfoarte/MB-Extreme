using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core;
using MBExtreme.Core.Info;
using MBExtreme.Core.Social;

namespace MBExtreme.UI.Pages;

/// <summary>Sugestões: ideia, problema ou elogio, com contato e relatório do PC opcionais. Vai para o servidor; sem servidor, WhatsApp ou e-mail.</summary>
public sealed class SuggestionsPage : ToolPageBase
{
    private string _kind = Suggestion.Kinds[0];
    private readonly TextBox _text = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 140, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 6, 0, 10), Padding = new Thickness(8) };
    private readonly TextBox _contact = new() { Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 6, 0, 10) };
    private readonly CheckBox _report = new() { Content = "Anexar o relatório do PC (peças, Windows e drivers; sem senhas nem arquivos)", Margin = new Thickness(0, 0, 0, 12) };

    public SuggestionsPage() : base("Sugestões", "Tem uma ideia para o MB Extreme, achou um problema ou quer mandar um elogio? Escreva aqui: a equipe da MB InfoArt lê tudo.")
    {
        Body.Children.Add(Muted("Tipo:", 12));
        Body.Children.Add(Choice(Suggestion.Kinds, 0, i => _kind = Suggestion.Kinds[i]));
        Body.Children.Add(Muted("O que você quer contar:", 12));
        System.Windows.Automation.AutomationProperties.SetName(_text, "Texto da sugestão");
        Body.Children.Add(_text);
        Body.Children.Add(Muted("Seu WhatsApp ou e-mail (opcional, se quiser resposta):", 12));
        System.Windows.Automation.AutomationProperties.SetName(_contact, "Contato");
        Body.Children.Add(_contact);
        _report.Foreground = Brush("TextBrush");
        Body.Children.Add(_report);
        var send = SmallButton("Enviar", async (_, _) => await SendAsync(), true);
        send.HorizontalAlignment = HorizontalAlignment.Left;
        Body.Children.Add(send);
    }

    private async Task SendAsync()
    {
        string report = _report.IsChecked == true ? await Task.Run(() => SystemInfoService.ToText(SystemInfoService.Collect())) : "";
        var s = new Suggestion(_kind, _text.Text, _contact.Text.Trim(), AppInfo.Version, MBExtreme.Core.Windows.SystemSummary.WindowsName(), report);
        if (s.Problem is { } problem) { SetStatus(problem, "WarnBrush"); return; }
        SetStatus("Enviando...");
        string server = AppServices.License.Config.ServerUrl;
        if (server.Length > 0 && await s.SendAsync(AppServices.Web, server))
        {
            SetStatus("Obrigado! Sua mensagem chegou na MB InfoArt.", "OkBrush");
            _text.Text = "";
            return;
        }
        // sem servidor: abre o WhatsApp (ou e-mail) com o texto pronto
        var c = AppServices.Channel;
        string? link = c.WhatsAppLink(s.AsMessage().Length > 1800 ? s.AsMessage()[..1800] : s.AsMessage());
        if (link is null && c.SupportEmail.Contains('@'))
            link = $"mailto:{c.SupportEmail}?subject={Uri.EscapeDataString("[" + s.Kind + "] MB Extreme")}&body={Uri.EscapeDataString(s.AsMessage())}";
        if (link is not null)
        {
            try { Process.Start(new ProcessStartInfo(link) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { }
            SetStatus("Abrimos a conversa com o texto pronto: é só enviar.", "OkBrush");
        }
        else if (TryCopy(s.AsMessage()))
            SetStatus("Ainda não dá para enviar direto daqui: o texto foi copiado. Mande para a MB InfoArt pelas redes (Nossas redes).", "WarnBrush");
    }
}
