using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MBExtreme.Core.Apps;
using MBExtreme.Core.Operations;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Instalar programas: kits prontos (Essencial, Gamer, Escritório...), categorias, busca e cartões com "Já instalado".
/// Marque o que quiser e clique em Instalar: vai um de cada vez pelo winget, direto da fonte oficial.
/// </summary>
public sealed class InstallAppsPage : UserControl
{
    private readonly Dictionary<string, CheckBox> _boxes = new();
    private readonly Dictionary<string, TextBlock> _states = new();
    private ISet<string> _installed = new HashSet<string>();
    private readonly TextBox _search;
    private readonly WrapPanel _chips = new();
    private readonly UniformGrid _grid = CompactPick.Grid(6, 240);
    private readonly TextBlock _status = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly Button _install;
    private string _category = "Todos";
    private bool _busy;

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    public InstallAppsPage()
    {
        var root = new DockPanel { Margin = new Thickness(28, 18, 28, 14) };
        var head = new StackPanel();
        var titleRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        titleRow.Children.Add(new TextBlock { Text = "Programas", FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Bottom });
        titleRow.Children.Add(new TextBlock { Text = "Instale direto do fabricante pelo winget, o instalador oficial da Microsoft, sem programas extras.", FontSize = 12, Foreground = Res("MutedBrush"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 0, 2), TextWrapping = TextWrapping.Wrap });
        head.Children.Add(titleRow);

        // barra única: busca, filtros, contador e o botão principal
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var (searchBox, search) = CompactPick.Search("Buscar programa...", 240);
        _search = search;
        _search.TextChanged += (_, _) => Fill();
        DockPanel.SetDock(searchBox, Dock.Left);
        bar.Children.Add(searchBox);
        _install = CompactPick.Button("Instalar", primary: true);
        _install.Click += async (_, _) => await InstallAsync();
        DockPanel.SetDock(_install, Dock.Right);
        bar.Children.Add(_install);
        var clear = CompactPick.Button("Desmarcar tudo");
        clear.Click += (_, _) => { foreach (var b in _boxes.Values) b.IsChecked = false; UpdateCount(); };
        DockPanel.SetDock(clear, Dock.Right);
        bar.Children.Add(clear);
        _chips.Margin = new Thickness(10, 0, 0, 0);
        _chips.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(_chips);
        head.Children.Add(bar);

        // kits prontos: faixa fina, um clique marca o kit
        head.Children.Add(new TextBlock { Text = "KITS PRONTOS (um clique marca todos)", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Res("MutedBrush"), Margin = new Thickness(0, 2, 0, 4) });
        var kits = new UniformGrid { Columns = 6, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var kit in AppCatalog.Kits)
        {
            var k = kit;
            int count = AppCatalog.InKit(kit.Key).Count;
            var b = new Button { Style = (Style)Application.Current.FindResource("GhostButton"), Padding = new Thickness(10, 4, 10, 4), MinHeight = 36, Margin = new Thickness(0, 0, 6, 0), HorizontalContentAlignment = HorizontalAlignment.Left, ToolTip = kit.Description };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = kit.Title, FontSize = 12.5, FontWeight = FontWeights.SemiBold });
            row.Children.Add(new TextBlock { Text = $"  {count} programas", FontSize = 11, Foreground = Res("MutedBrush"), VerticalAlignment = VerticalAlignment.Center });
            b.Content = row;
            b.Click += (_, _) => MarkKit(k);
            kits.Children.Add(b);
        }
        head.Children.Add(kits);
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        _status.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);

        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _grid });
        Content = root;

        foreach (var entry in AppCatalog.All)
        {
            var box = new CheckBox { VerticalAlignment = VerticalAlignment.Top };
            System.Windows.Automation.AutomationProperties.SetName(box, "Marcar " + entry.Title);
            box.Checked += (_, _) => UpdateCount();
            box.Unchecked += (_, _) => UpdateCount();
            _boxes[entry.Id] = box;
            _states[entry.Id] = CompactPick.Tag("");
        }
        BuildChips();
        Fill();
        UpdateCount();
        Loaded += async (_, _) => await CheckInstalledAsync();
    }

    private void BuildChips()
    {
        _chips.Children.Clear();
        foreach (var cat in new[] { "Todos" }.Concat(AppCatalog.Categories))
        {
            string c = cat;
            var b = new Button
            {
                Content = cat, Margin = new Thickness(0, 2, 6, 2), Padding = new Thickness(10, 2, 10, 2), FontSize = 11.5, MinHeight = 26,
                Style = (Style)Application.Current.FindResource(cat == _category ? "PrimaryButton" : "GhostButton"),
            };
            b.Click += (_, _) => { _category = c; BuildChips(); Fill(); };
            _chips.Children.Add(b);
        }
    }

    private void Fill()
    {
        _grid.Children.Clear();
        string q = _search.Text.Trim();
        foreach (var entry in AppCatalog.All)
        {
            if (_category != "Todos" && entry.Category != _category) continue;
            if (q.Length > 0 && !entry.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase) && !entry.Description.Contains(q, StringComparison.CurrentCultureIgnoreCase)) continue;
            _grid.Children.Add(Card(entry));
        }
    }

    private Border Card(CatalogEntry entry)
    {
        var state = _states[entry.Id];
        return CompactPick.Card(_boxes[entry.Id], CompactPick.CatalogIcon(entry.Id, entry.Title), entry.Title, entry.Description, state);
    }

    private void MarkKit(AppKit kit)
    {
        int added = 0;
        foreach (var entry in AppCatalog.InKit(kit.Key))
            if (!_installed.Contains(entry.Id) && _boxes[entry.Id].IsChecked != true) { _boxes[entry.Id].IsChecked = true; added++; }
        _category = "Todos";
        BuildChips();
        Fill();
        _status.Text = $"Kit {kit.Title}: {added} programa(s) marcado(s) (os que já estão instalados ficam de fora).";
    }

    private void UpdateCount()
    {
        int n = _boxes.Values.Count(b => b.IsChecked == true);
        _install.Content = n == 0 ? "Instalar" : $"Instalar ({n})";
        _install.IsEnabled = n > 0 && !_busy;
        if (!_busy) _status.Text = n == 0 ? "Marque os programas que quer instalar (ou escolha um kit)." : $"{n} programa(s) marcado(s).";
    }

    private async Task CheckInstalledAsync()
    {
        if (!await Task.Run(WingetService.IsAvailable))
        {
            _status.Text = "O winget não foi encontrado. Instale o \"Instalador de Aplicativo\" pela Microsoft Store para usar esta tela.";
            _status.Foreground = Res("WarnBrush");
            return;
        }
        _installed = await Task.Run(() => WingetService.InstalledOf(AppCatalog.All.Select(a => a.Id)));
        foreach (var (id, state) in _states)
        {
            bool has = _installed.Contains(id);
            state.Text = has ? "✔ Instalado" : "";
            state.Foreground = Res("OkBrush");
        }
    }

    private async Task InstallAsync()
    {
        if (_busy) return;
        var chosen = AppCatalog.All.Where(a => _boxes[a.Id].IsChecked == true).ToList();
        if (chosen.Count == 0) return;
        var operations = chosen.Select(a => (IOperation)new WingetOperation(a.Id, a.Title, upgrade: false)).ToList();
        var byOp = operations.Zip(chosen).ToDictionary(p => p.First, p => p.Second.Id);
        _busy = true;
        _install.IsEnabled = false;
        void OnChanged(QueueItem item) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!byOp.TryGetValue(item.Operation, out var id)) return;
            var state = _states[id];
            (state.Text, state.Foreground) = item.State switch
            {
                OperationState.Running => ("⏳ Instalando...", Res("TextBrush")),
                OperationState.Succeeded or OperationState.RebootRequired => ("✔ Instalado", Res("OkBrush")),
                OperationState.Pending => ("Na fila", Res("MutedBrush")),
                OperationState.Skipped => ("–", Res("MutedBrush")),
                _ => ("✖ " + item.Message, Res("BadBrush")),
            };
            if (item.State is OperationState.Succeeded or OperationState.RebootRequired) { _boxes[id].IsChecked = false; _installed.Add(id); }
        }));
        OperationQueue.Shared.Changed += OnChanged;
        try
        {
            _status.Text = $"Instalando {chosen.Count} programa(s), um de cada vez...";
            var results = await Task.Run(() => OperationQueue.Shared.Run(operations, UiProgress.Text(t => _status.Text = t), CancellationToken.None));
            int ok = results.Count(r => r.State is OperationState.Succeeded or OperationState.RebootRequired);
            _status.Text = $"{ok} instalado(s), {results.Count - ok} com erro." + (results.Any(r => r.State == OperationState.RebootRequired) ? " Reinicie para terminar." : "");
        }
        catch (InvalidOperationException ex) { _status.Text = ex.Message; }
        finally
        {
            OperationQueue.Shared.Changed -= OnChanged;
            _busy = false;
            UpdateCount();
        }
    }
}
