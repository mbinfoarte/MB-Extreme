using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MBExtreme.Core;
using MBExtreme.Core.Remote;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Suporte remoto com o RustDesk (oficial, código aberto, rede pública): à esquerda o ID e a senha deste PC para receber ajuda;
/// à direita, para o plano Técnico, conectar no ID de outro PC (tela ou arquivos) e a lista de atendidos; embaixo, o acesso sem
/// ninguém no PC (senha fixa). Uma faixa avisa sempre que alguém está conectado, com Encerrar. A senha da sessão troca sozinha
/// quando a conexão acaba (menos no acesso sem ninguém).
/// </summary>
public sealed partial class RustDeskSupportPage : ToolPageBase
{
    private readonly Border _live = new() { Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(1) };
    private readonly TextBlock _liveText = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _state = new() { FontSize = 12, Margin = new Thickness(0, 10, 0, 4) };
    private readonly TextBlock _id = new() { FontFamily = new FontFamily("Consolas"), FontSize = 34, FontWeight = FontWeights.Bold, Text = "— — —" };
    private readonly TextBlock _password = new() { FontFamily = new FontFamily("Consolas"), FontSize = 19, FontWeight = FontWeights.SemiBold, Text = "••••-••••", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _targetId = new() { FontFamily = new FontFamily("Consolas"), FontSize = 19, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 8, 0, 6) };
    private readonly PasswordBox _targetPassword = new() { FontSize = 13, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 8) };
    private readonly StackPanel _peers = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly CheckBox _unattended = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _setup = new();
    private readonly StackPanel _main = new();
    private readonly Grid _ready = new() { Visibility = Visibility.Collapsed };
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromSeconds(2) };
    private string? _myId;
    private string _sessionPassword = "";
    private bool _shown, _busy, _wasOpen;
    private DateTime _openSince;
    private static string PeersFile => RustDesk.PeersFile(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
    private static bool Tech => AppServices.License.IsAllowed("remote-tech");

    public RustDeskSupportPage() : base("Suporte remoto", "Receba ajuda ou acesse outro PC com tela, mouse, teclado, som e arquivos. Conexão criptografada de ponta a ponta.")
    {
        var liveRow = new DockPanel();
        var end = SmallButton("Encerrar acesso", (_, _) => { RustDeskHost.EndSessions(); SetStatus("Acesso encerrado.", "OkBrush"); }, true);
        DockPanel.SetDock(end, Dock.Right);
        liveRow.Children.Add(end);
        liveRow.Children.Add(_liveText);
        _live.Child = liveRow;
        _live.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x1D, 0x00));
        _live.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00));
        Body.Children.Add(_live);

        // a lista de quem pediu suporte (para o administrador) fica em Administrador › Suporte ao vivo
        Body.Children.Add(_main);

        // antes de instalar: um cartão explicando e o botão
        _setup.Children.Add(new TextBlock { Text = "Ativar o acesso remoto", FontSize = 16, FontWeight = FontWeights.SemiBold });
        _setup.Children.Add(Muted("Na primeira vez, o MB Extreme baixa e instala o módulo de acesso remoto (RustDesk, código aberto, 25 MB). Ele fica rodando discreto e só aceita conexões com o ID e a senha que aparecem aqui.", 12));
        _setup.Children.Add(Wrap(Btn("Ativar acesso remoto", async () => await InstallAsync(), true)));
        _main.Children.Add(CardOf(_setup));

        _ready.ColumnDefinitions.Add(new ColumnDefinition());
        _ready.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        _ready.ColumnDefinitions.Add(new ColumnDefinition());
        _ready.RowDefinitions.Add(new RowDefinition());
        _ready.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var receive = CardOf(BuildReceive());
        _ready.Children.Add(receive);
        var other = CardOf(BuildOther());
        Grid.SetColumn(other, 2);
        _ready.Children.Add(other);
        var quick = CardOf(BuildQuick());
        Grid.SetRow(quick, 1);
        Grid.SetColumnSpan(quick, 3);
        _ready.Children.Add(quick);
        _ready.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var unattended = CardOf(BuildUnattended());
        Grid.SetRow(unattended, 2);
        Grid.SetColumnSpan(unattended, 3);
        _ready.Children.Add(unattended);
        _main.Children.Add(_ready);

        var foot = Muted($"Conexão pela rede pública do RustDesk ({RustDesk.SourceUrl}, código aberto, licença {RustDesk.License}). A imagem da tela não passa pelos servidores da MB InfoArt.", 11);
        foot.Margin = new Thickness(0, 6, 0, 0);
        _main.Children.Add(foot);

        _watch.Tick += (_, _) => Watch();
        Loaded += async (_, _) =>
        {
            _watch.Start();
            if (!_shown) { _shown = true; await PrepareAsync(); }
        };
        Unloaded += (_, _) => _watch.Stop();
    }

    // ---------- montagem ----------

    private static WrapPanel Wrap(params UIElement[] items)
    {
        var w = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var i in items) w.Children.Add(i);
        return w;
    }

    private Button Btn(string text, Func<Task> click, bool primary = false)
    {
        var b = SmallButton(text, async (_, _) => await click(), primary);
        b.Margin = new Thickness(0, 0, 8, 6);
        return b;
    }

    private Button Btn(string text, Action click, bool primary = false) => Btn(text, () => { click(); return Task.CompletedTask; }, primary);

    private StackPanel BuildReceive()
    {
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = "Receber suporte", FontSize = 16, FontWeight = FontWeights.SemiBold });
        p.Children.Add(Muted("Passe o ID e a senha para o técnico. Ele só entra com a senha ou se você aceitar.", 12));
        p.Children.Add(_state);
        p.Children.Add(Muted("Seu ID", 11.5));
        var idRow = new StackPanel { Orientation = Orientation.Horizontal };
        idRow.Children.Add(_id);
        var refresh = SmallButton("", async (_, _) => await RefreshIdAsync(), false);
        var refreshContent = new StackPanel { Orientation = Orientation.Horizontal };
        refreshContent.Children.Add(new TextBlock { Text = "\uE72C", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        refreshContent.Children.Add(new TextBlock { Text = "Atualizar", VerticalAlignment = VerticalAlignment.Center });
        refresh.Content = refreshContent;
        System.Windows.Automation.AutomationProperties.SetName(refresh, "Atualizar o ID");
        refresh.VerticalAlignment = VerticalAlignment.Center;
        refresh.Margin = new Thickness(12, 0, 0, 0);
        refresh.ToolTip = "Busca o ID de novo e, se ele não vier, reinicia o serviço do acesso remoto. Não precisa fechar o programa.";
        idRow.Children.Add(refresh);
        p.Children.Add(idRow);
        p.Children.Add(Muted("Senha (troca a cada sessão; no acesso sem ninguém, vale a senha fixa)", 11.5));
        var pw = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        pw.Children.Add(_password);
        var show = SmallButton("Mostrar", (_, _) => ShowPassword(), false);
        var renew = SmallButton("Nova senha", async (_, _) => await NewPasswordAsync(), false);
        pw.Children.Add(show);
        pw.Children.Add(renew);
        p.Children.Add(pw);
        p.Children.Add(Wrap(
            Btn("Copiar ID e senha", () => { if (_myId is not null && TryCopy(RustDesk.ShareText(_myId, _sessionPassword))) SetStatus("ID e senha copiados.", "OkBrush"); }, true),
            Btn("Enviar pelo WhatsApp", () => { if (_myId is not null) Open("https://wa.me/?text=" + Uri.EscapeDataString(RustDesk.ShareText(_myId, _sessionPassword))); }),
            Btn("Falar com a MB InfoArt", () =>
            {
                string msg = $"Olá! Preciso de ajuda com o meu PC (MB Extreme {AppInfo.Version})." + (_myId is null ? "" : $"\n\nMeu ID de acesso: {RustDesk.FormatId(_myId)}");
                if (AppServices.Channel.WhatsAppLink(msg) is { } link) Open(link);
            })));
        return p;
    }

    private StackPanel BuildOther()
    {
        var p = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new TextBlock { Text = "Acessar outro PC", FontSize = 16, FontWeight = FontWeights.SemiBold });
        head.Children.Add(Tag("TÉCNICO"));
        p.Children.Add(head);
        p.Children.Add(Muted("Digite o ID do cliente. A tela dele abre numa janela nova.", 12));
        System.Windows.Automation.AutomationProperties.SetName(_targetId, "ID do outro PC");
        System.Windows.Automation.AutomationProperties.SetName(_targetPassword, "Senha do outro PC (opcional)");
        p.Children.Add(_targetId);
        p.Children.Add(Muted("Senha (opcional: sem senha, o cliente aceita na tela dele)", 11));
        p.Children.Add(_targetPassword);
        p.Children.Add(Wrap(Btn("Conectar", () => Connect(_targetId.Text, false), true), Btn("Transferir arquivos", () => Connect(_targetId.Text, true))));
        p.Children.Add(Muted("Atendidos recentemente", 11.5));
        p.Children.Add(_peers);
        RenderPeers();
        if (!Tech)
        {
            p.IsEnabled = false;
            p.Opacity = 0.55;
            p.ToolTip = "Disponível no plano Técnico.";
        }
        return p;
    }

    private readonly CheckBox _quick = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
    private readonly Button _help = new();

    private DockPanel BuildQuick()
    {
        var row = new DockPanel();
        _quick.IsChecked = SupportPresence.Prefs.QuickSupport == true;
        _quick.Checked += (_, _) => { SupportPresence.SetEnabled(true); PaintHelp(); SetStatus("Suporte rápido ligado.", "OkBrush"); };
        _quick.Unchecked += (_, _) => { SupportPresence.SetEnabled(false); PaintHelp(); SetStatus("Suporte rápido desligado: este PC saiu do painel da MB InfoArt.", "OkBrush"); };
        DockPanel.SetDock(_quick, Dock.Left);
        row.Children.Add(_quick);
        _help.Style = (Style)FindResource("PrimaryButton");
        _help.Margin = new Thickness(12, 0, 0, 0);
        _help.Click += async (_, _) =>
        {
            bool on = !SupportPresence.Prefs.HelpActive(DateTime.Now);
            await SupportPresence.RequestHelpAsync(on);
            PaintHelp();
            SetStatus(on ? "Pedido enviado: o suporte da MB InfoArt vê o seu PC em destaque e pede acesso. Aceite na janela que aparecer." : "Pedido de ajuda cancelado.", "OkBrush");
        };
        DockPanel.SetDock(_help, Dock.Right);
        row.Children.Add(_help);
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = "Suporte rápido da MB InfoArt", FontSize = 14, FontWeight = FontWeights.SemiBold });
        text.Children.Add(Muted("O suporte vê quando este PC está com o MB aberto e pede acesso sem você passar ID e senha. Você sempre aceita ou recusa na tela. Nunca enviamos a senha.", 11.5));
        row.Children.Add(text);
        PaintHelp();
        return row;
    }

    private void PaintHelp()
    {
        var prefs = SupportPresence.Prefs;
        _help.Visibility = prefs.QuickSupport == true && InviteUiReady ? Visibility.Visible : Visibility.Collapsed;
        _help.Content = prefs.HelpActive(DateTime.Now) ? "Cancelar pedido de ajuda" : "Pedir ajuda";
        if (!InviteUiReady) { _quick.IsEnabled = false; _quick.ToolTip = "Entre na sua conta para usar o suporte rápido."; }
    }

    private static bool InviteUiReady => MBExtreme.UI.Controls.InviteApi.Ready;

    private DockPanel BuildUnattended()
    {
        var row = new DockPanel();
        _unattended.IsChecked = SupportPresence.Prefs.Unattended;
        _unattended.Margin = new Thickness(0, 0, 12, 0);
        _unattended.Checked += async (_, _) => await SetUnattendedAsync(true);
        _unattended.Unchecked += async (_, _) => await SetUnattendedAsync(false);
        DockPanel.SetDock(_unattended, Dock.Left);
        row.Children.Add(_unattended);
        var fixedBtn = SmallButton("Definir senha fixa", async (_, _) => { if (_unattended.IsChecked == true) await FixedPasswordAsync(); else _unattended.IsChecked = true; }, false);
        DockPanel.SetDock(fixedBtn, Dock.Right);
        row.Children.Add(fixedBtn);
        var text = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new TextBlock { Text = "Acesso sem ninguém no PC", FontSize = 14, FontWeight = FontWeights.SemiBold });
        head.Children.Add(Tag("TÉCNICO"));
        text.Children.Add(head);
        text.Children.Add(Muted("Liga junto com o Windows e aceita a senha fixa que você define, para manutenção à distância. Este PC mostra um aviso sempre que alguém conectar.", 11.5));
        row.Children.Add(text);
        if (!Tech) { row.IsEnabled = false; row.Opacity = 0.55; row.ToolTip = "Disponível no plano Técnico."; }
        return row;
    }

    private Border Tag(string text) => new()
    {
        Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)), CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 1, 7, 1),
        Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.Black },
    };

    // ---------- ações ----------

    private async Task InstallAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            await RustDeskHost.InstallAsync(UiProgress.Text(t => SetStatus(t)), CancellationToken.None);
            SetStatus("Acesso remoto ativado.", "OkBrush");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.Net.Http.HttpRequestException or TaskCanceledException or System.ComponentModel.Win32Exception)
        {
            SetStatus("Não foi possível ativar o acesso remoto: " + ex.Message, "BadBrush");
            return;
        }
        finally { _busy = false; }
        await PrepareAsync();
    }

    private async Task PrepareAsync()
    {
        bool installed = RustDeskHost.IsInstalled;
        _setup.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        ((Border)_setup.Parent).Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        _ready.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        if (!installed) return;
        _lastIdTry = DateTime.Now.AddSeconds(30);   // a primeira leitura já está tentando; a nova tentativa sozinha espera
        Paint(false, "Preparando...");
        try
        {
            bool unattended = SupportPresence.Prefs.Unattended && Tech;
            await RustDeskHost.ApplyOptionsAsync(unattended, CancellationToken.None);
            _myId = await RustDeskHost.GetIdAsync(CancellationToken.None);
            _id.Text = _myId is null ? "— — —" : RustDesk.FormatId(_myId);
            if (!unattended) await NewPasswordAsync();
            else { _sessionPassword = ""; _password.Text = "senha fixa"; }
            Paint(_myId is not null, _myId is null ? "Sem ID ainda: tentando de novo sozinho. Se demorar, clique na setinha de atualizar." : "Pronto para receber conexões");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Paint(false, "Erro: " + ex.Message);
        }
    }

    /// <summary>Botão "Atualizar ID": lê o ID de novo (reiniciando o serviço se preciso) e gera uma senha nova para a sessão.</summary>
    private async Task RefreshIdAsync()
    {
        if (_busy || !RustDeskHost.IsInstalled) return;
        _busy = true;
        try
        {
            Paint(false, "Atualizando o ID...");
            _myId = await RustDeskHost.RefreshIdAsync(UiProgress.Text(t => Paint(false, t)), CancellationToken.None);
            _id.Text = _myId is null ? "— — —" : RustDesk.FormatId(_myId);
            if (_myId is not null && _unattended.IsChecked != true) await NewPasswordAsync();
            Paint(_myId is not null, _myId is null
                ? "Ainda sem ID: confira a internet e se o antivírus ou o firewall não está bloqueando o RustDesk, e clique em Atualizar ID de novo."
                : "Pronto para receber conexões");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Paint(false, "Erro: " + ex.Message);
        }
        finally { _busy = false; }
    }

    private void Paint(bool ok, string text)
    {
        _state.Text = "● " + text;
        _state.Foreground = Brush(ok ? "OkBrush" : "WarnBrush");
    }

    private async Task NewPasswordAsync()
    {
        if (_unattended.IsChecked == true) { SetStatus("No acesso sem ninguém a senha é a fixa. Desligue a opção para usar senha por sessão.", "WarnBrush"); return; }
        string pw = RustDesk.NewSessionPassword();
        try
        {
            await RustDeskHost.SetPasswordAsync(pw, CancellationToken.None);
            _sessionPassword = pw;
            _password.Text = "••••-••••";
        }
        catch (InvalidOperationException ex) { SetStatus(ex.Message, "BadBrush"); }
    }

    private void ShowPassword() => _password.Text = _sessionPassword.Length > 0 ? _sessionPassword : _password.Text;

    private void Connect(string id, bool files)
    {
        if (!Tech) { SetStatus("Conectar em outros PCs faz parte do plano Técnico.", "WarnBrush"); return; }
        if (RustDesk.NormalizeId(id) is not { } norm) { SetStatus("Digite o ID de 9 ou 10 números do outro PC.", "WarnBrush"); return; }
        if (norm == _myId) { SetStatus("Esse é o ID deste PC.", "WarnBrush"); return; }
        try
        {
            RustDeskHost.Connect(norm, _targetPassword.Password, files);
            var list = RustDesk.Remember(RustDesk.LoadPeers(PeersFile), norm, null, DateTime.Now);
            RustDesk.SavePeers(PeersFile, list);
            RenderPeers();
            SetStatus(files ? "Abrindo a transferência de arquivos..." : "Conectando... a tela do cliente abre numa janela nova.", "OkBrush");
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            SetStatus("Não foi possível conectar: " + ex.Message, "BadBrush");
        }
    }

    private void RenderPeers()
    {
        _peers.Children.Clear();
        var list = RustDesk.LoadPeers(PeersFile);
        if (list.Count == 0) { _peers.Children.Add(Muted("Os PCs que você acessar aparecem aqui.", 11.5)); return; }
        foreach (var peer in list.Take(6))
        {
            var p = peer;
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var go = SmallButton("Conectar", (_, _) => { _targetId.Text = RustDesk.FormatId(p.Id); Connect(p.Id, false); }, false);
            DockPanel.SetDock(go, Dock.Right);
            row.Children.Add(go);
            var rename = SmallButton("Nome", (_, _) => Rename(p), false);
            DockPanel.SetDock(rename, Dock.Right);
            row.Children.Add(rename);
            var t = new StackPanel();
            t.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeights.SemiBold, FontSize = 13 });
            t.Children.Add(Muted($"{RustDesk.FormatId(p.Id)} · {When(p.LastAccess)}", 11));
            row.Children.Add(t);
            _peers.Children.Add(row);
        }
    }

    private static string When(DateTime t) => t.Date == DateTime.Today ? $"hoje {t:HH:mm}" : t.Date == DateTime.Today.AddDays(-1) ? "ontem" : t.ToString("dd/MM");

    private void Rename(RustDeskPeer peer)
    {
        var dialog = new FormDialog(Window.GetWindow(this), "Nome do PC", "Ex.: Loja Center · PC Caixa", "Salvar");
        var read = dialog.Text("Nome", peer.Name, 40);
        if (dialog.ShowDialog() != true) return;
        try { RustDesk.SavePeers(PeersFile, RustDesk.Remember(RustDesk.LoadPeers(PeersFile), peer.Id, read(), peer.LastAccess)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetStatus(ex.Message, "BadBrush"); }
        RenderPeers();
    }

    private bool _toggling;

    private async Task SetUnattendedAsync(bool on)
    {
        if (_busy || _toggling) return;
        _toggling = true;
        try
        {
            if (on)
            {
                // só liga de verdade se o técnico definir a senha fixa
                if (!Tech || !await FixedPasswordAsync()) { _unattended.IsChecked = false; return; }
                SavePrefs(true);
                await RustDeskHost.ApplyOptionsAsync(true, CancellationToken.None);
                SetStatus("Acesso sem ninguém ligado: use a senha fixa para entrar neste PC.", "OkBrush");
            }
            else
            {
                SavePrefs(false);
                await RustDeskHost.ApplyOptionsAsync(false, CancellationToken.None);
                await NewPasswordAsync();
                SetStatus("Acesso sem ninguém desligado: a senha volta a trocar a cada sessão.", "OkBrush");
            }
        }
        finally { _toggling = false; }
    }

    /// <summary>Pede e grava a senha fixa. Devolve false se cancelou ou se a senha não serve.</summary>
    private async Task<bool> FixedPasswordAsync()
    {
        var dialog = new FormDialog(Window.GetWindow(this), "Senha fixa", "Use pelo menos 8 caracteres, com letras e números. Guarde-a: o MB não mostra depois.", "Salvar");
        var read = dialog.Text("Senha fixa", "", 32);
        if (dialog.ShowDialog() != true) return false;
        string pw = read();
        if (RustDesk.ValidateFixedPassword(pw) is { } problem) { SetStatus(problem, "WarnBrush"); return false; }
        try
        {
            await RustDeskHost.SetPasswordAsync(pw, CancellationToken.None);
            _sessionPassword = "";
            _password.Text = "senha fixa";
            SetStatus("Senha fixa salva.", "OkBrush");
            return true;
        }
        catch (InvalidOperationException ex) { SetStatus(ex.Message, "BadBrush"); return false; }
    }

    // ---------- conexão ativa ----------

    private DateTime _lastIdTry = DateTime.MinValue;

    private void Watch()
    {
        if (!RustDeskHost.IsInstalled) return;
        // sem ID ainda: tenta de novo sozinho a cada 15 s (o botão de atualizar faz o mesmo na hora)
        if (_shown && _myId is null && !_busy && DateTime.Now - _lastIdTry > TimeSpan.FromSeconds(15))
        {
            _lastIdTry = DateTime.Now;
            _ = RefreshIdAsync();
        }
        bool open = RustDeskHost.SessionOpen();
        if (open && !_wasOpen) _openSince = DateTime.Now;
        if (open) _liveText.Text = $"Alguém está vendo e controlando este PC há {(DateTime.Now - _openSince):mm\\:ss}. Se não foi você que pediu, encerre agora.";
        _live.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (_wasOpen && !open && _unattended.IsChecked != true) _ = NewPasswordAsync();   // a senha usada não vale mais
        _wasOpen = open;
    }

    private static void SavePrefs(bool unattended)
    {
        try { (SupportPresence.Prefs with { Unattended = unattended }).Save(SupportPresence.PrefsFile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
