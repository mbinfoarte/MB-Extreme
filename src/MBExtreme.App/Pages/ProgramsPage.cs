using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MBExtreme.Core.Apps;
using MBExtreme.Core.Operations;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Programas: atualizações e instalações pelo winget (fonte oficial da Microsoft), uma de cada vez pela fila, com aviso quando o programa
/// está aberto e mensagens de erro em português; e o inventário de tudo que está instalado (instalador comum e Microsoft Store), com desinstalação acompanhada.
/// </summary>
public sealed class ProgramsPage : ToolPageBase
{
    private readonly Dictionary<WingetApp, CheckBox> _upgrades = new();
    private readonly Dictionary<CatalogApp, CheckBox> _installs = new();
    private IReadOnlyList<InstalledProgram>? _inventory;
    private readonly Dictionary<InstalledProgram, CheckBox> _removes = new();
    private readonly System.Windows.Controls.Primitives.UniformGrid _inventoryGrid = CompactPick.Grid(6, 250);
    private TextBox? _inventorySearch;
    private readonly WrapPanel _inventoryChips = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    private Button? _removeButton;
    private string _inventoryFilter = "Todos", _inventorySort = "Nome";
    private IReadOnlyList<WingetApp> _lastUpgrades = Array.Empty<WingetApp>();
    private CancellationTokenSource? _cts;
    private readonly bool _uninstaller;

    public ProgramsPage() : this(false) { }

    /// <summary>startWithInventory: abre já na lista do que está instalado (tela Desinstalador).</summary>
    public ProgramsPage(bool startWithInventory) : base(startWithInventory ? "Desinstalador" : "Atualizar programas", "")
    {
        _uninstaller = startWithInventory;
        HideHeader();
        Body.Margin = new Thickness(0);
        Loaded += async (_, _) =>
        {
            if (Body.Children.Count > 0) return;
            if (startWithInventory) await ShowInventoryAsync();
            else { Render(Array.Empty<WingetApp>()); await SearchAsync(); }
        };
    }

    /// <summary>Título pequeno com a descrição na mesma linha.</summary>
    private UIElement Title(string title, string sub)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        row.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Bottom });
        row.Children.Add(new TextBlock { Text = sub, FontSize = 12, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 0, 2) });
        return row;
    }

    private static TextBlock Section(string text) => new() { Text = text, FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = (Brush)Application.Current.FindResource("MutedBrush"), Margin = new Thickness(0, 6, 0, 4) };

    private async Task SearchAsync()
    {
        SetStatus("Procurando atualizações...");
        try
        {
            if (!await Task.Run(WingetService.IsAvailable))
            {
                SetStatus("O winget não foi encontrado. Instale o \"Instalador de Aplicativo\" pela Microsoft Store.", "WarnBrush");
                return;
            }

            var list = await Task.Run(WingetService.Upgradable);
            _lastUpgrades = list;
            SetStatus(list.Count == 0 ? "Tudo em dia." : $"{list.Count} programa(s) com atualização.", list.Count == 0 ? "OkBrush" : "MutedBrush");
            Render(list);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException)
        {
            SetStatus("Não foi possível procurar: " + ex.Message, "BadBrush");
        }
    }

    /// <summary>Ícone de um programa do winget: o da lista, se for conhecido, senão as iniciais.</summary>
    private static FrameworkElement WingetIcon(string id, string name) => CompactPick.CatalogIcon(id, name);

    private void Render(IReadOnlyList<WingetApp> upgrades)
    {
        Body.Children.Clear();
        _upgrades.Clear();
        _installs.Clear();
        Body.Children.Add(Title("Atualizar programas", upgrades.Count == 0 ? "Procura versões novas pelo winget e atualiza um de cada vez." : $"{upgrades.Count} atualização(ões) encontrada(s)"));

        // barra: busca, filtros e ações
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var (searchBox, search) = CompactPick.Search("Buscar programa...", 240);
        search.Text = _query;
        DockPanel.SetDock(searchBox, Dock.Left);
        bar.Children.Add(searchBox);
        var update = CompactPick.Button("Atualizar selecionados", primary: true);
        update.Click += async (_, _) => await RunAsync(upgrade: true);
        var install = CompactPick.Button("Instalar selecionados");
        install.Click += async (_, _) => await RunAsync(upgrade: false);
        var again = CompactPick.Button("Procurar de novo");
        again.Click += async (_, _) => await SearchAsync();
        var stop = CompactPick.Button("Parar");
        stop.Click += (_, _) => _cts?.Cancel();
        foreach (var b in new[] { update, install, again, stop }) { DockPanel.SetDock(b, Dock.Right); bar.Children.Add(b); }
        var chips = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        bar.Children.Add(chips);
        Body.Children.Add(bar);

        if (upgrades.Count > 0)
        {
            var upGrid = CompactPick.Grid(6, 250);
            foreach (var app in upgrades)
            {
                var box = new CheckBox { IsChecked = true };
                _upgrades[app] = box;
                upGrid.Children.Add(CompactPick.Card(box, WingetIcon(app.Id, app.Name), app.Name, $"{app.Version} → {app.Available}", CompactPick.Tag("Nova versão", "AccentBrush")));
            }
            Body.Children.Add(upGrid);
        }

        Body.Children.Add(Section("INSTALAR FERRAMENTAS ÚTEIS"));
        var grid = CompactPick.Grid(6, 250);
        var chipList = new List<System.Windows.Controls.Primitives.ToggleButton>();
        var cats = new[] { "Todos" }.Concat(WingetService.Catalog.Select(a => a.Category).Distinct().OrderBy(c => c)).ToList();
        foreach (string cat in cats)
        {
            int n = cat == "Todos" ? WingetService.Catalog.Count : WingetService.Catalog.Count(a => a.Category == cat);
            var chip = new System.Windows.Controls.Primitives.ToggleButton { Content = $"{cat} {n}", Style = (Style)FindResource("ChipToggle"), Margin = new Thickness(0, 2, 6, 2), IsChecked = cat == _category };
            string c = cat;
            chip.Click += (_, _) => { _category = c; foreach (var o in chipList) o.IsChecked = o == chip; Fill(); };
            chipList.Add(chip);
            chips.Children.Add(chip);
        }
        void Fill()
        {
            grid.Children.Clear();
            foreach (var app in WingetService.Catalog)
            {
                if (!_installs.TryGetValue(app, out var box))
                {
                    box = new CheckBox();
                    _installs[app] = box;
                }
                bool show = (_category == "Todos" || app.Category == _category)
                    && (_query.Length == 0 || app.Title.Contains(_query, StringComparison.OrdinalIgnoreCase) || app.Description.Contains(_query, StringComparison.OrdinalIgnoreCase));
                if (show) grid.Children.Add(CompactPick.Card(box, CompactPick.CatalogIcon(app.Id, app.Title), app.Title, app.Description));
            }
        }
        search.TextChanged += (_, _) => { _query = search.Text.Trim(); Fill(); };
        Fill();
        Body.Children.Add(grid);
    }

    private string _category = "Todos", _query = "";

    private async Task RunAsync(bool upgrade)
    {
        var operations = new List<IOperation>();
        if (upgrade)
        {
            var chosen = _upgrades.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList();
            if (chosen.Count == 0) { SetStatus("Selecione ao menos um programa para atualizar.", "WarnBrush"); return; }
            // programa aberto: a atualização costuma falhar ou pedir para fechar no meio
            _inventory ??= await Task.Run(InstalledPrograms.List);
            var processes = await Task.Run(ProcessPaths);
            foreach (var app in chosen)
            {
                var program = InstalledPrograms.Match(_inventory, app.Name);
                var open = InstalledPrograms.RunningFrom(program?.InstallLocation, processes);
                if (open.Count > 0 && MessageBox.Show($"{app.Name} está aberto ({string.Join(", ", open)}).\n\nFeche o programa antes de atualizar. Atualizar mesmo assim?",
                        "Programa aberto", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) continue;
                operations.Add(new WingetOperation(app.Id, app.Name, upgrade: true));
            }
        }
        else
        {
            operations.AddRange(_installs.Where(p => p.Value.IsChecked == true).Select(p => (IOperation)new WingetOperation(p.Key.Id, p.Key.Title, upgrade: false)));
            if (operations.Count == 0) { SetStatus("Selecione ao menos um programa para instalar.", "WarnBrush"); return; }
        }
        if (operations.Count == 0) { SetStatus("Nada para fazer."); return; }
        await RunQueueAsync(operations, upgrade ? "Atualizando, um de cada vez" : "Instalando, um de cada vez");
        if (upgrade) await SearchAsync();
    }

    private async Task RunQueueAsync(IReadOnlyList<IOperation> operations, string title)
    {
        Body.Children.Clear();
        Body.Children.Add(Heading(title));
        var lines = operations.ToDictionary(o => o, o =>
        {
            var t = new TextBlock { Text = "◻  " + o.Info.Title + " — na fila", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2), Foreground = Brush("MutedBrush") };
            Body.Children.Add(t);
            return t;
        });
        void OnChanged(QueueItem item) => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!lines.TryGetValue(item.Operation, out var line)) return;
            string mark = item.State switch { OperationState.Running => "⏳", OperationState.Succeeded or OperationState.RebootRequired => "✔", OperationState.Pending => "◻", OperationState.Skipped => "–", _ => "✖" };
            line.Text = $"{mark}  {item.Operation.Info.Title} — {item.Message}";
            line.Foreground = Brush(item.State switch { OperationState.Succeeded => "OkBrush", OperationState.Failed or OperationState.Cancelled => "BadBrush", OperationState.Running => "TextBrush", _ => "MutedBrush" });
        }));

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        OperationQueue.Shared.Changed += OnChanged;
        try
        {
            SetStatus(title + "...");
            var results = await Task.Run(() => OperationQueue.Shared.Run(operations, null, token));
            int failed = results.Count(r => r.State == OperationState.Failed);
            bool reboot = results.Any(r => r.State == OperationState.RebootRequired);
            SetStatus($"{results.Count(r => r.State is OperationState.Succeeded or OperationState.RebootRequired)} concluído(s), {failed} com erro." + (reboot ? " Reinicie para terminar." : ""),
                failed == 0 ? "OkBrush" : "WarnBrush");
        }
        catch (InvalidOperationException ex)
        {
            SetStatus(ex.Message, "WarnBrush");
        }
        finally
        {
            OperationQueue.Shared.Changed -= OnChanged;
        }
    }

    private static IReadOnlyList<(string Name, string? Path)> ProcessPaths()
    {
        var list = new List<(string, string?)>();
        foreach (var p in Process.GetProcesses())
        {
            try { list.Add((p.ProcessName, p.MainModule?.FileName)); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            finally { p.Dispose(); }
        }
        return list;
    }

    // ---------------------------------------------------------------- inventário (Desinstalador)

    private async Task ShowInventoryAsync(bool keepStatus = false)
    {
        if (!keepStatus) SetStatus("Lendo os programas instalados...");
        _inventory = await Task.Run(InstalledPrograms.List);
        _removes.Clear();
        Body.Children.Clear();
        long total = _inventory.Sum(p => p.SizeBytes ?? 0);
        Body.Children.Add(Title("Desinstalador", $"{_inventory.Count} programas · {MBExtreme.Core.Diagnostics.DiagnosticRules.Size(total)} ocupados"));

        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var (searchBox, search) = CompactPick.Search("Buscar programa instalado...", 240);
        _inventorySearch = search;
        search.TextChanged += (_, _) => FillInventory();
        DockPanel.SetDock(searchBox, Dock.Left);
        bar.Children.Add(searchBox);
        _removeButton = CompactPick.Button("Desinstalar selecionados", primary: true);
        _removeButton.Click += async (_, _) => await UninstallSelectedAsync();
        var none = CompactPick.Button("Desmarcar");
        none.Click += (_, _) => { foreach (var b in _removes.Values) b.IsChecked = false; };
        var sort = new ComboBox { Width = 150, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Ordenar" };
        foreach (var o in new[] { "Nome", "Tamanho", "Mais recentes" }) sort.Items.Add("Ordenar: " + o);
        sort.SelectedIndex = Array.IndexOf(new[] { "Nome", "Tamanho", "Mais recentes" }, _inventorySort);
        sort.SelectionChanged += (_, _) => { _inventorySort = new[] { "Nome", "Tamanho", "Mais recentes" }[Math.Max(0, sort.SelectedIndex)]; FillInventory(); };
        foreach (FrameworkElement b in new FrameworkElement[] { _removeButton, none, sort }) { DockPanel.SetDock(b, Dock.Right); bar.Children.Add(b); }
        bar.Children.Add(UiTree.Free(_inventoryChips));
        Body.Children.Add(bar);
        BuildInventoryChips();
        Body.Children.Add(UiTree.Free(_inventoryGrid));
        FillInventory();
        UpdateRemoveButton();
        if (!keepStatus) SetStatus("Marque os programas e clique em Desinstalar selecionados: vai um de cada vez, com ponto de restauração antes.");
    }

    private static bool IsBig(InstalledProgram p) => p.SizeBytes is >= 500L * 1024 * 1024;
    private static bool IsRecent(InstalledProgram p) => p.InstalledOn is { } d && d >= DateTime.Today.AddDays(-30);

    private void BuildInventoryChips()
    {
        if (_inventory is null) return;
        _inventoryChips.Children.Clear();
        var filters = new (string Name, Func<InstalledProgram, bool> Test)[]
        {
            ("Todos", _ => true), ("Grandes", IsBig), ("Recentes", IsRecent), ("Microsoft Store", p => p.Source == ProgramSource.Store),
        };
        foreach (var (name, test) in filters)
        {
            string n = name;
            var chip = new System.Windows.Controls.Primitives.ToggleButton
            {
                Content = $"{name} {_inventory.Count(test)}", Style = (Style)FindResource("ChipToggle"), Margin = new Thickness(0, 2, 6, 2), IsChecked = name == _inventoryFilter,
            };
            chip.Click += (_, _) => { _inventoryFilter = n; BuildInventoryChips(); FillInventory(); };
            _inventoryChips.Children.Add(chip);
        }
    }

    private void FillInventory()
    {
        if (_inventory is null) return;
        _inventoryGrid.Children.Clear();
        string query = _inventorySearch?.Text.Trim() ?? "";
        IEnumerable<InstalledProgram> list = _inventory.Where(p => query.Length == 0 || p.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || p.Publisher.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        list = _inventoryFilter switch
        {
            "Grandes" => list.Where(IsBig),
            "Recentes" => list.Where(IsRecent),
            "Microsoft Store" => list.Where(p => p.Source == ProgramSource.Store),
            _ => list,
        };
        list = _inventorySort switch
        {
            "Tamanho" => list.OrderByDescending(p => p.SizeBytes ?? 0),
            "Mais recentes" => list.OrderByDescending(p => p.InstalledOn ?? DateTime.MinValue),
            _ => list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
        };
        var shown = list.Take(400).ToList();
        foreach (var program in shown)
        {
            if (!_removes.TryGetValue(program, out var box))
            {
                box = new CheckBox { IsEnabled = program.Source == ProgramSource.Store || program.UninstallCommand is not null };
                box.Checked += (_, _) => UpdateRemoveButton();
                box.Unchecked += (_, _) => UpdateRemoveButton();
                _removes[program] = box;
            }
            var details = new List<string>();
            if (program.Publisher.Length > 0) details.Add(program.Publisher);
            if (program.SizeBytes is { } size) details.Add(MBExtreme.Core.Diagnostics.DiagnosticRules.Size(size));
            if (program.InstalledOn is { } date) details.Add(date.ToString("dd/MM/yyyy"));
            TextBlock? tag = program.Source == ProgramSource.Store ? CompactPick.Tag("Store") : IsRecent(program) ? CompactPick.Tag("Novo", "AccentBrush") : IsBig(program) ? CompactPick.Tag("Grande") : null;
            var card = CompactPick.Card(box, CompactPick.ProgramIcon(program.IconPath, program.Name), program.Name, string.Join(" · ", details), tag);
            card.ToolTip = $"{program.Name} {program.Version}".Trim() + (box.IsEnabled ? "" : "\nEste programa não informou como desinstalar. Use Configurações > Aplicativos.");
            _inventoryGrid.Children.Add(card);
        }
        if (shown.Count == 0) _inventoryGrid.Children.Add(Muted("Nenhum programa encontrado."));
    }

    private void UpdateRemoveButton()
    {
        if (_removeButton is null) return;
        var chosen = _removes.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList();
        long bytes = chosen.Sum(p => p.SizeBytes ?? 0);
        _removeButton.Content = chosen.Count == 0 ? "Desinstalar selecionados" : $"Desinstalar {chosen.Count} selecionado(s)" + (bytes > 0 ? " · " + MBExtreme.Core.Diagnostics.DiagnosticRules.Size(bytes) : "");
        _removeButton.IsEnabled = chosen.Count > 0;
    }

    private async Task UninstallSelectedAsync()
    {
        var chosen = _removes.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList();
        if (chosen.Count == 0) return;
        var processes = await Task.Run(ProcessPaths);
        var open = chosen.SelectMany(p => InstalledPrograms.RunningFrom(p.InstallLocation, processes).Select(x => $"{p.Name} ({x})")).ToList();
        string list = string.Join("\n", chosen.Take(12).Select(p => "• " + p.Name)) + (chosen.Count > 12 ? $"\n... e mais {chosen.Count - 12}" : "");
        string warning = open.Count > 0 ? "\n\nAbertos agora (feche antes): " + string.Join(", ", open) : "";
        if (MessageBox.Show($"Desinstalar {chosen.Count} programa(s)?\n\n{list}{warning}\n\nAntes, o MB cria um ponto de restauração. Cada programa pode abrir a janela do próprio desinstalador.",
                "Desinstalar", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        SetStatus("Criando ponto de restauração...");
        var (ok, error) = await Task.Run(() => MBExtreme.Core.Windows.RestorePoints.CreateNow("MB Extreme: antes de desinstalar programas"));
        if (!ok && MessageBox.Show($"Não deu para criar o ponto de restauração ({error}).\n\nDesinstalar mesmo assim?", "Ponto de restauração",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) { SetStatus("Cancelado."); return; }
        await RunQueueAsync(chosen.Select(p => (IOperation)new UninstallOperation(p)).ToList(), "Desinstalando, um de cada vez");
        _inventory = null;
        await ShowInventoryAsync(keepStatus: true);
    }

    private async Task UninstallAsync(InstalledProgram program)
    {
        var open = InstalledPrograms.RunningFrom(program.InstallLocation, await Task.Run(ProcessPaths));
        string warning = open.Count > 0 ? $"\n\n{program.Name} está aberto ({string.Join(", ", open)}): feche antes." : "";
        if (MessageBox.Show($"Desinstalar {program.Name}?{warning}\n\nA janela do desinstalador do próprio programa vai aparecer. Não tem volta pelo MB Extreme.",
                "Desinstalar", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await RunQueueAsync(new IOperation[] { new UninstallOperation(program) }, "Desinstalando");
        _inventory = null;
        await ShowInventoryAsync(keepStatus: true);
    }
}
