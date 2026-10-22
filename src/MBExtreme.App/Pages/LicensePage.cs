using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Licensing;

namespace MBExtreme.UI.Pages;

/// <summary>Licença: mostra o plano, entra com a conta do site, renova e libera este PC.</summary>
public sealed class LicensePage : ToolPageBase
{
    private readonly LicenseManager _license = AppServices.License;
    private readonly StackPanel _state = new();
    private readonly TextBlock _code = new() { FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 34, FontWeight = FontWeights.Bold, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBox _url = new() { Width = 460, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 8) };
    private readonly TextBox _key = new() { Width = 460, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 8) };
    private readonly Button _login;
    private readonly Button _refresh;
    private readonly Button _signOut;
    private CancellationTokenSource? _loginCts;

    public LicensePage() : base("Licença",
        "Entre com a conta do site para ativar o plano Pro neste computador. A licença é assinada pelo servidor, fica guardada protegida pelo Windows e é renovada sozinha quando há internet; sem internet ela continua valendo até o vencimento.")
    {
        _login = AddButton("Entrar e ativar este PC", async (_, _) => await LoginAsync());
        _refresh = AddButton("Atualizar licença", async (_, _) => await RefreshAsync(), false);
        _signOut = AddButton("Remover deste PC", async (_, _) => await SignOutAsync(), false);
        AddButton("Minha conta no site", (_, _) => OpenAccount(), false);

        Body.Children.Add(_state);
        Body.Children.Add(_code);
        BuildAdvanced();

        Loaded += (_, _) => { _license.Changed += OnChanged; Render(); };
        Unloaded += (_, _) => _license.Changed -= OnChanged;
        Render();
    }

    private void OnChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(Render));

    private void Render()
    {
        var status = _license.Status;
        var config = _license.Config;
        _state.Children.Clear();

        (string title, string detail, string brush) = (status.State, config.Configured) switch
        {
            (LicenseState.Valid, _) => ($"Plano {(status.Plan == "pro" ? "Pro" : "gratuito")} ativo",
                $"{status.Payload!.Email} · vale até {status.Payload.ExpiresAt.LocalDateTime:dd/MM/yyyy HH:mm} (renova sozinho). Computadores permitidos: {status.Payload.MaxDevices}.", "OkBrush"),
            (LicenseState.Expired, _) => ("Licença vencida", status.Message, "WarnBrush"),
            (LicenseState.ClockTampered, _) => ("Relógio incorreto", status.Message, "WarnBrush"),
            (LicenseState.WrongDevice, _) => ("Licença de outro computador", status.Message, "BadBrush"),
            (LicenseState.Invalid, _) => ("Licença inválida", status.Message, "BadBrush"),
            (_, false) => ("Beta aberto", "O licenciamento ainda não está configurado: todos os recursos estão liberados.", "OkBrush"),
            _ => ("Sem licença", _license.Config.Enforce
                ? "Entre com a conta do site para liberar os recursos do plano Pro."
                : "Entre com a conta do site para ativar este PC. Enquanto isso, todos os recursos estão liberados (beta aberto).", "WarnBrush"),
        };

        _state.Children.Add(Row(title, detail, null, brush));
        if (_license.LastNotice is { Length: > 0 } notice) _state.Children.Add(Muted(notice));
        if (config.Enforce)
        {
            _state.Children.Add(Heading("Recursos do plano Pro"));
            var names = FeaturePolicy.ProPages.Select(k => NavigationMap.TileOf(k)?.Title ?? k).OrderBy(x => x).ToList();
            _state.Children.Add(Muted(string.Join(" · ", names)));
        }

        bool signedIn = status.Payload is not null && status.State != LicenseState.Invalid;
        _login.IsEnabled = config.Configured && _loginCts is null;
        _refresh.IsEnabled = config.Configured && signedIn;
        _signOut.IsEnabled = signedIn;
    }

    private void BuildAdvanced()
    {
        Body.Children.Add(Heading("Servidor de licenças (avançado)"));
        Body.Children.Add(Muted("Para testes locais: informe o endereço do servidor e a chave pública (veja docs/LICENCIAMENTO.md). Na versão de venda estes valores já vêm no programa."));
        _url.Text = AppServices.Settings.LicenseServerUrl;
        _key.Text = AppServices.Settings.LicensePublicKey;
        Body.Children.Add(Muted("Endereço do servidor (ex.: http://localhost:5100):"));
        Body.Children.Add(_url);
        Body.Children.Add(Muted("Chave pública:"));
        Body.Children.Add(_key);

        var enforce = Check("Exigir licença para os recursos do plano Pro", "Desligado, nada é bloqueado.");
        enforce.IsChecked = AppServices.Settings.LicenseEnforce;
        Body.Children.Add(enforce);

        Body.Children.Add(SmallButton("Salvar configuração", (_, _) =>
        {
            AppServices.Settings.LicenseServerUrl = _url.Text.Trim();
            AppServices.Settings.LicensePublicKey = _key.Text.Trim();
            AppServices.Settings.LicenseEnforce = enforce.IsChecked == true;
            AppServices.Settings.Save();
            _license.Start();
            SetStatus("Configuração salva.", "OkBrush");
            Render();
        }));
    }

    private async Task LoginAsync()
    {
        _loginCts = new CancellationTokenSource();
        _login.IsEnabled = false;
        SetStatus("Abrindo o navegador...");
        try
        {
            var (ok, message) = await _license.LoginAsync(code =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _code.Text = code.UserCode;
                    _code.Visibility = Visibility.Visible;
                    SetStatus($"Confirme no navegador que o código é {code.UserCode} e clique em Aprovar. Se o navegador não abriu: {code.VerificationUri}");
                }));
            }, _loginCts.Token);
            SetStatus(message, ok ? "OkBrush" : "WarnBrush");
        }
        finally
        {
            _code.Visibility = Visibility.Collapsed;
            _loginCts = null;
            Render();
        }
    }

    private async Task RefreshAsync()
    {
        SetStatus("Renovando...");
        var (ok, message) = await _license.RefreshAsync();
        SetStatus(message, ok ? "OkBrush" : "WarnBrush");
    }

    private async Task SignOutAsync()
    {
        if (MessageBox.Show("Remover a licença deste computador?\n\nA vaga é liberada no site e você pode ativar outro PC.", "Licença",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        SetStatus(await _license.SignOutAsync(), "OkBrush");
    }

    private void OpenAccount()
    {
        var config = _license.Config;
        if (!config.Configured) { SetStatus("O servidor de licenças não está configurado.", "WarnBrush"); return; }
        Open(config.ServerUrl + "/account");
    }
}

/// <summary>Mostrada no lugar de uma página do plano Pro quando a licença não vale.</summary>
public sealed class UpgradePage : ToolPageBase
{
    public UpgradePage(string title, string key) : base(title,
        "Este recurso faz parte do plano Pro. Entre com a conta do site para ativá-lo neste computador.")
    {
        AddButton("Ver licença", (_, _) => MainWindow.Current?.Go("license"));
        Body.Children.Add(Muted("Os recursos gratuitos continuam disponíveis no menu."));
        _ = key;
    }
}
