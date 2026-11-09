using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Recording;

namespace MBExtreme.UI.Pages;

/// <summary>Enviar gravações e clipes para o YouTube direto do estúdio (conta conectada pelo Google, envio retomável).</summary>
public sealed partial class StudioPage
{
    private static readonly HttpClient UploadHttp = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly TextBox _ytId = new() { Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly PasswordBox _ytSecret = new() { Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _ytStatus = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private YouTubeToken? _ytToken;
    private bool _ytLoaded, _ytBusy;
    private CancellationTokenSource? _ytUpload;

    private bool YouTubeConnected => !string.IsNullOrEmpty(_s.StudioYtRefreshProtected);

    /// <summary>Parte de Programa › Contas e chaves: Client ID, segredo e o botão para conectar a conta.</summary>
    private void AddYouTubeAccount(Panel target)
    {
        foreach (var box in new FrameworkElement[] { _ytId, _ytSecret, _ytStatus }) Detach(box);
        var head = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        head.Children.Add(new TextBlock { Text = "Enviar vídeos para o YouTube", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 4), VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(Help("Use o mesmo projeto do Google Cloud das estatísticas: 1) em \"APIs e serviços\", ative a YouTube Data API v3; " +
            "2) em \"Tela de consentimento OAuth\", escolha Externo e adicione o seu e-mail como usuário de teste; " +
            "3) em \"Credenciais\", crie um \"ID do cliente OAuth\" do tipo \"App para computador\" e copie o ID e a chave secreta para cá. " +
            "Depois clique em Conectar: o navegador abre, você entra na sua conta e autoriza o envio. O MB Extreme guarda só a autorização, protegida pelo Windows."));
        target.Children.Add(head);

        if (!_ytLoaded)
        {
            _ytLoaded = true;
            _ytId.Text = _s.StudioYtClientId;
            if (SecretStore.Unprotect(_s.StudioYtSecretProtected) is { } secret) _ytSecret.Password = secret;
            _ytId.TextChanged += (_, _) => _s.StudioYtClientId = _ytId.Text.Trim();
            _ytSecret.PasswordChanged += (_, _) =>
            {
                try { _s.StudioYtSecretProtected = _ytSecret.Password.Length > 0 ? SecretStore.Protect(_ytSecret.Password) : null; }
                catch (CryptographicException) { SetStatus("Não foi possível guardar a chave secreta com segurança.", "WarnBrush"); }
            };
        }
        target.Children.Add(Field("ID do cliente OAuth", _ytId, 170));
        target.Children.Add(Field("Chave secreta", _ytSecret, 170));

        var row = new WrapPanel();
        var connect = SmallButton(YouTubeConnected ? "Conectar outra conta" : "Conectar minha conta", async (_, _) => await ConnectYouTubeAsync(), !YouTubeConnected);
        connect.Margin = new Thickness(0, 0, 8, 4);
        row.Children.Add(connect);
        if (YouTubeConnected)
        {
            var disconnect = SmallButton("Desconectar", (_, _) =>
            {
                _s.StudioYtRefreshProtected = null;
                _ytToken = null;
                _s.Save();
                BuildAccounts();
            });
            disconnect.Margin = new Thickness(0, 0, 8, 4);
            row.Children.Add(disconnect);
        }
        target.Children.Add(Field("", row, 170));
        _ytStatus.Text = YouTubeConnected ? "✓ Conta conectada: dá para enviar vídeos em Transmitir › Extras ou pelo botão Enviar na lista de gravações." : "Conta não conectada.";
        _ytStatus.Foreground = Brush(YouTubeConnected ? "OkBrush" : "MutedBrush");
        target.Children.Add(Field("", _ytStatus, 170));
    }

    private async Task ConnectYouTubeAsync()
    {
        if (_ytBusy) return;
        string id = _ytId.Text.Trim(), secret = _ytSecret.Password.Trim();
        if (id.Length == 0 || secret.Length == 0) { _ytStatus.Text = "Preencha o ID do cliente e a chave secreta (veja o \"?\")."; _ytStatus.Foreground = Brush("WarnBrush"); return; }

        _ytBusy = true;
        _ytStatus.Text = "Abrindo o navegador... entre na sua conta do Google e autorize o envio de vídeos.";
        _ytStatus.Foreground = Brush("MutedBrush");
        try
        {
            using var receiver = new YouTubeUpload.LoopbackReceiver();
            var (verifier, challenge) = YouTubeUpload.NewPkce();
            string state = YouTubeUpload.NewState();
            Process.Start(new ProcessStartInfo(YouTubeUpload.AuthUrl(id, receiver.RedirectUri, challenge, state)) { UseShellExecute = true });
            using var wait = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            string target = await receiver.WaitAsync(wait.Token);
            var (code, error) = YouTubeUpload.ParseCallback(target, state);
            if (code is null) throw new InvalidOperationException(error);
            var token = await YouTubeUpload.ExchangeCodeAsync(UploadHttp, id, secret, code, verifier, receiver.RedirectUri, CancellationToken.None);
            if (token.RefreshToken is null) throw new InvalidOperationException("O Google não liberou o acesso permanente. Remova o MB Extreme em myaccount.google.com/permissions e conecte de novo.");
            _s.StudioYtRefreshProtected = SecretStore.Protect(token.RefreshToken);
            _ytToken = token;
            _s.Save();
            BuildAccounts();
            SetStatus("Conta do YouTube conectada.", "OkBrush");
        }
        catch (OperationCanceledException)
        {
            _ytStatus.Text = "O navegador não voltou em 5 minutos. Clique em Conectar de novo.";
            _ytStatus.Foreground = Brush("WarnBrush");
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or Win32Exception or IOException or System.Net.Sockets.SocketException or CryptographicException or System.Text.Json.JsonException)
        {
            _ytStatus.Text = "Não foi possível conectar: " + ex.Message;
            _ytStatus.Foreground = Brush("WarnBrush");
        }
        finally
        {
            _ytBusy = false;
        }
    }

    /// <summary>Token de acesso válido (renova pelo token guardado quando passa de uma hora).</summary>
    private async Task<string> AccessTokenAsync()
    {
        if (_ytToken is { } t && !t.Expired(DateTime.UtcNow)) return t.AccessToken;
        string refresh = SecretStore.Unprotect(_s.StudioYtRefreshProtected) ?? throw new InvalidOperationException("Conecte a conta do YouTube em Programa › Contas e chaves.");
        string secret = SecretStore.Unprotect(_s.StudioYtSecretProtected) ?? throw new InvalidOperationException("Falta a chave secreta do cliente OAuth (Programa › Contas e chaves).");
        _ytToken = await YouTubeUpload.RefreshAsync(UploadHttp, _s.StudioYtClientId, secret, refresh, CancellationToken.None);
        return _ytToken.AccessToken;
    }

    // ---------------------------------------------------------------- janela "Enviar para o YouTube"

    private IReadOnlyList<string> RecentVideos()
    {
        var files = new List<string>();
        try
        {
            foreach (string folder in new[] { StudioController.Folder, StudioController.ClipsFolder })
                if (Directory.Exists(folder))
                    files.AddRange(Directory.GetFiles(folder).Where(f => VideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return files.OrderByDescending(File.GetLastWriteTime).Take(30).ToList();
    }

    private void OpenUploadDialog(string? file = null)
    {
        var panel = new StackPanel();
        if (!YouTubeConnected)
        {
            panel.Children.Add(Muted("Para enviar, conecte a sua conta do YouTube uma vez (leva um minuto).", 12));
            var go = SmallButton("Ir para Contas e chaves", (_, _) => { Window.GetWindow(panel)?.Close(); _selectTab?.Invoke(13); }, true);
            go.HorizontalAlignment = HorizontalAlignment.Left;
            go.Margin = new Thickness(0, 8, 0, 0);
            panel.Children.Add(go);
            ShowEditor("Enviar para o YouTube", panel, null, 480);
            return;
        }
        if (_ytUpload is not null) { SetStatus("Já tem um vídeo sendo enviado. Espere terminar.", "WarnBrush"); return; }

        var videos = RecentVideos().ToList();
        if (file is not null && !videos.Contains(file)) videos.Insert(0, file);
        if (videos.Count == 0) { SetStatus("Nada gravado ainda para enviar.", "WarnBrush"); return; }
        string chosen = file ?? videos[0];

        var title = new TextBox { MaxLength = YouTubeUpload.MaxTitle, Padding = new Thickness(6, 4, 6, 4), Text = Path.GetFileNameWithoutExtension(chosen) };
        var description = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 90, MaxLength = YouTubeUpload.MaxDescription, Padding = new Thickness(6, 4, 6, 4), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        if (_s.StudioChannelName.Length > 0) description.Text = _s.StudioChannelName;
        var list = new PickList(this, 150);
        list.SetItems(videos.Select(v => $"{Path.GetFileName(v)} · {new FileInfo(v).Length / 1_048_576.0:0} MB").ToList(), videos.IndexOf(chosen));
        list.Picked += i => { chosen = videos[i]; title.Text = Path.GetFileNameWithoutExtension(chosen); };
        int privacy = Math.Clamp(_s.StudioYtPrivacy, 0, 2);

        panel.Children.Add(Field("Vídeo", list, 100));
        panel.Children.Add(Field("Título", title, 100));
        panel.Children.Add(Field("Descrição", description, 100));
        panel.Children.Add(Field("Quem vê", Chips(new[] { "Só eu (privado)", "Quem tiver o link", "Todo mundo (público)" }, privacy, i => { privacy = i; _s.StudioYtPrivacy = i; }), 100));
        if (_studio.IsRunning && _studio.Config?.Stream is not null)
            panel.Children.Add(Muted("A live está no ar: enviar agora divide a internet com a transmissão e pode fazer ela travar. O melhor é enviar depois.", 11.5));
        var send = SmallButton("Enviar agora", (_, _) =>
        {
            string video = chosen;
            Window.GetWindow(panel)?.Close();
            _ = UploadAsync(video, YouTubeUpload.CleanTitle(title.Text, Path.GetFileNameWithoutExtension(video)), YouTubeUpload.CleanDescription(description.Text), (YouTubePrivacy)privacy);
        }, true);
        send.HorizontalAlignment = HorizontalAlignment.Left;
        send.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(send);
        panel.Children.Add(Muted("O envio continua com esta janela fechada; o andamento aparece no alto do estúdio. Se a internet cair, ele continua de onde parou.", 11));
        ShowEditor("Enviar para o YouTube", panel, null, 560);
    }

    private async Task UploadAsync(string file, string title, string description, YouTubePrivacy privacy)
    {
        _ytUpload = new CancellationTokenSource();
        var token = _ytUpload.Token;
        string name = Path.GetFileName(file);
        int lastShown = -1;
        var progress = new Progress<double>(p =>
        {
            int percent = (int)(p * 100);
            if (percent == lastShown) return;
            lastShown = percent;
            SetStatus($"Enviando \"{name}\" para o YouTube: {percent}%");
        });
        try
        {
            SetStatus($"Preparando o envio de \"{name}\"...");
            var session = await YouTubeUpload.StartSessionAsync(UploadHttp, await AccessTokenAsync(), file, YouTubeUpload.Metadata(title, description, privacy), token);
            string id = await YouTubeUpload.UploadAsync(UploadHttp, AccessTokenAsync, session, file, progress, token);
            string url = YouTubeUpload.VideoUrl(id);
            bool copied = TryCopy(url);
            SetStatus($"Vídeo enviado: {url}" + (copied ? " (link copiado)." : ".") + " O YouTube ainda processa por alguns minutos antes de aparecer em alta qualidade.", "OkBrush");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Envio cancelado.", "WarnBrush");
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            SetStatus("Não foi possível enviar: " + ex.Message, "BadBrush");
        }
        finally
        {
            _ytUpload.Dispose();
            _ytUpload = null;
        }
    }
}
