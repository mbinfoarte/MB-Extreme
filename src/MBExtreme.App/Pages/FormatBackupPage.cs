using System.IO;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Windows;

namespace MBExtreme.UI.Pages;

/// <summary>Backup para formatar: salva drivers e redes Wi-Fi antes e devolve tudo depois (a Pós-formatação também acha o backup).</summary>
public sealed class FormatBackupPage : ToolPageBase
{
    private string? _destination;
    private readonly TextBlock _destText = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly CheckBox _drivers = new() { Content = "Drivers do PC", IsChecked = true, Margin = new Thickness(0, 8, 16, 0) };
    private readonly CheckBox _wifi = new() { Content = "Redes Wi-Fi (com a senha)", IsChecked = true, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel _found = new();
    private bool _busy;

    public FormatBackupPage() : base("Backup para formatar",
        "Salve os drivers e as redes Wi-Fi antes de formatar. Depois, abra o MB Extreme, vá em Pós-formatação ou aqui, e devolva tudo com um clique. Seus arquivos (fotos, documentos) não entram neste backup: copie esses à parte.")
    {
        _drivers.Foreground = _wifi.Foreground = Brush("TextBrush");
        var before = new StackPanel();
        before.Children.Add(new TextBlock { Text = "Antes de formatar: salvar", FontSize = 15, FontWeight = FontWeights.SemiBold });
        var dest = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        dest.ColumnDefinitions.Add(new ColumnDefinition());
        dest.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _destText.Text = "Nenhuma pasta escolhida (use um pendrive ou outro disco, não o C:).";
        dest.Children.Add(_destText);
        var choose = SmallButton("Escolher onde salvar...", (_, _) => Choose());
        Grid.SetColumn(choose, 1);
        dest.Children.Add(choose);
        before.Children.Add(dest);
        var boxes = new StackPanel { Orientation = Orientation.Horizontal };
        boxes.Children.Add(_drivers);
        boxes.Children.Add(_wifi);
        before.Children.Add(boxes);
        var warn = Muted("Atenção: o arquivo das redes Wi-Fi guarda as senhas em texto, que qualquer pessoa consegue ler. Guarde o pendrive com cuidado e apague a pasta do backup depois de usar.", 11.5);
        warn.Foreground = Brush("WarnBrush");
        warn.Margin = new Thickness(0, 8, 0, 8);
        before.Children.Add(warn);
        before.Children.Add(SmallButton("Fazer o backup", async (_, _) => await BackupAsync(), true));
        Body.Children.Add(CardOf(before));

        var after = new StackPanel();
        after.Children.Add(new TextBlock { Text = "Depois de formatar: restaurar", FontSize = 15, FontWeight = FontWeights.SemiBold });
        after.Children.Add(Muted("Conecte o pendrive ou o disco com o backup e clique em Procurar de novo."));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 8) };
        buttons.Children.Add(SmallButton("Procurar de novo", (_, _) => FindBackups()));
        buttons.Children.Add(SmallButton("Escolher a pasta do backup...", (_, _) => ChooseBackup()));
        after.Children.Add(buttons);
        after.Children.Add(_found);
        Body.Children.Add(CardOf(after));
        Loaded += (_, _) => FindBackups();
    }

    private void Choose()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Onde salvar o backup (pendrive ou outro disco)" };
        if (dialog.ShowDialog() != true) return;
        _destination = dialog.FolderName;
        string system = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        bool onSystem = string.Equals(Path.GetPathRoot(_destination), system, StringComparison.OrdinalIgnoreCase);
        _destText.Text = _destination + (onSystem ? "   ⚠ é o disco do Windows: se formatar, o backup some!" : "");
        _destText.Foreground = Brush(onSystem ? "WarnBrush" : "TextBrush");
    }

    private async Task BackupAsync()
    {
        if (_busy) return;
        if (_destination is null) { SetStatus("Escolha onde salvar primeiro.", "WarnBrush"); return; }
        if (_drivers.IsChecked != true && _wifi.IsChecked != true) { SetStatus("Marque o que salvar.", "WarnBrush"); return; }
        _busy = true;
        try
        {
            var (info, _) = await Task.Run(() => FormatBackup.Create(_destination, _drivers.IsChecked == true, _wifi.IsChecked == true,
                new Progress<string>(t => SetStatus(t)), CancellationToken.None));
            SetStatus($"Backup feito em {info.Folder}: {info.Drivers} driver(s) e {info.WifiNetworks} rede(s) Wi-Fi.", "OkBrush");
            FindBackups();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SetStatus("Não foi possível fazer o backup: " + ex.Message, "BadBrush");
        }
        finally { _busy = false; }
    }

    private void FindBackups() => ShowFound(FormatBackup.Find(FormatBackup.ReadyRoots()));

    private void ChooseBackup()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Pasta do backup (a que tem o backup.json)" };
        if (dialog.ShowDialog() != true) return;
        string manifest = Path.Combine(dialog.FolderName, FormatBackup.Manifest);
        var info = File.Exists(manifest) ? FormatBackup.FromJson(File.ReadAllText(manifest), dialog.FolderName) : null;
        if (info is null) { SetStatus("Essa pasta não tem um backup do MB Extreme (falta o backup.json).", "WarnBrush"); return; }
        ShowFound(new[] { info });
    }

    private void ShowFound(IReadOnlyList<FormatBackupInfo> list)
    {
        _found.Children.Clear();
        if (list.Count == 0) { _found.Children.Add(Muted("Nenhum backup do MB Extreme nos discos e pendrives conectados.")); return; }
        foreach (var b in list)
        {
            var backup = b;
            _found.Children.Add(Row($"{b.Computer} · {b.Created:dd/MM/yyyy HH:mm}", $"{b.Drivers} driver(s) · {b.WifiNetworks} rede(s) Wi-Fi · {b.Windows}\n{b.Folder}",
                SmallButton("Restaurar", async (_, _) => await RestoreAsync(backup), true)));
        }
    }

    private async Task RestoreAsync(FormatBackupInfo backup)
    {
        if (_busy) return;
        if (MessageBox.Show($"Instalar os drivers e as redes Wi-Fi do backup de {backup.Computer} ({backup.Created:dd/MM/yyyy})?\n\nO Windows usa só os drivers que servem neste PC. Pode levar alguns minutos e a tela pode piscar.",
                "Restaurar backup", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _busy = true;
        try
        {
            await Task.Run(() => FormatBackup.Restore(backup, new Progress<string>(t => SetStatus(t)), CancellationToken.None));
            SetStatus("Pronto: drivers e redes Wi-Fi devolvidos. Se algum aparelho ainda estiver sem driver, reinicie o PC.", "OkBrush");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SetStatus("Não foi possível restaurar: " + ex.Message, "BadBrush");
        }
        finally { _busy = false; }
    }
}
