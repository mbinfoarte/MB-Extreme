using System.IO;
using System.Net.WebSockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using MBExtreme.Core.Remote;
using MouseAction = MBExtreme.Core.Remote.MouseAction;

namespace MBExtreme.UI.Pages;

/// <summary>Console do técnico: conecta ao computador de um cliente pelo ID, vê a tela e controla mouse e teclado.</summary>
public sealed class RemoteConsolePage : ToolPageBase
{
    private readonly TextBox _id = new() { Width = 220, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 8) };
    private readonly TextBox _password = new() { Width = 220, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 8) };
    private readonly System.Windows.Controls.Image _screen = new() { Stretch = System.Windows.Media.Stretch.Uniform, Focusable = true };
    private readonly Border _screenHost = new() { MinHeight = 320, Background = System.Windows.Media.Brushes.Black, Focusable = true };
    private readonly Button _connect;
    private readonly Button _disconnect;
    private RemoteTechnician? _technician;
    private CancellationTokenSource? _cts;
    private int _frameWidth = 16, _frameHeight = 9;
    private DateTime _lastMove = DateTime.MinValue;

    public RemoteConsolePage() : base("Console do técnico",
        "Acesso remoto do MB Extreme para atender clientes. Peça ao cliente para abrir Suporte remoto, ligar o acesso e informar o ID e a senha. Ele precisa aceitar a conexão na tela dele. Só contas de técnico autorizadas no servidor conseguem conectar.")
    {
        _connect = AddButton("Conectar", async (_, _) => await ConnectAsync());
        _disconnect = AddButton("Desconectar", (_, _) => Disconnect(), false);
        _disconnect.IsEnabled = false;

        Body.Children.Add(Heading("ID do cliente"));
        Body.Children.Add(_id);
        Body.Children.Add(Heading("Senha da sessão"));
        Body.Children.Add(_password);

        _screenHost.Child = _screen;
        _screen.MouseMove += OnMouseMove;
        _screen.MouseDown += (_, e) => SendMouse(MouseAction.Down, e);
        _screen.MouseUp += (_, e) => SendMouse(MouseAction.Up, e);
        _screen.MouseWheel += OnWheel;
        _screenHost.PreviewKeyDown += (_, e) => SendKey(e, true);
        _screenHost.PreviewKeyUp += (_, e) => SendKey(e, false);
        _screen.MouseDown += (_, _) => _screenHost.Focus();
        Body.Children.Add(Heading("Tela do cliente"));
        Body.Children.Add(_screenHost);
        Body.Children.Add(Muted("Clique na tela para controlar. A tecla Windows não é enviada. Janelas de administrador (UAC) e a tela de login do Windows ainda não podem ser controladas por esta versão."));
        Unloaded += (_, _) => Disconnect();
    }

    private async Task ConnectAsync()
    {
        string? id = RemoteIds.ParseId(_id.Text);
        string password = RemoteIds.NormalizePassword(_password.Text);
        string? token = AppServices.License.CurrentToken;
        var config = AppServices.License.Config;
        if (id is null) { SetStatus("Informe o ID de 9 dígitos do cliente.", "WarnBrush"); return; }
        if (password.Length != RemoteIds.PasswordLength) { SetStatus("A senha tem 10 caracteres.", "WarnBrush"); return; }
        if (token is null) { SetStatus("Entre com a sua conta de técnico em Licença e conta.", "WarnBrush"); return; }
        if (config.ServerUrl.Length == 0) { SetStatus("O endereço do servidor não está configurado.", "WarnBrush"); return; }

        _connect.IsEnabled = false;
        _disconnect.IsEnabled = true;
        _cts = new CancellationTokenSource();
        _technician = new RemoteTechnician();
        _technician.StateChanged += s => Dispatcher.BeginInvoke(new Action(() => SetStatus(s)));
        _technician.FrameReceived += OnFrame;

        string url = (config.ServerUrl.StartsWith("https://") ? "wss://" + config.ServerUrl[8..] : "ws://" + config.ServerUrl[7..]) + "/v1/remote/ws/tech?id=" + id;
        try
        {
            using var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
            await socket.ConnectAsync(new Uri(url), _cts.Token);
            await _technician.RunAsync(socket, password, _cts.Token);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or UriFormatException)
        {
            SetStatus("Não foi possível conectar. Sua conta precisa estar autorizada como técnico no servidor.", "BadBrush");
        }
        finally
        {
            Disconnect();
        }
    }

    private void OnFrame(FrameMessage frame)
    {
        try
        {
            var bitmap = new BitmapImage();
            using (var stream = new MemoryStream(frame.Jpeg))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }
            bitmap.Freeze();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _screen.Source = bitmap;
                _frameWidth = frame.ScreenWidth;
                _frameHeight = frame.ScreenHeight;
            }));
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or InvalidOperationException) { }
    }

    private void Disconnect()
    {
        _cts?.Cancel();
        _cts = null;
        _technician?.Dispose();
        _technician = null;
        _screen.Source = null;
        _connect.IsEnabled = true;
        _disconnect.IsEnabled = false;
    }

    /// <summary>Posição do mouse dentro da imagem exibida (com faixas pretas por causa da proporção), de 0 a 65535.</summary>
    private (int X, int Y)? Normalized(MouseEventArgs e)
    {
        var p = e.GetPosition(_screen);
        double aspect = (double)_frameWidth / _frameHeight;
        double w = _screen.ActualWidth, h = _screen.ActualHeight;
        double shownW = w / h > aspect ? h * aspect : w;
        double shownH = w / h > aspect ? h : w / aspect;
        double x = p.X - (w - shownW) / 2, y = p.Y - (h - shownH) / 2;
        if (x < 0 || y < 0 || x > shownW || y > shownH) return null;
        return (RemoteMessages.Normalize(x, shownW), RemoteMessages.Normalize(y, shownH));
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if ((DateTime.UtcNow - _lastMove).TotalMilliseconds < 33) return;   // até ~30 por segundo
        _lastMove = DateTime.UtcNow;
        if (Normalized(e) is { } pos) _ = _technician?.SendAsync(new MouseMessage(MouseAction.Move, MBExtreme.Core.Remote.MouseButton.None, pos.X, pos.Y, 0));
    }

    private void SendMouse(MouseAction action, MouseButtonEventArgs e)
    {
        if (Normalized(e) is not { } pos) return;
        var button = e.ChangedButton switch { System.Windows.Input.MouseButton.Left => MBExtreme.Core.Remote.MouseButton.Left, System.Windows.Input.MouseButton.Right => MBExtreme.Core.Remote.MouseButton.Right, System.Windows.Input.MouseButton.Middle => MBExtreme.Core.Remote.MouseButton.Middle, _ => MBExtreme.Core.Remote.MouseButton.None };
        if (button == MBExtreme.Core.Remote.MouseButton.None) return;
        _ = _technician?.SendAsync(new MouseMessage(action, button, pos.X, pos.Y, 0));
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (Normalized(e) is { } pos) _ = _technician?.SendAsync(new MouseMessage(MouseAction.Wheel, MBExtreme.Core.Remote.MouseButton.None, pos.X, pos.Y, e.Delta));
    }

    private void SendKey(KeyEventArgs e, bool down)
    {
        if (_technician?.Connected != true) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (!RemoteMessages.IsKeyAllowed(vk)) return;
        bool extended = key is Key.Right or Key.Left or Key.Up or Key.Down or Key.Insert or Key.Delete or Key.Home or Key.End or Key.PageUp or Key.PageDown or Key.RightCtrl or Key.RightAlt or Key.Divide or Key.NumLock;
        _ = _technician.SendAsync(new KeyMessage(vk, down, extended));
        e.Handled = true;
    }
}
