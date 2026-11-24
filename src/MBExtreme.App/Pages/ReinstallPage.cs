using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Install;
using MBExtreme.Core.Storage;
using MBExtreme.Core.Windows;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Reinstalar o Windows sem pendrive (plano Técnico), em 5 passos: ISO, diagnóstico (só leitura), backup, preparar o instalador
/// (partição MB-SETUP + entrada de boot de uma vez só, com Cancelar) e, depois de instalar, remover a partição.
/// </summary>
public sealed class ReinstallPage : ToolPageBase
{
    private string? _iso;
    private ReinstallFacts? _facts;
    private FormatBackupInfo? _backup;
    private ReinstallState? _state;
    private readonly TextBlock _isoText = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _checks = new();
    private readonly TextBlock _backupText = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _copied = new() { Content = "Já copiei meus arquivos pessoais para fora do C:", Margin = new Thickness(0, 8, 0, 6) };
    private readonly TextBlock _prepText = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly Button _prepare = CompactPick.Button("Preparar o instalador", primary: true);
    private readonly Button _cancelPrep = CompactPick.Button("Cancelar a preparação");
    private readonly Button _restart = CompactPick.Button("Reiniciar e abrir o instalador", primary: true);
    private readonly Button _remove = CompactPick.Button("Remover a partição MB-SETUP");
    private readonly TextBlock _afterText = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly Border[] _badges = new Border[5];
    private bool _busy;

    public ReinstallPage() : base("Reinstalar o Windows sem pendrive", "Plano Técnico · usa uma partição auxiliar no próprio disco e abre o instalador no próximo reinício.")
    {
        _copied.Foreground = Brush("TextBrush");
        _copied.Checked += (_, _) => UpdatePrepare();
        _copied.Unchecked += (_, _) => UpdatePrepare();
        Body.Children.Add(Step(0, "Escolha a ISO", "Uma ISO do Windows 10 ou 11 neste PC: a oficial ou a que você criou na aba Criar ISO. O MB só lê a ISO para conferir o que tem dentro.",
            _isoText, Buttons(SmallButton("Escolher ISO…", async (_, _) => await ChooseIsoAsync(), true))));
        Body.Children.Add(Step(1, "Diagnóstico do PC", "Só leitura: disco, modo de boot (UEFI/GPT), BitLocker, energia na tomada e espaço livre. Nada é alterado.",
            _checks, Buttons(SmallButton("Verificar de novo", async (_, _) => await DiagnoseAsync()))));
        Body.Children.Add(Step(2, "Backup de segurança", "Salva drivers, redes Wi-Fi e a chave do Windows na partição auxiliar. Seus arquivos pessoais NÃO entram: copie Documentos, Fotos e Área de Trabalho para outro disco ou para a nuvem.",
            Stack(_copied, _backupText), Buttons(SmallButton("Salvar drivers, Wi-Fi e chave", async (_, _) => await BackupAsync(), true))));
        _prepare.Click += async (_, _) => await PrepareAsync();
        _cancelPrep.Click += async (_, _) => await CancelAsync();
        _restart.Click += (_, _) => RestartNow();
        _remove.Click += async (_, _) => await RemoveAsync();
        Body.Children.Add(Step(3, "Preparar o instalador", "Reduz o C: em 8 GB, cria a partição MB-SETUP, copia o instalador e programa a abertura dele no próximo reinício. Nada é apagado nesta etapa; Cancelar desfaz tudo.",
            _prepText, Buttons(_prepare, _cancelPrep, _restart)));
        Body.Children.Add(Step(4, "Depois da instalação", "A partição MB-SETUP continua ocupando espaço. Abra o MB Extreme no Windows novo: ele devolve os drivers e o Wi-Fi salvos e apaga a partição, devolvendo o espaço ao C:.",
            _afterText, Buttons(_remove)));

        var warn = new StackPanel();
        warn.Children.Add(new TextBlock { Text = "⚠  Antes de reiniciar", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brush("WarnBrush") });
        warn.Children.Add(Muted("Na tela do instalador, formatar o C: apaga tudo que está nele. Ponto de restauração e Central Desfazer não voltam uma formatação. Não apague a partição MB-SETUP no instalador: é dela que ele lê os arquivos.", 11.5));
        Body.Children.Add(CardOf(warn));

        Loaded += async (_, _) => await RefreshAsync();
    }

    private static StackPanel Stack(params UIElement[] items)
    {
        var p = new StackPanel();
        foreach (var i in items) p.Children.Add(i);
        return p;
    }

    private static StackPanel Buttons(params Button[] buttons)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0);
            p.Children.Add(buttons[i]);
        }
        return p;
    }

    private Border Step(int index, string title, string text, UIElement content, UIElement actions)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var badge = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), VerticalAlignment = VerticalAlignment.Top };
        _badges[index] = badge;
        Paint(index, false);
        grid.Children.Add(badge);
        var s = new StackPanel();
        s.Children.Add(new TextBlock { Text = $"{index + 1}. {title}", FontSize = 15, FontWeight = FontWeights.SemiBold });
        s.Children.Add(Muted(text, 11.5));
        if (content is FrameworkElement fe) fe.Margin = new Thickness(0, 6, 0, 0);
        s.Children.Add(content);
        s.Children.Add(actions);
        Grid.SetColumn(s, 1);
        grid.Children.Add(s);
        return CardOf(grid);
    }

    private void Paint(int index, bool done)
    {
        var yellow = Color.FromRgb(0xFF, 0xC4, 0x00);
        _badges[index].Background = done ? new SolidColorBrush(yellow) : Brush("Surface2Brush");
        _badges[index].BorderBrush = new SolidColorBrush(yellow);
        _badges[index].BorderThickness = new Thickness(done ? 0 : 1.5);
        _badges[index].Child = new TextBlock
        {
            Text = done ? "✓" : (index + 1).ToString(), FontWeight = FontWeights.Bold, Foreground = done ? Brushes.Black : Brush("TextBrush"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private async Task RefreshAsync()
    {
        _state = Reinstall.LoadState();
        bool leftover = _state is null && await Task.Run(Reinstall.HasSetupPartition);
        _afterText.Text = leftover ? "A partição MB-SETUP foi encontrada neste disco. Clique para devolver os drivers e o espaço ao C:." : "Nenhuma partição MB-SETUP neste PC.";
        _afterText.Foreground = Brush(leftover ? "WarnBrush" : "MutedBrush");
        _remove.IsEnabled = leftover;
        Paint(4, false);
        if (_isoText.Text.Length == 0) { _isoText.Text = "Nenhuma ISO escolhida."; _isoText.Foreground = Brush("MutedBrush"); }
        UpdatePrepare();
        if (_facts is null) await DiagnoseAsync();
    }

    private void UpdatePrepare()
    {
        bool prepared = _state is not null;
        Paint(3, prepared);
        _cancelPrep.IsEnabled = prepared;
        _restart.IsEnabled = prepared;
        bool ready = _iso is not null && _facts is not null && _backup is not null && _copied.IsChecked == true &&
                     Reinstall.CanPrepare(Reinstall.Evaluate(_facts, Reinstall.PartitionBytes(new FileInfo(_iso).Length)));
        _prepare.IsEnabled = !prepared && ready && !_busy;
        _prepText.Text = prepared
            ? $"Pronto desde {_state!.Created:dd/MM HH:mm}: partição {_state.Letter}: ({_state.Bytes / Reinstall.Gb} GB). No próximo reinício abre o instalador; depois o PC volta a ligar no Windows normal se você não instalar."
            : ready ? "Tudo certo para preparar." : "Faça os passos 1 a 3 (e marque que copiou seus arquivos) para liberar.";
        _prepText.Foreground = Brush(prepared ? "OkBrush" : "MutedBrush");
    }

    private async Task ChooseIsoAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "ISO do Windows", Filter = "Imagem ISO (*.iso)|*.iso" };
        if (dialog.ShowDialog() != true || _busy) return;
        _busy = true;
        string iso = dialog.FileName;
        _isoText.Text = "Conferindo a ISO…";
        try
        {
            var (editions, hash) = await Task.Run(() => (IsoJob.ReadEditions(iso), IsoBuilder.Sha256(iso)));
            if (editions.Count == 0) throw new InvalidOperationException("A ISO não tem edições do Windows.");
            _iso = iso;
            _isoText.Text = $"{Path.GetFileName(iso)}\n{string.Join(", ", editions.Select(e => e.Name).Take(3))}{(editions.Count > 3 ? "…" : "")} · SHA-256 {hash[..16]}…";
            _isoText.Foreground = Brush("OkBrush");
            Paint(0, true);
            if (_facts is not null) ShowChecks();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _iso = null;
            _isoText.Text = "Não foi possível usar a ISO: " + ex.Message;
            _isoText.Foreground = Brush("BadBrush");
            Paint(0, false);
        }
        finally { _busy = false; UpdatePrepare(); }
    }

    private async Task DiagnoseAsync()
    {
        _checks.Children.Clear();
        _checks.Children.Add(Muted("Lendo…"));
        try
        {
            _facts = await Task.Run(Reinstall.ReadFacts);
            ShowChecks();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException)
        {
            _facts = null;
            _checks.Children.Clear();
            _checks.Children.Add(Muted("Não foi possível ler o PC: " + ex.Message));
            Paint(1, false);
        }
        UpdatePrepare();
    }

    private void ShowChecks()
    {
        if (_facts is null) return;
        long bytes = Reinstall.PartitionBytes(_iso is null ? 6 * Reinstall.Gb : new FileInfo(_iso).Length);
        var checks = Reinstall.Evaluate(_facts, bytes);
        _checks.Children.Clear();
        var wrap = new WrapPanel();
        foreach (var c in checks)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 18, 2) };
            row.Children.Add(new TextBlock { Text = "●", Margin = new Thickness(0, 0, 6, 0), Foreground = Brush(c.Level switch { CheckLevel.Ok => "OkBrush", CheckLevel.Warn => "WarnBrush", _ => "BadBrush" }) });
            row.Children.Add(new TextBlock { Text = c.Text, FontSize = 12 });
            wrap.Children.Add(row);
        }
        _checks.Children.Add(wrap);
        Paint(1, Reinstall.CanPrepare(checks));
    }

    private async Task BackupAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var (info, key) = await Task.Run(() => Reinstall.Backup(UiProgress.Text(t => _backupText.Text = t), CancellationToken.None));
            _backup = info;
            _backupText.Text = $"Salvo: {info.Drivers} drivers, {info.WifiNetworks} rede(s) Wi-Fi e {(key.Length > 0 ? "a chave da BIOS" : "a informação de ativação digital")}. Vai para a MB-SETUP no passo 4.";
            _backupText.Foreground = Brush("OkBrush");
            Paint(2, true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _backupText.Text = "Não foi possível fazer o backup: " + ex.Message;
            _backupText.Foreground = Brush("BadBrush");
        }
        finally { _busy = false; UpdatePrepare(); }
    }

    private async Task PrepareAsync()
    {
        if (_busy || _iso is null || _facts is null || _backup is null) return;
        if (MessageBox.Show("O MB vai reduzir o C:, criar a partição MB-SETUP e programar o instalador para o próximo reinício.\n\nNada é apagado agora, e \"Cancelar a preparação\" desfaz tudo. Continuar?",
                "Preparar o instalador", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _busy = true;
        _prepare.IsEnabled = false;
        string iso = _iso, folder = _backup.Folder;
        var facts = _facts;
        try
        {
            _state = await Task.Run(() => Reinstall.Prepare(iso, facts, folder, UiProgress.Text(t => _prepText.Text = t), CancellationToken.None));
            SetStatus("Instalador preparado. Clique em \"Reiniciar e abrir o instalador\" quando estiver pronto.", "OkBrush");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            SetStatus("Não foi possível preparar (o que foi feito foi desfeito): " + ex.Message, "BadBrush");
        }
        finally { _busy = false; UpdatePrepare(); }
    }

    private async Task CancelAsync()
    {
        if (_busy || _state is null) return;
        _busy = true;
        var state = _state;
        try
        {
            await Task.Run(() => Reinstall.Cancel(state));
            _state = null;
            SetStatus("Preparação cancelada: entrada de boot removida e espaço devolvido ao C:.", "OkBrush");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            SetStatus("Não foi possível desfazer tudo: " + ex.Message + " Use o Gerenciador de discos para apagar a MB-SETUP.", "BadBrush");
        }
        finally { _busy = false; UpdatePrepare(); }
    }

    private void RestartNow()
    {
        if (_state is null) return;
        if (MessageBox.Show("O PC vai reiniciar e abrir o instalador do Windows.\n\nSalve e feche seus arquivos. Lembre: formatar o C: na tela do instalador apaga tudo nele.\n\nReiniciar agora?",
                "Reiniciar", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        Reinstall.Restart();
    }

    private async Task RemoveAsync()
    {
        if (_busy) return;
        if (MessageBox.Show("Devolver os drivers e as redes Wi-Fi salvos e apagar a partição MB-SETUP (o espaço volta para o C:)?", "Remover a MB-SETUP",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _busy = true;
        try
        {
            await Task.Run(() => Reinstall.RemoveAfterInstall(UiProgress.Text(t => _afterText.Text = t), CancellationToken.None));
            _afterText.Text = "Pronto: partição removida e espaço devolvido ao C:.";
            _afterText.Foreground = Brush("OkBrush");
            _remove.IsEnabled = false;
            Paint(4, true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _afterText.Text = "Não foi possível remover: " + ex.Message;
            _afterText.Foreground = Brush("BadBrush");
        }
        finally { _busy = false; }
    }
}
