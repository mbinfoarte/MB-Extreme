using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Apps;
using MBExtreme.Core.Install;
using MBExtreme.Core.Storage;
using MBExtreme.Core.Windows;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Criador de ISO personalizada (plano Técnico): 1. ISO oficial de origem, 2. personalizar (instalação, limpeza, depois de instalar),
/// 3. gerar o .iso ou gravar no pendrive. À direita, os modelos salvos e as regras. O trabalho todo é feito numa cópia.
/// </summary>
public sealed class CreateIsoPage : ToolPageBase
{
    private string? _iso;
    private IReadOnlyList<WimEdition> _editions = Array.Empty<WimEdition>();
    private readonly TextBlock _isoName = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _isoInfo = new() { FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _edition = new() { MinWidth = 220, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBox _account = new() { Text = "Técnico", Width = 160, Margin = new Thickness(0, 4, 0, 0) };
    private readonly ComboBox _kit = new() { MinWidth = 160, Margin = new Thickness(0, 4, 0, 0) };
    private readonly CheckBox _unattended, _local, _bypass, _apps, _xbox, _telemetry, _copilot, _store, _drivers, _mb, _kitBox, _gamer, _post;
    private readonly RadioButton _toIso = new() { GroupName = "iso-target", IsChecked = true, Margin = new Thickness(0, 4, 0, 4) };
    private readonly RadioButton _toUsb = new() { GroupName = "iso-target", Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBlock _outText = new() { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ComboBox _usb = new() { MinWidth = 220, Margin = new Thickness(22, 2, 0, 0) };
    private readonly ProgressBar _bar = new() { Height = 8, Maximum = 100, Margin = new Thickness(0, 8, 0, 4), Visibility = Visibility.Collapsed };
    private readonly TextBlock _barText = new() { FontSize = 11.5, Visibility = Visibility.Collapsed };
    private readonly Button _generate = CompactPick.Button("Gerar ISO personalizada", primary: true);
    private readonly Button _cancel = CompactPick.Button("Cancelar");
    private readonly StackPanel _templates = new();
    private readonly TextBlock _driverInfo = new() { FontSize = 11 };
    private string? _output;
    private string? _driversFolder;
    private CancellationTokenSource? _cts;
    private bool _busy;
    private static string TemplateFile => IsoBuilder.TemplatesFile(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    public CreateIsoPage() : base("Criador de ISO personalizada", "Plano Técnico · parte da ISO oficial da Microsoft e gera a sua versão pronta para formatar.")
    {
        _unattended = TileCheck("Instalação automática", "pula as telas de idioma, região e contrato");
        _local = TileCheck("Conta local", "sem exigir conta Microsoft");
        _bypass = TileCheck("Pular verificação de TPM/CPU", "para PCs antigos: o Windows 11 avisa que não tem suporte");
        _apps = TileCheck("Remover apps que vêm junto", "Clipchamp, Notícias, Clima, Solitaire, Teams…");
        _xbox = TileCheck("Remover também o Xbox", "app Xbox e Game Bar (deixe para quem joga)");
        _telemetry = TileCheck("Desligar telemetria e anúncios", "os mesmos ajustes da aba Otimizações");
        _copilot = TileCheck("Copilot e Recall desligados", "");
        _store = TileCheck("Manter Microsoft Store", "desmarque só para PCs de empresa");
        _drivers = TileCheck("Drivers deste PC", "do Backup para formatar");
        _mb = TileCheck("Instalar o MB Extreme", "baixa e abre o instalador no primeiro login");
        _kitBox = TileCheck("Kit de programas", "instalado pelo winget no primeiro login");
        _gamer = TileCheck("Aplicar perfil Gamer", "lembra de aplicar depois da Pós-formatação");
        _post = TileCheck("Pós-formatação automática", "o MB abre a Pós-formatação no primeiro uso");
        _unattended.IsChecked = _local.IsChecked = _apps.IsChecked = _telemetry.IsChecked = _copilot.IsChecked = _store.IsChecked = _drivers.IsChecked = _mb.IsChecked = _kitBox.IsChecked = true;
        foreach (var k in AppCatalog.Kits) _kit.Items.Add(new ComboBoxItem { Content = k.Title, Tag = k.Key });
        _kit.SelectedIndex = 0;

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        var left = new StackPanel();
        left.Children.Add(SourceCard());
        left.Children.Add(CustomizeCard());
        left.Children.Add(GenerateCard());
        layout.Children.Add(left);
        var right = new StackPanel();
        var tpl = new StackPanel();
        tpl.Children.Add(new TextBlock { Text = "Modelos salvos", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        tpl.Children.Add(_templates);
        right.Children.Add(CardOf(tpl));
        var rules = new StackPanel();
        rules.Children.Add(new TextBlock { Text = "🛡  Regras", FontSize = 13, FontWeight = FontWeights.SemiBold });
        rules.Children.Add(Muted("Usa sempre uma ISO oficial. Não ativa o Windows: o PC usa a própria licença (a chave fica salva no Backup para formatar). Tudo é feito numa cópia; a ISO original não é alterada.", 11.5));
        rules.Children.Add(Muted("A tela de escolher o disco continua aparecendo: escolha você onde instalar, para não apagar o disco errado.", 11.5));
        right.Children.Add(CardOf(rules));
        Grid.SetColumn(right, 2);
        layout.Children.Add(right);
        Body.Children.Add(layout);

        _generate.Click += async (_, _) => await GenerateAsync();
        _cancel.Click += (_, _) => _cts?.Cancel();
        _cancel.Visibility = Visibility.Collapsed;
        Loaded += (_, _) => { RenderTemplates(); FindDrivers(); FindUsb(); };
    }

    // ---------- passos ----------

    private FrameworkElement StepHead(string number, string title)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)),
            Child = new TextBlock { Text = number, Foreground = Brushes.Black, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        row.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private Border SourceCard()
    {
        var s = new StackPanel();
        s.Children.Add(StepHead("1", "ISO de origem"));
        _isoName.Text = "Nenhuma ISO escolhida";
        _isoInfo.Text = "Use a ISO oficial do Windows 10 ou 11 (site da Microsoft).";
        _isoInfo.Foreground = Brush("MutedBrush");
        s.Children.Add(_isoName);
        s.Children.Add(_isoInfo);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-12, 8, 0, 0) };
        buttons.Children.Add(SmallButton("Escolher ISO…", async (_, _) => await ChooseIsoAsync(), true));
        buttons.Children.Add(SmallButton("Baixar a ISO oficial (site da Microsoft)", (_, _) => Open(IsoBuilder.MicrosoftDownloadPage)));
        s.Children.Add(buttons);
        return CardOf(s);
    }

    private Border CustomizeCard()
    {
        var s = new StackPanel();
        s.Children.Add(StepHead("2", "Personalizar"));
        var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        top.Children.Add(Labeled("Edição", _edition));
        top.Children.Add(Labeled("Nome da conta local", _account));
        top.Children.Add(Labeled("Kit de programas", _kit));
        s.Children.Add(top);
        s.Children.Add(Group("INSTALAÇÃO", _unattended, _local, _bypass));
        s.Children.Add(Group("LIMPEZA E PRIVACIDADE", _apps, _xbox, _telemetry, _copilot, _store));
        s.Children.Add(Group("DEPOIS DE INSTALAR", _drivers, _mb, _kitBox, _gamer, _post));
        _driverInfo.Foreground = Brush("MutedBrush");
        s.Children.Add(_driverInfo);
        _bypass.Checked += (_, _) => SetStatus("Sem TPM 2.0/CPU compatível o Windows 11 instala, mas a Microsoft pode não mandar atualizações. Use só em PCs antigos.", "WarnBrush");
        return CardOf(s);
    }

    private FrameworkElement Labeled(string label, FrameworkElement control)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 16, 4) };
        p.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = Brush("MutedBrush") });
        p.Children.Add(control);
        return p;
    }

    private FrameworkElement Group(string title, params CheckBox[] boxes)
    {
        var p = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        p.Children.Add(new TextBlock { Text = title, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 0, 0, 4) });
        p.Children.Add(TileGrid(boxes.Select(b => (UIElement)TileOf(b)), 3, 230));
        return p;
    }

    private Border GenerateCard()
    {
        var s = new StackPanel();
        s.Children.Add(StepHead("3", "Gerar"));
        _toIso.Content = "Salvar arquivo .iso";
        _toUsb.Content = "Gravar no pendrive (apaga o pendrive)";
        _toIso.Foreground = _toUsb.Foreground = Brush("TextBrush");
        s.Children.Add(_toIso);
        var outRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 0, 0, 0) };
        _outText.Text = "Escolha onde salvar";
        _outText.Foreground = Brush("MutedBrush");
        _outText.MaxWidth = 420;
        outRow.Children.Add(_outText);
        outRow.Children.Add(SmallButton("Salvar como…", (_, _) => ChooseOutput()));
        s.Children.Add(outRow);
        s.Children.Add(_toUsb);
        s.Children.Add(_usb);
        s.Children.Add(_bar);
        _barText.Foreground = Brush("MutedBrush");
        s.Children.Add(_barText);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        _generate.MinHeight = 36;
        buttons.Children.Add(_generate);
        _cancel.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(_cancel);
        var save = SmallButton("Salvar como modelo", (_, _) => SaveTemplate());
        buttons.Children.Add(save);
        s.Children.Add(buttons);
        return CardOf(s);
    }

    // ---------- ações ----------

    private async Task ChooseIsoAsync()
    {
        if (_busy) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "ISO oficial do Windows", Filter = "Imagem ISO (*.iso)|*.iso" };
        if (dialog.ShowDialog() != true) return;
        _busy = true;
        _iso = dialog.FileName;
        _isoName.Text = _iso;
        _isoInfo.Text = "Lendo as edições e conferindo o SHA-256…";
        try
        {
            string iso = _iso;
            var (editions, hash) = await Task.Run(() => (IsoJob.ReadEditions(iso), IsoBuilder.Sha256(iso)));
            _editions = editions;
            _edition.Items.Clear();
            foreach (var e in editions) _edition.Items.Add(new ComboBoxItem { Content = e.Name, Tag = e.Index });
            var pick = IsoBuilder.Pick(editions, "Windows 11 Pro");
            _edition.SelectedIndex = pick is null ? -1 : editions.ToList().IndexOf(pick);
            long size = new FileInfo(iso).Length;
            _isoInfo.Text = $"{DiskText.Size(size)} · {editions.Count} edição(ões) · SHA-256 {hash[..16]}…  (confira no site da Microsoft se quiser)";
            _isoInfo.Foreground = Brush(editions.Count > 0 ? "OkBrush" : "WarnBrush");
            if (_output is null && pick is not null)
            {
                _output = Path.Combine(Path.GetDirectoryName(iso) ?? "C:\\", IsoBuilder.DefaultOutputName(pick.Name));
                _outText.Text = _output;
            }
            if (editions.Count == 0) SetStatus("Essa ISO não tem edições do Windows. Use a ISO oficial.", "WarnBrush");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _isoInfo.Text = "Não foi possível ler a ISO: " + ex.Message;
            _isoInfo.Foreground = Brush("BadBrush");
        }
        finally { _busy = false; }
    }

    private void ChooseOutput()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Salvar a ISO personalizada", Filter = "Imagem ISO (*.iso)|*.iso", FileName = Path.GetFileName(_output ?? "MB-Windows.iso") };
        if (dialog.ShowDialog() != true) return;
        _output = dialog.FileName;
        _outText.Text = _output;
        _toIso.IsChecked = true;
    }

    private void FindDrivers()
    {
        var backup = FormatBackup.Find(FormatBackup.ReadyRoots()).FirstOrDefault();
        _driversFolder = backup is null ? null : Path.Combine(backup.Folder, "drivers");
        _driverInfo.Text = backup is null
            ? "Nenhum Backup para formatar encontrado: faça um na aba Backup para formatar para pôr os drivers deste PC na ISO."
            : $"Drivers: backup de {backup.Computer} ({backup.Created:dd/MM/yyyy}, {backup.Drivers} drivers).";
        if (backup is null) _drivers.IsChecked = false;
    }

    private void FindUsb()
    {
        _usb.Items.Clear();
        foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable))
        {
            try
            {
                if (!d.IsReady) continue;
                _usb.Items.Add(new ComboBoxItem { Content = $"{d.VolumeLabel} {DiskText.Size(d.TotalSize)} ({d.Name.TrimEnd('\\')}) · apaga o pendrive", Tag = d.Name[0] });
            }
            catch (IOException) { }
        }
        if (_usb.Items.Count == 0) _usb.Items.Add(new ComboBoxItem { Content = "Nenhum pendrive conectado", IsEnabled = false });
        _usb.SelectedIndex = 0;
    }

    private IsoOptions Options() => new()
    {
        EditionName = (_edition.SelectedItem as ComboBoxItem)?.Content as string ?? "Windows 11 Pro",
        Unattended = _unattended.IsChecked == true,
        LocalAccount = _local.IsChecked == true ? _account.Text.Trim() : "Técnico",
        BypassChecks = _bypass.IsChecked == true,
        RemoveApps = _apps.IsChecked == true,
        RemoveXbox = _xbox.IsChecked == true,
        DisableTelemetry = _telemetry.IsChecked == true,
        DisableCopilot = _copilot.IsChecked == true,
        KeepStore = _store.IsChecked == true,
        AddDrivers = _drivers.IsChecked == true,
        InstallMbExtreme = _mb.IsChecked == true,
        Kit = _kitBox.IsChecked == true ? (_kit.SelectedItem as ComboBoxItem)?.Tag as string ?? "" : "",
        GamerProfile = _gamer.IsChecked == true,
        AutoPostInstall = _post.IsChecked == true,
    };

    private void Apply(IsoOptions o)
    {
        _unattended.IsChecked = o.Unattended; _bypass.IsChecked = o.BypassChecks; _apps.IsChecked = o.RemoveApps; _xbox.IsChecked = o.RemoveXbox;
        _telemetry.IsChecked = o.DisableTelemetry; _copilot.IsChecked = o.DisableCopilot; _store.IsChecked = o.KeepStore; _drivers.IsChecked = o.AddDrivers && _driversFolder is not null;
        _mb.IsChecked = o.InstallMbExtreme; _kitBox.IsChecked = o.Kit.Length > 0; _gamer.IsChecked = o.GamerProfile; _post.IsChecked = o.AutoPostInstall;
        _account.Text = o.LocalAccount;
        for (int i = 0; i < _kit.Items.Count; i++) if ((_kit.Items[i] as ComboBoxItem)?.Tag as string == o.Kit) _kit.SelectedIndex = i;
        for (int i = 0; i < _edition.Items.Count; i++) if (string.Equals((_edition.Items[i] as ComboBoxItem)?.Content as string, o.EditionName, StringComparison.OrdinalIgnoreCase)) _edition.SelectedIndex = i;
    }

    private void SaveTemplate()
    {
        var o = Options();
        var dialog = new FormDialog(Window.GetWindow(this), "Salvar como modelo", "Guarde estas opções para repetir em outros PCs.", "Salvar");
        var read = dialog.Text("Nome do modelo (ex.: Cliente gamer)", _currentTemplate ?? IsoBuilder.Summary(o), 40);
        if (dialog.ShowDialog() != true) return;
        string name = read();
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            IsoBuilder.WriteTemplates(TemplateFile, IsoBuilder.SaveTemplate(IsoBuilder.LoadTemplates(TemplateFile), new IsoTemplate(name.Trim(), o)));
            RenderTemplates();
            SetStatus($"Modelo \"{name.Trim()}\" salvo.", "OkBrush");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetStatus("Não foi possível salvar o modelo: " + ex.Message, "BadBrush"); }
    }

    private void RenderTemplates()
    {
        _templates.Children.Clear();
        var list = IsoBuilder.LoadTemplates(TemplateFile);
        if (list.Count == 0) { _templates.Children.Add(Muted("Marque as opções e clique em \"Salvar como modelo\" para repetir em outros PCs.", 11.5)); return; }
        foreach (var t in list)
        {
            var template = t;
            var b = new Button { Style = (Style)FindResource("GhostButton"), HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(10, 6, 10, 6) };
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = t.Name, FontWeight = FontWeights.SemiBold, FontSize = 12.5 });
            text.Children.Add(new TextBlock { Text = IsoBuilder.Summary(t.Options) + (t.Uses > 0 ? $" · usado {t.Uses}x" : ""), FontSize = 11, Foreground = Brush("MutedBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
            b.Content = text;
            b.Click += (_, _) => { Apply(template.Options); _currentTemplate = template.Name; SetStatus($"Modelo \"{template.Name}\" carregado.", "OkBrush"); };
            _templates.Children.Add(b);
        }
    }

    private string? _currentTemplate;

    private static string? FindOscdimg() =>
        IsoBuilder.OscdimgCandidates(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), AppContext.BaseDirectory).FirstOrDefault(File.Exists);

    private async Task<bool> InstallAdkAsync()
    {
        if (MessageBox.Show("Para gerar o arquivo .iso o MB usa o oscdimg, ferramenta gratuita da Microsoft (Windows ADK › Ferramentas de Implantação, ~100 MB).\n\nInstalar agora pelo winget?\n\nSe preferir, escolha \"Gravar no pendrive\", que não precisa dela.",
                "Ferramenta da Microsoft", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return false;
        SetStatus("Instalando as Ferramentas de Implantação do Windows ADK…");
        var info = new ProcessStartInfo("winget.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in IsoBuilder.AdkInstallArgs()) info.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(info);
            if (p is not null) await p.WaitForExitAsync();
        }
        catch (System.ComponentModel.Win32Exception) { SetStatus("O winget não está disponível neste Windows. Instale o Windows ADK pelo site da Microsoft.", "BadBrush"); return false; }
        return FindOscdimg() is not null;
    }

    private async Task GenerateAsync()
    {
        if (_busy) return;
        if (_iso is null || _editions.Count == 0) { SetStatus("Escolha a ISO oficial primeiro (passo 1).", "WarnBrush"); return; }
        var o = Options();
        if (o.Unattended && IsoBuilder.ValidateAccount(o.LocalAccount) is { } bad) { SetStatus(bad, "WarnBrush"); return; }
        bool usb = _toUsb.IsChecked == true;
        char usbLetter = (_usb.SelectedItem as ComboBoxItem)?.Tag is char c ? c : '\0';
        if (usb && usbLetter == '\0') { SetStatus("Conecte um pendrive de 8 GB ou mais.", "WarnBrush"); return; }
        if (!usb && _output is null) { SetStatus("Escolha onde salvar a ISO.", "WarnBrush"); return; }
        string? oscdimg = usb ? "" : FindOscdimg();
        if (oscdimg is null && !(await InstallAdkAsync() && (oscdimg = FindOscdimg()) is not null)) return;

        string baseDir = usb ? Path.GetDirectoryName(_iso)! : Path.GetDirectoryName(_output!)!;
        string work = Path.Combine(baseDir, IsoJob.WorkFolderName);
        long need = IsoBuilder.WorkSpaceNeeded(new FileInfo(_iso).Length);
        long free = new DriveInfo(Path.GetPathRoot(work)!).AvailableFreeSpace;
        if (free < need) { SetStatus($"Pouco espaço em {Path.GetPathRoot(work)}: precisa de {DiskText.Size(need)} livres para trabalhar (tem {DiskText.Size(free)}).", "WarnBrush"); return; }
        if (usb && MessageBox.Show($"Tudo o que está no pendrive {usbLetter}: será apagado. Continuar?", "Gravar no pendrive", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var request = new IsoRequest(_iso, o, usb ? IsoTarget.UsbDrive : IsoTarget.IsoFile, _output ?? "", usbLetter, work, o.AddDrivers ? _driversFolder : null);
        _busy = true;
        _cts = new CancellationTokenSource();
        _generate.IsEnabled = false;
        _cancel.Visibility = _bar.Visibility = _barText.Visibility = Visibility.Visible;
        var started = DateTime.Now;
        var progress = new Progress<IsoProgress>(p =>
        {
            _bar.Value = (p.Step - 1) * 100.0 / p.Total + 100.0 / p.Total / 2;
            _barText.Text = $"Etapa {p.Step} de {p.Total} · {p.Text} · {(DateTime.Now - started):mm\\:ss}";
        });
        try
        {
            string tool = oscdimg ?? "";
            await Task.Run(() => IsoJob.Run(request, tool, progress, _cts.Token));
            _bar.Value = 100;
            if (_currentTemplate is not null) IsoBuilder.WriteTemplates(TemplateFile, IsoBuilder.CountUse(IsoBuilder.LoadTemplates(TemplateFile), _currentTemplate));
            RenderTemplates();
            _barText.Text = usb ? "Pendrive pronto: dê boot por ele no PC a formatar." : "ISO pronta: " + _output;
            SetStatus(usb ? "Pronto! O pendrive já dá boot (UEFI)." : "Pronto! Use a ISO no Ventoy, no Rufus ou na aba Reinstalar sem pendrive.", "OkBrush");
        }
        catch (OperationCanceledException) { _barText.Text = "Cancelado. A ISO original não foi alterada."; SetStatus("Cancelado.", "WarnBrush"); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            _barText.Text = "Parou com erro.";
            SetStatus("Não foi possível gerar: " + ex.Message, "BadBrush");
        }
        finally
        {
            _busy = false;
            _generate.IsEnabled = true;
            _cancel.Visibility = Visibility.Collapsed;
        }
    }
}
