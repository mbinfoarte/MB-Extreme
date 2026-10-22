using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core;
using MBExtreme.Core.Info;
using MBExtreme.Core.Licensing;
using MBExtreme.Core.Support;

namespace MBExtreme.UI.Pages;

/// <summary>Suporte remoto com o AnyDesk: o cliente prepara o programa, passa o ID e aceita a conexão; o técnico conecta pelo ID.</summary>
public sealed class SupportPage : ToolPageBase
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private static readonly AnyDeskService Service = new();

    private readonly TextBlock _id = new() { FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 32, FontWeight = FontWeights.Bold, Text = "— — —" };
    private readonly TextBox _problem = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 6, 0, 8) };
    private readonly TextBox _technicianId = new() { Width = 260, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 8) };
    private readonly CheckBox _withReport;
    private readonly Button _prepare;
    private readonly Button _close;
    private string? _currentId;
    private bool _busy;

    public SupportPage() : base("Suporte remoto",
        "Peça ajuda do suporte da MB InfoArt. Você prepara o AnyDesk (programa de acesso remoto) com um clique, passa o seu ID e só quem você autorizar entra no seu computador, depois de você aceitar a conexão na tela.")
    {
        _prepare = AddButton("Preparar o AnyDesk", async (_, _) => await PrepareAsync());
        _close = AddButton("Fechar o AnyDesk", (_, _) => CloseAnyDesk(), false);
        _close.IsEnabled = false;

        Body.Children.Add(Heading("Seu ID"));
        Body.Children.Add(CardOf(_id));
        var idButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var copyId = SmallButton("Copiar ID", (_, _) =>
        {
            if (_currentId is null) { SetStatus("Prepare o AnyDesk primeiro.", "WarnBrush"); return; }
            if (TryCopy(AnyDeskRules.FormatId(_currentId))) SetStatus("ID copiado.", "OkBrush");
        }, true);
        copyId.Margin = new Thickness(0);
        idButtons.Children.Add(copyId);
        Body.Children.Add(idButtons);

        Body.Children.Add(Heading("O que você precisa?"));
        Body.Children.Add(Muted("Descreva o problema ou a manutenção que quer. O pedido já leva o seu ID."));
        Body.Children.Add(_problem);
        _withReport = Check("Incluir o relatório do sistema", "Modelo das peças, Windows e versões, para o técnico já chegar sabendo. Não inclui senhas nem arquivos pessoais.");
        _withReport.IsChecked = true;
        Body.Children.Add(_withReport);
        var request = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var copyRequest = SmallButton("Copiar pedido de suporte", async (_, _) => await CopyRequestAsync(), true);
        copyRequest.Margin = new Thickness(0);
        request.Children.Add(copyRequest);
        Body.Children.Add(request);
        Body.Children.Add(Muted("Envie o texto copiado ao suporte (WhatsApp, e-mail...). Quando o técnico conectar, o AnyDesk mostra um aviso: só clique em Aceitar se foi você quem pediu."));

        Body.Children.Add(Heading("Segurança"));
        Body.Children.Add(CardOf(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 22,
            Text = "• O acesso só começa depois que você clica em Aceitar.\n• Você vê tudo o que o técnico faz e pode encerrar a qualquer momento (botão Fechar o AnyDesk ou o X do AnyDesk).\n• Nunca informe senhas do Windows, bancos ou e-mail ao técnico, e não configure acesso sem supervisão.\n• O AnyDesk é baixado do site oficial só quando você pede e tem a assinatura digital conferida antes de abrir.",
        }));

        Body.Children.Add(Heading("Sou o técnico"));
        Body.Children.Add(Muted("Digite o ID que o cliente passou. O AnyDesk abre e pede ao cliente para aceitar."));
        Body.Children.Add(_technicianId);
        var connect = new StackPanel { Orientation = Orientation.Horizontal };
        var go = SmallButton("Conectar ao cliente", async (_, _) => await ConnectAsync(), true);
        go.Margin = new Thickness(0);
        connect.Children.Add(go);
        Body.Children.Add(connect);

        Body.Children.Add(Muted("O AnyDesk é um programa de terceiros (não vem com o MB Extreme). O uso pessoal é gratuito; o atendimento comercial por quem presta o suporte exige uma licença do AnyDesk, conforme os termos do fabricante."));
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        _close.IsEnabled = Service.IsRunning;
        _prepare.Content = AnyDeskService.Locate() is null ? "Preparar o AnyDesk" : "Abrir o AnyDesk e mostrar o ID";
    }

    private async Task<string?> EnsureInstalledAsync()
    {
        string? path = AnyDeskService.Locate();
        if (path is not null) return path;

        if (MessageBox.Show("O AnyDesk será baixado do site oficial (download.anydesk.com) e a assinatura digital será conferida antes de abrir.\n\nBaixar agora?",
                "Preparar o AnyDesk", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return null;

        SetStatus("Baixando o AnyDesk...");
        var progress = new Progress<double>(p => SetStatus($"Baixando o AnyDesk... {p:0}%"));
        var (ok, message) = await AnyDeskService.DownloadAsync(Http, progress, CancellationToken.None);
        SetStatus(message, ok ? "OkBrush" : "BadBrush");
        return ok ? AnyDeskService.Locate() : null;
    }

    private async Task PrepareAsync()
    {
        if (_busy) return;
        _busy = true;
        _prepare.IsEnabled = false;
        try
        {
            string? path = await EnsureInstalledAsync();
            if (path is null) return;

            if (!Service.Start(path)) { SetStatus("Não foi possível abrir o AnyDesk.", "BadBrush"); return; }
            SetStatus("Abrindo o AnyDesk e lendo o seu ID...");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            string? id = await AnyDeskService.GetIdAsync(path, cts.Token);
            if (id is null)
            {
                SetStatus("O AnyDesk abriu, mas não consegui ler o ID. Veja o número no topo da janela do AnyDesk e informe ao suporte.", "WarnBrush");
                return;
            }

            _currentId = id;
            _id.Text = AnyDeskRules.FormatId(id);
            SetStatus("Pronto. Passe o ID ao suporte e aceite a conexão quando o técnico pedir.", "OkBrush");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Não consegui ler o ID a tempo. Veja o número na janela do AnyDesk.", "WarnBrush");
        }
        finally
        {
            _busy = false;
            _prepare.IsEnabled = true;
            Refresh();
        }
    }

    private async Task CopyRequestAsync()
    {
        string name = AppServices.License.Status.Payload is { } payload ? LicenseProfile.NameOf(payload, Environment.UserName) : Environment.UserName;
        string text = AnyDeskRules.SupportMessage(name, _currentId, _problem.Text, AppInfo.DisplayVersion);

        if (_withReport.IsChecked == true)
        {
            SetStatus("Montando o relatório do sistema...");
            string report = await Task.Run(() => SystemInfoService.ToText(SystemInfoService.Collect()));
            text += Environment.NewLine + Environment.NewLine + report;
        }

        if (TryCopy(text)) SetStatus(_currentId is null ? "Pedido copiado (ainda sem ID: clique em Preparar o AnyDesk)." : "Pedido copiado. Cole na conversa com o suporte.", _currentId is null ? "WarnBrush" : "OkBrush");
    }

    private async Task ConnectAsync()
    {
        string? uri = AnyDeskRules.ConnectUri(_technicianId.Text);
        if (uri is null) { SetStatus("Digite o ID do cliente (9 ou 10 números).", "WarnBrush"); return; }

        string? path = await EnsureInstalledAsync();
        if (path is null) return;

        try
        {
            Process.Start(new ProcessStartInfo(path, uri[8..]) { UseShellExecute = true }); // anydesk.exe <id>
            SetStatus("Conectando... o cliente precisa aceitar a conexão.", "OkBrush");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            SetStatus("Não foi possível abrir o AnyDesk: " + ex.Message, "BadBrush");
        }
    }

    private void CloseAnyDesk()
    {
        SetStatus(Service.StopStarted() ? "AnyDesk fechado." : "O AnyDesk aberto por aqui já foi fechado. Se ele foi instalado no Windows, feche pela janela dele.", "OkBrush");
        Refresh();
    }
}
