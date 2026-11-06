using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MBExtreme.Core.Apps;
using MBExtreme.Core.Operations;

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
    private readonly TextBox _search = new() { Padding = new Thickness(10, 6, 10, 6), FontSize = 13 };
    private readonly WrapPanel _chips = new() { Margin = new Thickness(0, 10, 0, 6) };
    private readonly UniformGrid _grid = new() { Columns = 4 };
    private readonly TextBlock _status = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly Button _install;
    private string _category = "Todos";
    private bool _busy;

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    public InstallAppsPage()
    {
        var root = new DockPanel { Margin = new Thickness(40, 24, 40, 16) };
        var head = new StackPanel();
        head.Children.Add(new TextBlock { Text = "Programas", FontSize = 28, FontWeight = FontWeights.SemiBold });
        head.Children.Add(new TextBlock { Text = "Instale os programas mais usados em poucos cliques, sem procurar site por site. Vem do winget, o instalador oficial da Microsoft, direto do fabricante, sem programas extras.", Foreground = Res("MutedBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10) });
        System.Windows.Automation.AutomationProperties.SetName(_search, "Procurar programa");
        _search.TextChanged += (_, _) => Fill();
        head.Children.Add(_search);

        head.Children.Add(new TextBlock { Text = "Kits prontos", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) });
        var kits = new UniformGrid { Columns = 6 };
        foreach (var kit in AppCatalog.Kits)
        {
            var k = kit;
            int count = AppCatalog.InKit(kit.Key).Count;
            kits.Children.Add(ToolCard.Build(kit.Title, $"{count} programas · {kit.Description}", kit.Icon, () => MarkKit(k)));
        }
        head.Children.Add(kits);
        head.Children.Add(_chips);
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var bottom = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition());
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottom.Children.Add(_status);
        var clear = new Button { Content = "Desmarcar tudo", Style = (Style)Application.Current.FindResource("GhostButton"), Padding = new Thickness(14, 7, 14, 7), FontSize = 13, Margin = new Thickness(8, 0, 8, 0) };
        clear.Click += (_, _) => { foreach (var b in _boxes.Values) b.IsChecked = false; UpdateCount(); };
        Grid.SetColumn(clear, 1);
        bottom.Children.Add(clear);
        _install = new Button { Content = "Instalar", Style = (Style)Application.Current.FindResource("PrimaryButton"), Padding = new Thickness(22, 7, 22, 7), FontSize = 13 };
        _install.Click += async (_, _) => await InstallAsync();
        Grid.SetColumn(_install, 2);
        bottom.Children.Add(_install);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _grid });
        Content = root;

        foreach (var entry in AppCatalog.All)
        {
            var box = new CheckBox { VerticalAlignment = VerticalAlignment.Top };
            System.Windows.Automation.AutomationProperties.SetName(box, "Marcar " + entry.Title);
            box.Checked += (_, _) => UpdateCount();
            box.Unchecked += (_, _) => UpdateCount();
            _boxes[entry.Id] = box;
            _states[entry.Id] = new TextBlock { FontSize = 10.5, Margin = new Thickness(0, 4, 0, 0) };
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
                Content = cat, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(12, 4, 12, 4), FontSize = 12,
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
        var box = _boxes[entry.Id];
        if (box.Parent is Panel old) old.Children.Remove(box);
        var state = _states[entry.Id];
        if (state.Parent is Panel oldState) oldState.Children.Remove(state);
        var text = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        text.Children.Add(new TextBlock { Text = entry.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = entry.Description, FontSize = 11, Foreground = Res("MutedBrush"), TextWrapping = TextWrapping.Wrap, MaxHeight = 30 });
        text.Children.Add(state);
        var row = new DockPanel();
        DockPanel.SetDock(box, Dock.Left);
        row.Children.Add(box);
        row.Children.Add(text);
        var border = new Border
        {
            Background = Res("SurfaceBrush"), BorderBrush = Res("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 9, 10, 9), Margin = new Thickness(0, 0, 8, 8), Child = row, Cursor = System.Windows.Input.Cursors.Hand,
        };
        border.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not CheckBox) box.IsChecked = box.IsChecked != true; };
        return border;
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
            state.Text = has ? "✔ Já instalado" : "";
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
            var results = await Task.Run(() => OperationQueue.Shared.Run(operations, new Progress<string>(t => _status.Text = t), CancellationToken.None));
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
