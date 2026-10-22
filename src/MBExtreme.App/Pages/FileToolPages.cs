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
    private readonly List<(string Name, string Path)> _places = new();
    private int _place;
    private int _min = 1;

    public LargeFilesPage() : base("Arquivos grandes",
        "Encontra os maiores arquivos de uma unidade ou pasta. Nada é apagado sozinho: você vê a lista, abre a pasta e escolhe o que enviar para a Lixeira. Arquivos do Windows e dos programas ficam de fora.")
    {
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
            SetStatus(found.Count == 0 ? "Nenhum arquivo desse tamanho." : $"{found.Count} arquivos grandes, somando {Size(found.Sum(f => f.Length))}.", found.Count == 0 ? "MutedBrush" : "OkBrush");
            foreach (var file in found)
            {
                var f = file;
                var buttons = new StackPanel { Orientation = Orientation.Horizontal };
                buttons.Children.Add(SmallButton("Mostrar", (_, _) => Reveal(f.Path)));
                var row = Row(Path.GetFileName(f.Path), $"{Size(f.Length)} · {f.Modified:dd/MM/yyyy} · {Path.GetDirectoryName(f.Path)}", buttons);
                buttons.Children.Add(SmallButton("Lixeira", (_, _) =>
                {
                    if (Recycle(f.Path, $"Enviar {Path.GetFileName(f.Path)} ({Size(f.Length)}) para a Lixeira?")) Results.Children.Remove(row);
                }, true));
                Results.Children.Add(row);
            }
        };
    }
}

/// <summary>Arquivos duplicados: acha cópias idênticas (conteúdo conferido) e deixa escolher qual manter.</summary>
public sealed class DuplicatesPage : FileSearchPageBase
{
    private static readonly long[] MinSizes = { 1L << 20, 10L << 20, 100L << 20 };
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
        "Encontra cópias idênticas (o conteúdo é conferido por completo, não só o nome) e mostra quanto espaço dá para recuperar. Você escolhe o que enviar para a Lixeira; sempre fica pelo menos uma cópia.")
    {
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
            long wasted = groups.Sum(g => g.WastedBytes);
            SetStatus(groups.Count == 0 ? "Nenhum arquivo duplicado." : $"{groups.Count} grupos de cópias, {Size(wasted)} recuperáveis.", groups.Count == 0 ? "MutedBrush" : "OkBrush");
            foreach (var group in groups.Take(100)) Results.Children.Add(GroupCard(group));
        };
    }

    private UIElement GroupCard(DuplicateGroup group)
    {
        var panel = new StackPanel();
        var remaining = new List<(string Path, Border Row)>();
        var title = new TextBlock { Text = $"{Size(group.Length)} · {group.Files.Count} cópias", FontWeight = FontWeights.SemiBold, FontSize = 14 };
        panel.Children.Add(title);

        void Update() => title.Text = $"{Size(group.Length)} · {remaining.Count} {(remaining.Count == 1 ? "cópia" : "cópias")}";

        foreach (var file in group.Files)
        {
            var f = file;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(SmallButton("Mostrar", (_, _) => Reveal(f.Path)));
            var row = Row(Path.GetFileName(f.Path), $"{Path.GetDirectoryName(f.Path)} · {f.Modified:dd/MM/yyyy}", buttons);
            var trash = SmallButton("Lixeira", (_, _) => { }, true);
            trash.Click += (_, _) =>
            {
                if (remaining.Count <= 1) { SetStatus("Mantenha pelo menos uma cópia.", "WarnBrush"); return; }
                if (!Recycle(f.Path, $"Enviar esta cópia para a Lixeira?\n\n{f.Path}")) return;
                remaining.RemoveAll(r => r.Path == f.Path);
                panel.Children.Remove(row);
                Update();
            };
            buttons.Children.Add(trash);
            remaining.Add((f.Path, row));
            panel.Children.Add(row);
        }

        var card = CardOf(panel);
        card.Margin = new Thickness(0, 0, 0, 12);
        return card;
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
            SetStatus(_items.Count == 0 ? "Nada para limpar." : $"{_items.Count} itens encontrados.", _items.Count == 0 ? "MutedBrush" : "OkBrush");
        };
    }

    private void Section(string title, IReadOnlyList<string> paths)
    {
        Results.Children.Add(Heading($"{title} ({paths.Count})"));
        foreach (string path in paths.Take(300))
        {
            var box = new CheckBox { Content = path, IsChecked = true, Foreground = Brush("TextBrush"), Margin = new Thickness(0, 3, 0, 3) };
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
