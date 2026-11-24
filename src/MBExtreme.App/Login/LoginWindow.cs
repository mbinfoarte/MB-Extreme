using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MBExtreme.Core.Licensing;

namespace MBExtreme.UI.Login;

/// <summary>
/// Janela de login do próprio programa: Google (pelo navegador), e-mail e senha da conta do site, criar conta grátis,
/// confirmar o e-mail com o código de 6 dígitos e "esqueci a senha". No fim ativa o PC e a licença entra na hora.
/// </summary>
internal sealed class LoginWindow : Window
{
    private static readonly Color Yellow = Color.FromRgb(0xFF, 0xC4, 0x00);
    private readonly StackPanel _body = new();
    private readonly TextBlock _error = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x8E)), Visibility = Visibility.Collapsed };
    private CancellationTokenSource? _cts;
    private AccountSession? _session;
    private string _email = "";
    private string _invite = "";

    /// <summary>Deu certo (a licença entrou).</summary>
    public bool Success { get; private set; }

    /// <summary>Clicou em "Pular por enquanto": o programa abre em modo visitante (só analisar, sem corrigir).</summary>
    public bool Skipped { get; private set; }

    private readonly bool _required;

    public LoginWindow(bool signUp = false, bool required = false)
    {
        _required = required;
        Title = "MB Extreme · Entrar";
        Icon = Application.Current?.MainWindow?.Icon ?? TryIcon();
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = true;

        var close = new Button { Content = "✕", Style = (Style)FindResource("GhostButton"), Padding = new Thickness(8, 2, 8, 2), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, ToolTip = required ? "Fechar o MB Extreme" : "Fechar (dá para entrar depois em Conta e licença)" };
        close.Click += (_, _) => Close();
        var root = new StackPanel();
        root.Children.Add(close);
        root.Children.Add(_body);
        var card = new Border
        {
            CornerRadius = new CornerRadius(18), Padding = new Thickness(26, 8, 26, 24), Margin = new Thickness(12),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xC4, 0x00)), BorderThickness = new Thickness(1),
            Background = new LinearGradientBrush(Color.FromRgb(0x18, 0x19, 0x1D), Color.FromRgb(0x0B, 0x0B, 0x0D), 90),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 3, Opacity = 0.6 },
            Child = root,
        };
        TextElement.SetForeground(card, new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF5)));
        card.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is Border or StackPanel or TextBlock) try { DragMove(); } catch (InvalidOperationException) { } };
        Content = card;
        Closed += (_, _) => _cts?.Cancel();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        if (signUp) ShowSignUp(); else ShowSignIn();
    }

    /// <summary>Abre a janela (se ainda não houver conta neste PC). Devolve true quando a licença entrou.</summary>
    /// <summary>
    /// Ao abrir o programa sem conta: só esta janela aparece (o programa fica escondido). Entrou ou criou a conta: o programa abre.
    /// Fechou: o programa fecha. Devolve true quando a licença entrou.
    /// </summary>
    public static bool Gate()
    {
        var w = new LoginWindow(required: true) { ShowInTaskbar = true, Topmost = false };
        w.ShowDialog();
        return w.Success || w.Skipped;
    }

    public static bool Prompt(Window? owner, bool signUp = false)
    {
        var w = new LoginWindow(signUp);
        if (owner is { IsVisible: true }) { w.Owner = owner; w.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        w.ShowDialog();
        return w.Success;
    }

    // ------------------------------------------------------------ peças

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    private void Head(string title, string sub)
    {
        _body.Children.Clear();
        var logo = new Border
        {
            Width = 54, Height = 54, CornerRadius = new CornerRadius(27), Background = new SolidColorBrush(Yellow), HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock { Text = "MB", FontSize = 18, FontWeight = FontWeights.Black, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        _body.Children.Add(logo);
        _body.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 2), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        if (sub.Length > 0) _body.Children.Add(new TextBlock { Text = sub, FontSize = 12, Foreground = Res("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
        _error.Visibility = Visibility.Collapsed;
    }

    private void Error(string text)
    {
        _error.Text = text;
        _error.Visibility = Visibility.Visible;
        if (_error.Parent is null) _body.Children.Add(_error);
    }

    private static TextBox Field(string value = "") => new() { Text = value, FontSize = 13.5, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 4, 0, 10) };

    private static PasswordBox Password() => new() { FontSize = 13.5, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 4, 0, 10) };

    private static TextBlock Label(string text) => new() { Text = text, FontSize = 12, Foreground = Res("MutedBrush") };

    private Button Primary(string text, Func<Task> click)
    {
        var b = new Button { Content = text, Style = (Style)FindResource("PrimaryButton"), Padding = new Thickness(12, 11, 12, 11), FontSize = 14, Margin = new Thickness(0, 6, 0, 10), IsDefault = true };
        b.Click += async (_, _) =>
        {
            b.IsEnabled = false;
            object old = b.Content;
            b.Content = "Aguarde...";
            try { await click(); }
            finally { b.IsEnabled = true; if (b.Content as string == "Aguarde...") b.Content = old; }
        };
        return b;
    }

    private Button GoogleButton()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(new TextBlock { Text = "G", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x42, 0x85, 0xF4)), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = "Continuar com o Google", FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F)), VerticalAlignment = VerticalAlignment.Center });
        var b = new Button
        {
            Content = row, Background = Brushes.White, BorderThickness = new Thickness(0), Padding = new Thickness(12, 10, 12, 10), Cursor = Cursors.Hand,
            Template = Rounded(), Margin = new Thickness(0, 0, 0, 12),
        };
        b.Click += async (_, _) => await GoogleAsync();
        return b;
    }

    private static ControlTemplate Rounded()
    {
        var t = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        border.AppendChild(cp);
        t.VisualTree = border;
        return t;
    }

    private UIElement Divider()
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new Border { Height = 1, Background = Res("BorderBrush"), VerticalAlignment = VerticalAlignment.Center });
        var t = new TextBlock { Text = "  ou com e-mail  ", FontSize = 11, Foreground = Res("MutedBrush") };
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        var r = new Border { Height = 1, Background = Res("BorderBrush"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(r, 2);
        g.Children.Add(r);
        return g;
    }

    /// <summary>"Pular por enquanto": abre o programa sem conta (modo visitante). Fechar no X continua fechando o MB na primeira vez.</summary>
    private UIElement SkipLink() => Link("", "Pular por enquanto", () => { Skipped = true; Close(); });

    private UIElement Link(string before, string link, Action click)
    {
        var t = new TextBlock { FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Res("MutedBrush"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        if (before.Length > 0) t.Inlines.Add(before + " ");
        var h = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(link)) { Foreground = new SolidColorBrush(Yellow), FontWeight = FontWeights.SemiBold, TextDecorations = null };
        h.Click += (_, _) => click();
        t.Inlines.Add(h);
        return t;
    }

    // ------------------------------------------------------------ telas

    private void ShowSignIn()
    {
        Head("Entrar no MB Extreme", "Use a mesma conta do site mbinfoarte.com.br");
        _body.Children.Add(GoogleButton());
        _body.Children.Add(Divider());
        var email = Field(_email);
        var pass = Password();
        _body.Children.Add(Label("E-mail"));
        _body.Children.Add(email);
        _body.Children.Add(Label("Senha"));
        _body.Children.Add(pass);
        var row = new DockPanel { Margin = new Thickness(0, -4, 0, 4) };
        var forgot = Link("", "Esqueci a senha", async () => await ForgotAsync(email.Text));
        DockPanel.SetDock(forgot, Dock.Right);
        row.Children.Add(forgot);
        row.Children.Add(new TextBlock());
        _body.Children.Add(row);
        _body.Children.Add(Primary("Entrar", async () =>
        {
            _email = email.Text.Trim();
            if (_email.Length < 5 || pass.Password.Length == 0) { Error("Digite o e-mail e a senha."); return; }
            try { _session = await AppServices.License.Account.SignInAsync(_email, pass.Password, Token()); }
            catch (AccountException ex) { Error(ex.Message); return; }
            await ActivateAsync();
        }));
        _body.Children.Add(Link("Não tem conta?", "Criar conta grátis", ShowSignUp));
        _body.Children.Add(SkipLink());
        Loaded += (_, _) => (email.Text.Length == 0 ? (Control)email : pass).Focus();
    }

    private void ShowSignUp()
    {
        Head("Criar conta grátis", "Teste o plano Gamer por 3 dias, sem cartão");
        _body.Children.Add(GoogleButton());
        _body.Children.Add(Divider());
        var name = Field();
        var email = Field(_email);
        var pass = Password();
        var invite = Field(_invite);
        invite.CharacterCasing = CharacterCasing.Upper;
        invite.MaxLength = 16;
        var terms = new CheckBox { Content = "Li e aceito os Termos de uso e a Política de privacidade", FontSize = 11.5, Foreground = Res("MutedBrush"), Margin = new Thickness(0, 0, 0, 6) };
        _body.Children.Add(Label("Nome"));
        _body.Children.Add(name);
        _body.Children.Add(Label("E-mail"));
        _body.Children.Add(email);
        _body.Children.Add(Label("Senha (mínimo 8)"));
        _body.Children.Add(pass);
        _body.Children.Add(Label("Código de um amigo (opcional · o teste vira 7 dias)"));
        _body.Children.Add(invite);
        _body.Children.Add(terms);
        _body.Children.Add(Link("", "Ler os termos", () => Open(MBExtreme.Core.AppInfo.SiteUrl + "/termos")));
        _body.Children.Add(Primary("Criar conta e começar", async () =>
        {
            _email = email.Text.Trim();
            _invite = InviteClient.NormalizeCode(invite.Text);
            if (name.Text.Trim().Length < 2) { Error("Digite o seu nome."); return; }
            if (!_email.Contains('@')) { Error("Confira o e-mail."); return; }
            if (pass.Password.Length < 8) { Error("A senha precisa ter pelo menos 8 caracteres."); return; }
            if (terms.IsChecked != true) { Error("Para criar a conta, aceite os Termos e a Privacidade."); return; }
            try { _session = await AppServices.License.Account.SignUpAsync(name.Text, _email, pass.Password, Token()); }
            catch (AccountException ex) { Error(ex.Message); return; }
            await ActivateAsync();
        }));
        _body.Children.Add(Link("Já tem conta?", "Entrar", ShowSignIn));
        _body.Children.Add(SkipLink());
    }

    private void ShowVerify()
    {
        Head("Confirme o seu e-mail", $"Mandamos um código de 6 números para {_email}. Olhe também o spam.");
        var code = Field();
        code.FontSize = 22;
        code.MaxLength = 6;
        code.TextAlignment = TextAlignment.Center;
        _body.Children.Add(code);
        _body.Children.Add(Primary("Confirmar e ativar", async () =>
        {
            if (_session is null) { ShowSignIn(); return; }
            try
            {
                await AppServices.License.Account.VerifyEmailCodeAsync(_session, code.Text, Token());
                _session = await AppServices.License.Account.RefreshAsync(_session, Token());
            }
            catch (AccountException ex) { Error(ex.Message); return; }
            await ActivateAsync();
        }));
        _body.Children.Add(Link("Não chegou?", "Mandar de novo", async () =>
        {
            if (_session is null) return;
            try { await AppServices.License.Account.SendEmailCodeAsync(_session, Token()); Error("Código enviado de novo."); }
            catch (AccountException ex) { Error(ex.Message); }
        }));
        Loaded += (_, _) => code.Focus();
        code.Focus();
    }

    private void ShowDone(string message)
    {
        Head("Tudo certo!", "");
        _body.Children.Add(new Border
        {
            Width = 50, Height = 50, CornerRadius = new CornerRadius(25), BorderThickness = new Thickness(2), BorderBrush = Res("OkBrush"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 12),
            Child = new TextBlock { Text = "✓", FontSize = 22, Foreground = Res("OkBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        _body.Children.Add(new Border
        {
            Background = Res("Surface2Brush"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 6),
            Child = new TextBlock { Text = message + "\nEste PC já está ativado na sua conta.", FontSize = 13, TextWrapping = TextWrapping.Wrap },
        });
        _body.Children.Add(Primary("Abrir o MB Extreme", () => { Close(); return Task.CompletedTask; }));
    }

    // ------------------------------------------------------------ ações

    private CancellationToken Token()
    {
        _cts ??= new CancellationTokenSource();
        return _cts.Token;
    }

    private async Task ActivateAsync()
    {
        if (_session is null) return;
        var (ok, message, needVerify) = await AppServices.License.LoginWithAccountAsync(_session, _invite.Length >= 4 ? _invite : null, Token());
        if (ok) { Success = true; ShowDone(message.Replace("Ativado: ", "")); return; }
        if (needVerify)
        {
            try { await AppServices.License.Account.SendEmailCodeAsync(_session, Token()); }
            catch (AccountException ex) { ShowVerify(); Error(ex.Message); return; }
            ShowVerify();
            return;
        }
        Error(message);
    }

    private async Task ForgotAsync(string email)
    {
        if (!email.Contains('@')) { Error("Digite o seu e-mail acima e clique de novo em Esqueci a senha."); return; }
        try
        {
            await AppServices.License.Account.SendPasswordResetAsync(email, Token());
            Error("Pronto: mandamos um link para criar uma senha nova. Olhe o seu e-mail (e o spam).");
            _error.Foreground = Res("OkBrush");
        }
        catch (AccountException ex) { Error(ex.Message); }
    }

    /// <summary>Google: abre o navegador para escolher a conta e aprovar este PC; a janela fecha sozinha quando der certo.</summary>
    private async Task GoogleAsync()
    {
        Head("Continue no navegador", "Abrimos o site no seu navegador. Entre com o Google e clique em Aprovar. Esta janela segue sozinha quando der certo.");
        var code = new TextBlock { FontSize = 26, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Yellow), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
        _body.Children.Add(code);
        _body.Children.Add(new TextBlock { Text = "o mesmo código aparece no site", FontSize = 11, Foreground = Res("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) });
        string? url = null;
        var reopen = new Button { Content = "Abrir o navegador de novo", Style = (Style)FindResource("GhostButton"), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 8) };
        reopen.Click += (_, _) => { if (url is not null) Open(url); };
        _body.Children.Add(reopen);
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _body.Children.Add(Link("", "Voltar", () => { _cts?.Cancel(); _cts = null; ShowSignIn(); }));
        var (ok, message) = await AppServices.License.LoginAsync(c => Dispatcher.BeginInvoke(new Action(() => { code.Text = c.UserCode; url = c.VerificationUri; })), token);
        if (token.IsCancellationRequested) return;
        if (ok) { Success = true; ShowDone(message.Replace("Ativado: ", "")); }
        else { ShowSignIn(); Error(message); }
    }

    private static ImageSource? TryIcon()
    {
        try { return System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico")); }
        catch (Exception ex) when (ex is System.IO.IOException or UriFormatException or NotSupportedException or InvalidOperationException) { return null; }
    }

    private static void Open(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
