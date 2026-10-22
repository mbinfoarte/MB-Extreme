using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Tools;

namespace MBExtreme.UI.Pages;

/// <summary>Base das ferramentas que procuram arquivos: botão Procurar/Parar e resultados.</summary>
public abstract class FileSearchPageBase : ToolPageBase
{
    protected readonly StackPanel Results = new();
    private readonly Button _start;
    private readonly Button _stop;
    private CancellationTokenSource? _cts;

    protected FileSearchPageBase(string title, string description, string startText = "Procurar") : base(title, description)
    {
        _start = AddButton(startText, async (_, _) => await RunAsync());
        _stop = AddButton("Parar", (_, _) => _cts?.Cancel(), false);
        _stop.IsEnabled = false;
    }

    /// <summary>Faz a procura (fora da interface) e devolve uma função que mostra o resultado.</summary>
    protected abstract Task<Action> SearchAsync(CancellationToken token, IProgress<string> progress);

    private async Task RunAsync()
    {
        _start.IsEnabled = false;
        _stop.IsEnabled = true;
        Results.Children.Clear();
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(text => SetStatus(text));
        try
        {
            SetStatus("Procurando...");
            var show = await SearchAsync(_cts.Token, progress);
            show();
        }
        catch (OperationCanceledException)
        {
            SetStatus("Procura interrompida.", "WarnBrush");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or AggregateException)
        {
            SetStatus("Não foi possível procurar: " + ex.Message, "BadBrush");
        }
        finally
        {
            _start.IsEnabled = true;
            _stop.IsEnabled = false;
        }
    }

    /// <summary>Linha de resultado com caixa de seleção à esquerda, texto e botões à direita.</summary>
    protected Border CheckRow(CheckBox box, string title, string detail, params UIElement[] buttons)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(box);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = detail, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brush("MutedBrush") });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var b in buttons) right.Children.Add(b);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return CardOf(grid);
    }

    protected static string Size(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:0.0} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:0} MB",
        _ => $"{bytes / 1024.0:0} KB",
    };

    protected void Reveal(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            SetStatus("Não foi possível abrir a pasta.", "WarnBrush");
        }
    }

    /// <summary>Confirma e envia à Lixeira. Retorna true se foi.</summary>
    protected bool Recycle(string path, string confirmText)
    {
        if (MessageBox.Show(confirmText, "Enviar para a Lixeira", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return false;
        try
        {
            RecycleBin.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        {
            SetStatus("Não foi possível apagar: " + ex.Message, "BadBrush");
            return false;
        }
    }
}

/// <summary>Arquivos grandes: o que mais ocupa espaço, para você decidir o que apagar.</summary>
public sealed class LargeFilesPage : FileSearchPageBase
{
    private static readonly long[] MinSizes = { 100L << 20, 500L << 20, 1L << 30 };
    private readonly List<(FoundFile File, CheckBox Box, Border Row)> _rows = new();
    private readonly List<(string Name, string Path)> _places = new();
    private readonly Button _send;
    private int _place;
    private int _min = 1;

    public LargeFilesPage() : base("Arquivos grandes",
        "Encontra os maiores arquivos de uma unidade ou pasta. Nada é apagado sozinho: você vê a lista, marca o que quer (ou clica em Marcar todos) e envia para a Lixeira. Arquivos do Windows e dos programas ficam de fora.")
    {
        _send = AddButton("Enviar marcados para a Lixeira", (_, _) => SendMarked(), false);
        _send.IsEnabled = false;
        AddSelectionButtons(() => _rows.Select(r => r.Box));

        foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            _places.Add(($"Unidade {d.Name.TrimEnd('\\')}", d.Name));
        _places.Insert(0, ("Minha pasta de usuário", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));

        Body.Children.Add(Heading("Onde procurar"));
        Body.Children.Add(Choice(_places.Select(p => p.Name).ToList(), _place, i => _place = i));
        Body.Children.Add(Heading("Tamanho mínimo"));
        Body.Children.Add(Choice(new[] { "100 MB", "500 MB", "1 GB" }, _min, i => _min = i));
        Body.Children.Add(Results);
    }

    protected override async Task<Action> SearchAsync(CancellationToken token, IProgress<string> progress)
    {
        string root = _places[_place].Path;
        long min = MinSizes[_min];
        var found = await Task.Run(() => FileSearch.Largest(root, min, 100, token, n => progress.Report($"Procurando... {n:N0} arquivos verificados")));
        return () =>
        {
            Results.Children.Clear();
            _rows.Clear();
            SetStatus(found.Count == 0 ? "Nenhum arquivo desse tamanho." : $"{found.Count} arquivos grandes, somando {Size(found.Sum(f => f.Length))}.", found.Count == 0 ? "MutedBrush" : "OkBrush");
            foreach (var file in found)
            {
                var f = file;
                var box = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                var show = SmallButton("Mostrar", (_, _) => Reveal(f.Path));
                var row = CheckRow(box, Path.GetFileName(f.Path), $"{Size(f.Length)} · {f.Modified:dd/MM/yyyy} · {Path.GetDirectoryName(f.Path)}", show);
                _rows.Add((f, box, row));
                Results.Children.Add(row);
            }

            _send.IsEnabled = _rows.Count > 0;
        };
    }

    private void SendMarked()
    {
        var chosen = _rows.Where(r => r.Box.IsChecked == true).ToList();
        if (chosen.Count == 0) { SetStatus("Marque os arquivos que quer enviar para a Lixeira.", "WarnBrush"); return; }
        long total = chosen.Sum(r => r.File.Length);
        if (MessageBox.Show($"Enviar {chosen.Count} arquivo(s), {Size(total)}, para a Lixeira?", "Enviar para a Lixeira", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        int done = 0;
        foreach (var r in chosen)
        {
            try
            {
                RecycleBin.Delete(r.File.Path);
                Results.Children.Remove(r.Row);
                _rows.Remove(r);
                done++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException) { }
        }

        SetStatus($"{done} de {chosen.Count} arquivos enviados para a Lixeira.", done == chosen.Count ? "OkBrush" : "WarnBrush");
        _send.IsEnabled = _rows.Count > 0;
    }
}

/// <summary>Arquivos duplicados: acha cópias idênticas (conteúdo conferido) e deixa escolher quais enviar para a Lixeira.</summary>
public sealed class DuplicatesPage : FileSearchPageBase
{
    private static readonly long[] MinSizes = { 1L << 20, 10L << 20, 100L << 20 };
    private sealed record Copy(FoundFile File, CheckBox Box, Border Row);
    private sealed record Group(long Length, List<Copy> Copies, TextBlock Title, StackPanel Panel);

    private readonly List<Group> _groups = new();
    private readonly Button _send;
    private int _place;
    private int _min;

    private static readonly (string Name, Environment.SpecialFolder[] Folders)[] Places =
    {
        ("Todas as pastas pessoais", new[] { Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.MyVideos, Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.DesktopDirectory }),
        ("Documentos", new[] { Environment.SpecialFolder.MyDocuments }),
        ("Imagens", new[] { Environment.SpecialFolder.MyPictures }),
        ("Vídeos", new[] { Environment.SpecialFolder.MyVideos }),
        ("Música", new[] { Environment.SpecialFolder.MyMusic }),
        ("Área de trabalho", new[] { Environment.SpecialFolder.DesktopDirectory }),
    };

    public DuplicatesPage() : base("Arquivos duplicados",
        "Encontra cópias idênticas (o conteúdo é conferido por completo, não só o nome). Clique em Marcar cópias extras para marcar tudo menos a cópia mais antiga de cada grupo e envie para a Lixeira; sempre fica pelo menos uma cópia.")
    {
        _send = AddButton("Enviar marcados para a Lixeira", (_, _) => SendMarked(), false);
        _send.IsEnabled = false;
        AddButton("Marcar cópias extras", (_, _) => MarkExtras(), false);
        AddButton("Desmarcar todos", (_, _) => { foreach (var g in _groups) foreach (var c in g.Copies) c.Box.IsChecked = false; }, false);

        Body.Children.Add(Heading("Onde procurar"));
        Body.Children.Add(Choice(Places.Select(p => p.Name).ToList(), _place, i => _place = i));
        Body.Children.Add(Heading("Tamanho mínimo"));
        Body.Children.Add(Choice(new[] { "1 MB", "10 MB", "100 MB" }, _min, i => _min = i));
        Body.Children.Add(Results);
    }

    protected override async Task<Action> SearchAsync(CancellationToken token, IProgress<string> progress)
    {
        var roots = Places[_place].Folders.Select(Environment.GetFolderPath).Where(p => p.Length > 0).Distinct().ToList();
        long min = MinSizes[_min];
        var groups = await Task.Run(() => FileSearch.Duplicates(roots, min, token, step => progress.Report(step)));
        return () =>
        {
            Results.Children.Clear();
            _groups.Clear();
            long wasted = groups.Sum(g => g.WastedBytes);
            SetStatus(groups.Count == 0 ? "Nenhum arquivo duplicado." : $"{groups.Count} grupos de cópias, {Size(wasted)} recuperáveis.", groups.Count == 0 ? "MutedBrush" : "OkBrush");
            foreach (var group in groups.Take(100)) Results.Children.Add(GroupCard(group));
            _send.IsEnabled = _groups.Count > 0;
        };
    }

    private UIElement GroupCard(DuplicateGroup source)
    {
        var panel = new StackPanel();
        var title = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 14 };
        panel.Children.Add(title);
        var group = new Group(source.Length, new List<Copy>(), title, panel);

        foreach (var file in source.Files)
        {
            var f = file;
            var box = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var row = CheckRow(box, Path.GetFileName(f.Path), $"{Path.GetDirectoryName(f.Path)} · {f.Modified:dd/MM/yyyy}", SmallButton("Mostrar", (_, _) => Reveal(f.Path)));
            group.Copies.Add(new Copy(f, box, row));
            panel.Children.Add(row);
        }

        _groups.Add(group);
        UpdateTitle(group);
        var card = CardOf(panel);
        card.Margin = new Thickness(0, 0, 0, 12);
        return card;
    }

    private void UpdateTitle(Group g) =>
        g.Title.Text = $"{Size(g.Length)} · {g.Copies.Count} {(g.Copies.Count == 1 ? "cópia" : "cópias")}";

    /// <summary>Marca todas as cópias de cada grupo, menos a mais antiga (a primeira da lista).</summary>
    private void MarkExtras()
    {
        foreach (var g in _groups)
            for (int i = 0; i < g.Copies.Count; i++) g.Copies[i].Box.IsChecked = i > 0;
    }

    private void SendMarked()
    {
        var chosen = _groups.SelectMany(g => g.Copies.Where(c => c.Box.IsChecked == true).Select(c => (Group: g, Copy: c))).ToList();
        if (chosen.Count == 0) { SetStatus("Marque as cópias que quer enviar (ou clique em Marcar cópias extras).", "WarnBrush"); return; }

        // nunca deixa um grupo sem nenhuma cópia
        var allowed = new List<(Group Group, Copy Copy)>();
        int skippedGroups = 0;
        foreach (var byGroup in chosen.GroupBy(c => c.Group))
        {
            if (byGroup.Count() >= byGroup.Key.Copies.Count) { skippedGroups++; continue; }
            allowed.AddRange(byGroup);
        }

        if (allowed.Count == 0) { SetStatus("Em cada grupo, deixe pelo menos uma cópia desmarcada.", "WarnBrush"); return; }
        long total = allowed.Sum(c => c.Copy.File.Length);
        if (MessageBox.Show($"Enviar {allowed.Count} cópia(s), {Size(total)}, para a Lixeira?" + (skippedGroups > 0 ? $"\n\n{skippedGroups} grupo(s) com todas as cópias marcadas foram ignorados." : ""),
                "Enviar para a Lixeira", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        int done = 0;
        foreach (var (group, copy) in allowed)
        {
            try
            {
                RecycleBin.Delete(copy.File.Path);
                group.Panel.Children.Remove(copy.Row);
                group.Copies.Remove(copy);
                UpdateTitle(group);
                done++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException) { }
        }

        SetStatus($"{done} de {allowed.Count} cópias enviadas para a Lixeira.", done == allowed.Count ? "OkBrush" : "WarnBrush");
    }
}

/// <summary>Pastas vazias e atalhos quebrados: lixo que só atrapalha. Marque o que quer limpar.</summary>
public sealed class CleanExtrasPage : FileSearchPageBase
{
    private readonly List<(string Path, CheckBox Box)> _items = new();
    private readonly Button _clean;

    public CleanExtrasPage() : base("Pastas vazias e atalhos inválidos",
        "Procura pastas vazias no seu perfil e atalhos da área de trabalho e do menu Iniciar que apontam para programas ou arquivos que não existem mais. Os itens vão para a Lixeira, então dá para recuperar.")
    {
        _clean = AddButton("Limpar marcados", (_, _) => CleanSelected(), false);
        _clean.IsEnabled = false;
        AddSelectionButtons(() => _items.Select(i => i.Box));
        Body.Children.Add(Results);
    }

    protected override async Task<Action> SearchAsync(CancellationToken token, IProgress<string> progress)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folders = await Task.Run(() =>
        {
            progress.Report("Procurando pastas vazias...");
            var list = new List<string>();
            foreach (string sub in new[] { "Documents", "Downloads", "Desktop", "Pictures", "Videos", "Music" })
            {
                string path = Path.Combine(profile, sub);
                if (Directory.Exists(path)) list.AddRange(FileSearch.EmptyFolders(path, token));
            }
            return list;
        });
        var shortcuts = await Task.Run(() =>
        {
            progress.Report("Procurando atalhos inválidos...");
            return ShortcutTools.Broken(ShortcutTools.DefaultFolders(), token);
        });

        return () =>
        {
            Results.Children.Clear();
            _items.Clear();
            Section("Pastas vazias", folders);
            Section("Atalhos inválidos", shortcuts);
            _clean.IsEnabled = _items.Count > 0;
            SetStatus(_items.Count == 0 ? "Nada para limpar." : $"{_items.Count} itens encontrados. Marque o que quer limpar e clique em Limpar marcados.", _items.Count == 0 ? "MutedBrush" : "OkBrush");
        };
    }

    private void Section(string title, IReadOnlyList<string> paths)
    {
        Results.Children.Add(Heading($"{title} ({paths.Count})"));
        foreach (string path in paths.Take(300))
        {
            var box = new CheckBox { Content = path, IsChecked = false, Foreground = Brush("TextBrush"), Margin = new Thickness(0, 3, 0, 3) };
            _items.Add((path, box));
            Results.Children.Add(box);
        }
        if (paths.Count == 0) Results.Children.Add(Muted("Nenhum."));
    }

    private void CleanSelected()
    {
        var chosen = _items.Where(i => i.Box.IsChecked == true).ToList();
        if (chosen.Count == 0) return;
        if (MessageBox.Show($"Enviar {chosen.Count} itens para a Lixeira?", "Limpar", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        int ok = 0;
        foreach (var (path, box) in chosen)
        {
            try
            {
                // a pasta pode ter deixado de estar vazia desde a procura
                if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any()) continue;
                RecycleBin.Delete(path);
                box.IsEnabled = false;
                box.IsChecked = false;
                box.Content = "✔ " + path;
                ok++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException) { }
        }

        SetStatus($"{ok} de {chosen.Count} itens enviados para a Lixeira.", ok == chosen.Count ? "OkBrush" : "WarnBrush");
    }
}
