using System.IO;
using System.Net.WebSockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using MBExtreme.Core.Remote;
using MBExtreme.Core.Settings;
using MBExtreme.UI.Remote;
using MouseAction = MBExtreme.Core.Remote.MouseAction;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Acesso remoto: o ID deste computador (para quem vai te atender) e o campo para digitar o ID de outro computador e conectar,
/// com tela, mouse, teclado e som. O outro lado sempre precisa aceitar.
/// </summary>
public sealed class RemoteAccessPage : ToolPageBase
{
    private readonly TextBlock _myId = new() { FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 32, FontWeight = FontWeights.Bold, Text = "— — —" };
    private readonly TextBlock _myState = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _myPassword = new() { FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBox _targetId = new() { FontSize = 20, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBox _targetPassword = new() { Margin = new Thickness(0, 4, 0, 8) };
    private readonly Button _connect;
    private readonly Button _disconnect;
    private Button? _toggle;
    private readonly TextBlock _sessionInfo = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
    private readonly System.Windows.Controls.Image _screen = new() { Stretch = System.Windows.Media.Stretch.Uniform, Focusable = true };
    private readonly Border _screenHost = new() { MinHeight = 360, Background = System.Windows.Media.Brushes.Black, Focusable = true, Visibility = Visibility.Collapsed };
    private readonly StackPanel _session = new() { Visibility = Visibility.Collapsed };
    private readonly StackPanel _recent = new();
    private CheckBox? _mute;
    private RemoteTechnician? _technician;
    private RemoteAudioPlayer? _player;
    private CancellationTokenSource? _cts;
    private string? _currentTarget;
    private int _frameWidth = 16, _frameHeight = 9;
    private DateTime _lastMove = DateTime.MinValue;

    public RemoteAccessPage() : base("Acesso remoto",
        "Veja e controle outro computador (tela, mouse, teclado e som) ou deixe alguém ajudar você. Cada acesso só começa depois que a pessoa do outro lado aceita, e ela pode encerrar quando quiser.")
    {
        _connect = AddButton("Conectar", async (_, _) => await ConnectAsync());
        _disconnect = AddButton("Desconectar", (_, _) => Disconnect("Desconectado."), false);
        _disconnect.IsEnabled = false;

        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 6, 0, 0) };
        grid.Children.Add(BuildThisComputer());
        grid.Children.Add(BuildOtherComputer());
        Body.Children.Add(grid);

        _session.Children.Add(Heading("Sessão"));
        _session.Children.Add(CardOf(_sessionInfo));
        _mute = new CheckBox { Content = "Silenciar o som do outro computador", Foreground = Brush("TextBrush"), Margin = new Thickness(0, 0, 0, 8) };
        _mute.Click += (_, _) => { if (_player is not null) _player.Muted = _mute.IsChecked == true; };
        _session.Children.Add(_mute);
        Body.Children.Add(_session);

        _screenHost.Child = _screen;
        _screen.MouseMove += OnMouseMove;
        _screen.MouseDown += (_, e) => { _screenHost.Focus(); SendMouse(MouseAction.Down, e); };
        _screen.MouseUp += (_, e) => SendMouse(MouseAction.Up, e);
        _screen.MouseWheel += OnWheel;
        _screenHost.PreviewKeyDown += (_, e) => SendKey(e, true);
        _screenHost.PreviewKeyUp += (_, e) => SendKey(e, false);
        Body.Children.Add(_screenHost);

        Body.Children.Add(Heading("Conexões recentes"));
        Body.Children.Add(_recent);
        Body.Children.Add(Muted("Para conectar a outro computador você precisa estar com a sua conta ativa (Licença e conta; a conta grátis serve). A tecla Windows não é enviada. Janelas de administrador (UAC) e a tela de login do Windows ainda não podem ser controladas."));

        Loaded += async (_, _) =>
        {
            AppServices.RemoteSupport.Changed += OnMineChanged;
            RefreshMine();
            RenderRecent();
            var (ok, message, _) = await AppServices.RemoteSupport.EnsureRegisteredAsync();
            if (!ok) SetStatus(message, "WarnBrush");
            RefreshMine();
        };
        Unloaded += (_, _) =>
        {
            AppServices.RemoteSupport.Changed -= OnMineChanged;
            Disconnect("Desconectado.");
        };
    }

    // ------------------------------------------------------------ este computador

    private UIElement BuildThisComputer()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Este computador", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brush("AccentBrush") });
        panel.Children.Add(Muted("Seu endereço de acesso. Passe este número a quem vai te atender."));
        panel.Children.Add(_myId);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var copy = SmallButton("Copiar ID", (_, _) => CopyId(), true);
        copy.Margin = new Thickness(0);
        buttons.Children.Add(copy);
        _toggle = SmallButton("Permitir acesso", async (_, _) => await ToggleListenAsync());
        buttons.Children.Add(_toggle);
        panel.Children.Add(buttons);
        panel.Children.Add(_myState);

        var auto = new CheckBox { Content = "Ficar disponível sempre que o MB Extreme estiver aberto", IsChecked = AppServices.Settings.RemoteAutoListen, Foreground = Brush("TextBrush"), Margin = new Thickness(0, 10, 0, 0) };
        auto.Click += (_, _) => Save(s => s.RemoteAutoListen = auto.IsChecked == true);
        panel.Children.Add(auto);

        var audio = new CheckBox { Content = "Compartilhar o som deste PC na sessão", IsChecked = AppServices.Settings.RemoteShareAudio, Foreground = Brush("TextBrush"), Margin = new Thickness(0, 4, 0, 0) };
        audio.Click += (_, _) => Save(s => s.RemoteShareAudio = audio.IsChecked == true);
        panel.Children.Add(audio);

        var password = new CheckBox { Content = "Exigir senha da sessão (mais seguro)", IsChecked = AppServices.Settings.RemoteRequirePassword, Foreground = Brush("TextBrush"), Margin = new Thickness(0, 4, 0, 0) };
        password.Click += (_, _) => AppServices.RemoteSupport.RequirePassword = password.IsChecked == true;
        panel.Children.Add(password);
        panel.Children.Add(_myPassword);
        panel.Children.Add(Muted("Sem senha, basta o ID e o seu aceite; os dois lados conferem um código de 6 dígitos que aparece durante a sessão (se forem diferentes, encerre). Com senha, a conexão só abre para quem a souber.", 11));

        var card = CardOf(panel);
        card.Margin = new Thickness(0, 0, 8, 8);
        return card;
    }

    private static void Save(Action<AppSettings> change)
    {
        change(AppServices.Settings);
        AppServices.Settings.Save();
    }

    private void OnMineChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(RefreshMine));

    private void RefreshMine()
    {
        var r = AppServices.RemoteSupport;
        _myId.Text = r.Id is { Length: 9 } id ? RemoteIds.FormatId(id) : "— — —";
        _myState.Text = r.State;
        if (_toggle is not null) _toggle.Content = r.Running ? "Bloquear acesso" : "Permitir acesso";
        _myPassword.Text = r.RequirePassword && r.Running && r.Password is { } p ? "Senha: " + RemoteIds.FormatPassword(p) : "";
        _myPassword.Visibility = string.IsNullOrEmpty(_myPassword.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CopyId()
    {
        var r = AppServices.RemoteSupport;
        if (r.Id is null) { SetStatus("O ID ainda não foi criado. Confira o endereço do servidor em Licença e conta.", "WarnBrush"); return; }
        string text = RemoteIds.FormatId(r.Id) + (r.RequirePassword && r.Password is { } p ? $"\nSenha: {RemoteIds.FormatPassword(p)}" : "");
        if (TryCopy(text)) SetStatus("ID copiado.", "OkBrush");
    }

    private async Task ToggleListenAsync()
    {
        var r = AppServices.RemoteSupport;
        if (r.Running)
        {
            r.Stop();
            SetStatus("Acesso bloqueado: ninguém consegue pedir acesso a este PC.", "OkBrush");
        }
        else
        {
            var (ok, message) = await r.StartAsync();
            SetStatus(message, ok ? "OkBrush" : "BadBrush");
        }
        RefreshMine();
    }

    // ------------------------------------------------------------ outro computador

    private UIElement BuildOtherComputer()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Outro computador", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brush("WarnBrush") });
        panel.Children.Add(Muted("Digite o ID do computador que você quer acessar."));
        panel.Children.Add(_targetId);
        panel.Children.Add(Muted("Senha da sessão (só se a pessoa informou uma):"));
        panel.Children.Add(_targetPassword);
        var go = new Button { Content = "Conectar", Style = (Style)FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(26, 9, 26, 9) };
        go.Click += async (_, _) => await ConnectAsync();
        panel.Children.Add(go);
        _targetId.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await ConnectAsync(); };

        var card = CardOf(panel);
        card.Margin = new Thickness(8, 0, 0, 8);
        return card;
    }

    private void RenderRecent()
    {
        _recent.Children.Clear();
        var items = AppServices.Settings.RemoteRecent.OrderByDescending(r => r.LastUsed).Take(8).ToList();
        if (items.Count == 0) { _recent.Children.Add(Muted("Nenhuma conexão ainda.")); return; }
        foreach (var item in items)
        {
            var current = item;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(SmallButton("Conectar", async (_, _) => { _targetId.Text = RemoteIds.FormatId(current.Id); await ConnectAsync(); }, true));
            buttons.Children.Add(SmallButton("Esquecer", (_, _) =>
            {
                AppServices.Settings.RemoteRecent.RemoveAll(r => r.Id == current.Id);
                AppServices.Settings.Save();
                RenderRecent();
            }));
            _recent.Children.Add(Row(RemoteIds.FormatId(current.Id), $"Último acesso: {current.LastUsed:dd/MM/yyyy HH:mm}", buttons));
        }
    }

    private async Task ConnectAsync()
    {
        if (_cts is not null) return;
        string? id = RemoteIds.ParseId(_targetId.Text);
        string password = RemoteIds.NormalizePassword(_targetPassword.Text);
        string? token = AppServices.License.CurrentToken;
        var config = AppServices.License.Config;
        if (id is null) { SetStatus("Digite o ID de 9 dígitos.", "WarnBrush"); return; }
        if (id == AppServices.Settings.RemoteId) { SetStatus("Este é o ID do seu próprio computador.", "WarnBrush"); return; }
        if (password.Length is > 0 and not RemoteIds.PasswordLength) { SetStatus("A senha tem 10 caracteres (ou deixe em branco).", "WarnBrush"); return; }
        if (token is null) { SetStatus("Entre com a sua conta em Licença e conta (a conta grátis serve) para conectar a outro computador.", "WarnBrush"); return; }
        if (config.ServerUrl.Length == 0) { SetStatus("O endereço do servidor não está configurado em Licença e conta.", "WarnBrush"); return; }

        _connect.IsEnabled = false;
        _disconnect.IsEnabled = true;
        _cts = new CancellationTokenSource();
        _currentTarget = id;
        _player = new RemoteAudioPlayer { Muted = _mute?.IsChecked == true };
        _technician = new RemoteTechnician();
        _session.Visibility = Visibility.Visible;
        _sessionInfo.Text = "Conectando...";
        _technician.StateChanged += s => Dispatcher.BeginInvoke(new Action(() => OnState(s)));
        _technician.FrameReceived += OnFrame;
        _technician.AudioReceived += a => _player?.Play(a);

        string url = (config.ServerUrl.StartsWith("https://") ? "wss://" + config.ServerUrl[8..] : "ws://" + config.ServerUrl[7..]) + "/v1/remote/ws/tech?id=" + id;
        try
        {
            using var socket = new ClientWebSocket();
            socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
            await socket.ConnectAsync(new Uri(url), _cts.Token);
            await _technician.RunAsync(socket, password.Length == 0 ? null : password, _cts.Token);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or UriFormatException)
        {
            SetStatus("Não foi possível conectar. Confira a internet e se a sua conta está ativa em Licença e conta.", "BadBrush");
        }
        finally
        {
            Disconnect(null);
        }
    }

    private void OnState(string state)
    {
        SetStatus(state);
        string code = _technician?.SecurityCode is { } c ? $" · código de segurança {c} (deve ser igual ao que aparece no outro computador)" : "";
        _sessionInfo.Text = state + code;
        if (_technician?.Connected == true && _currentTarget is { } id)
        {
            var recent = AppServices.Settings.RemoteRecent;
            recent.RemoveAll(r => r.Id == id);
            recent.Add(new RemoteRecentEntry { Id = id, LastUsed = DateTime.Now });
            AppServices.Settings.Save();
            _screenHost.Visibility = Visibility.Visible;
            RenderRecent();
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

    private void Disconnect(string? message)
    {
        _cts?.Cancel();
        _cts = null;
        _technician?.Dispose();
        _technician = null;
        _player?.Dispose();
        _player = null;
        _screen.Source = null;
        _screenHost.Visibility = Visibility.Collapsed;
        _session.Visibility = Visibility.Collapsed;
        _connect.IsEnabled = true;
        _disconnect.IsEnabled = false;
        if (message is not null) SetStatus(message);
    }

    // ------------------------------------------------------------ mouse e teclado

    /// <summary>Posição do mouse dentro da imagem exibida (com faixas pretas por causa da proporção), de 0 a 65535.</summary>
    private (int X, int Y)? Normalized(MouseEventArgs e)
    {
        var p = e.GetPosition(_screen);
        double aspect = (double)_frameWidth / _frameHeight;
        double w = _screen.ActualWidth, h = _screen.ActualHeight;
        if (w <= 0 || h <= 0) return null;
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
        var button = e.ChangedButton switch
        {
            System.Windows.Input.MouseButton.Left => MBExtreme.Core.Remote.MouseButton.Left,
            System.Windows.Input.MouseButton.Right => MBExtreme.Core.Remote.MouseButton.Right,
            System.Windows.Input.MouseButton.Middle => MBExtreme.Core.Remote.MouseButton.Middle,
            _ => MBExtreme.Core.Remote.MouseButton.None,
        };
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
