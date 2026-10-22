using System.Net.Http;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using System.Windows;
using MBExtreme.Core.Licensing;
using MBExtreme.Core.Remote;

namespace MBExtreme.UI.Remote;

/// <summary>Suporte remoto próprio, lado do cliente: mantém a conexão com o servidor enquanto o usuário quiser receber suporte.</summary>
internal sealed class RemoteSupportController
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private CancellationTokenSource? _cts;
    private RemoteAgent? _agent;
    private Window? _banner;

    public bool Running => _cts is not null;
    public string? Id => AppServices.Settings.RemoteId;
    public string? Password => _agent?.Password;
    public string State { get; private set; } = "Desligado";
    public event EventHandler? Changed;

    private void Set(string state)
    {
        State = state;
        Application.Current?.Dispatcher.BeginInvoke(new Action(() => Changed?.Invoke(this, EventArgs.Empty)));
    }

    public async Task<(bool Ok, string Message)> StartAsync()
    {
        if (Running) return (true, "Já está ligado.");
        var config = AppServices.License.Config;
        if (config.ServerUrl.Length == 0) return (false, "O endereço do servidor não está configurado (Licença e conta).");

        var settings = AppServices.Settings;
        string? secret = SecretStore.Unprotect(settings.RemoteProtectedSecret);
        try
        {
            if (string.IsNullOrEmpty(settings.RemoteId) || string.IsNullOrEmpty(secret))
            {
                var response = await Http.PostAsJsonAsync(config.ServerUrl + "/v1/remote/register", new { deviceId = DeviceIdentity.Current() });
                if (!response.IsSuccessStatusCode) return (false, "O servidor não criou o ID de suporte agora. Tente mais tarde.");
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                settings.RemoteId = body.GetProperty("id").GetString();
                secret = body.GetProperty("secret").GetString();
                settings.RemoteProtectedSecret = SecretStore.Protect(secret!);
                settings.Save();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            return (false, "Sem conexão com o servidor de suporte: " + ex.Message);
        }

        _agent = new RemoteAgent(new ScreenGrabber(), new InputInjector(), AskAcceptAsync);
        _agent.StateChanged += Set;
        _agent.SessionEnded += () => Application.Current?.Dispatcher.BeginInvoke(new Action(CloseBanner));
        _cts = new CancellationTokenSource();
        _ = RunLoopAsync(config.ServerUrl, settings.RemoteId!, secret!, _cts.Token);
        Set("Conectando...");
        return (true, "Pronto. Passe o ID e a senha ao técnico.");
    }

    private async Task RunLoopAsync(string serverUrl, string id, string secret, CancellationToken stop)
    {
        string url = (serverUrl.StartsWith("https://") ? "wss://" + serverUrl[8..] : "ws://" + serverUrl[7..]) +
                     $"/v1/remote/ws/client?id={id}&secret={Uri.EscapeDataString(secret)}";
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(new Uri(url), stop);
                Set("Aguardando o técnico.");
                await _agent!.RunAsync(socket, stop);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or UriFormatException or HttpRequestException)
            {
                if (stop.IsCancellationRequested) break;
                Set("Sem conexão com o servidor. Tentando de novo...");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(5), stop); }
            catch (OperationCanceledException) { break; }
        }

        Set("Desligado");
    }

    private async Task<bool> AskAcceptAsync(string technician)
    {
        var tcs = new TaskCompletionSource<bool>();
        Application.Current.Dispatcher.Invoke(() =>
        {
            var answer = MessageBox.Show(
                $"O técnico \"{technician}\" da MB InfoArt quer acessar o seu computador.\n\nVocê verá tudo o que ele fizer e pode encerrar a qualquer momento. Só aceite se foi você quem pediu suporte.\n\nPermitir?",
                "Pedido de acesso remoto", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer == MessageBoxResult.Yes) ShowBanner(technician);
            tcs.SetResult(answer == MessageBoxResult.Yes);
        });
        return await tcs.Task;
    }

    private void ShowBanner(string technician)
    {
        CloseBanner();
        var button = new System.Windows.Controls.Button { Content = "Encerrar", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(12, 0, 0, 0) };
        button.Click += (_, _) => Stop();
        var panel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(14, 8, 14, 8) };
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = $"● Sessão remota ativa: {technician} está no seu computador",
            Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(button);
        _banner = new Window
        {
            Content = new System.Windows.Controls.Border { Background = System.Windows.Media.Brushes.DarkRed, Child = panel },
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.WidthAndHeight,
            Topmost = true, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
            Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - 480) / 2, Top = 0,
        };
        _banner.Show();
    }

    private void CloseBanner()
    {
        _banner?.Close();
        _banner = null;
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        CloseBanner();
        Set("Desligado");
    }

    /// <summary>Gera um novo ID (o antigo deixa de funcionar).</summary>
    public void ResetId()
    {
        Stop();
        AppServices.Settings.RemoteId = null;
        AppServices.Settings.RemoteProtectedSecret = null;
        AppServices.Settings.Save();
    }
}
