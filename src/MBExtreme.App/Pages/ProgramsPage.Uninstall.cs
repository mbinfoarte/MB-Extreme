using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Apps;
using MBExtreme.Core.Diagnostics;
using MBExtreme.Core.Operations;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Desinstalador: tamanho medido de verdade e data de instalação em cada programa; remoção sem cliques (MSI, Inno, NSIS e comando
/// silencioso) com barra de progresso por etapas; depois procura os restos (pastas, atalhos e registro) e apaga o que é certeza,
/// com cópia do registro e pastas na Lixeira.
/// </summary>
public sealed partial class ProgramsPage
{
    private IReadOnlyList<InstalledProgram>? _inventory;
    // disco de cada programa (C:, D:...; null = local desconhecido) e o filtro por disco ('*' = todos, '?' = desconhecido)
    private Dictionary<InstalledProgram, (char? Drive, string? Folder)> _locations = new();
    private char _driveFilter = '*';
    private readonly WrapPanel _driveChips = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly Dictionary<InstalledProgram, CheckBox> _removes = new();
    private readonly Dictionary<string, StackPanel> _sizeBlocks = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ProgramMeasure> _measures = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Controls.Primitives.UniformGrid _inventoryGrid = CompactPick.Grid(5, 290);
    private TextBox? _inventorySearch;
    private readonly WrapPanel _inventoryChips = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    private readonly TextBlock _inventoryHeader = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 0, 2) };
    private Button? _removeButton;
    private string _inventoryFilter = "Todos", _inventorySort = "Maiores primeiro";
    private CancellationTokenSource? _measureCts;
    private int _measuring;

    private static readonly string[] Sorts = { "Nome", "Maiores primeiro", "Mais recentes", "Mais antigos" };
    private static readonly Brush Orange = Frozen(Color.FromRgb(0xFF, 0xA9, 0x4D));

    private static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    // ---------------------------------------------------------------- inventário

    private long? SizeOf(InstalledProgram p) => _measures.TryGetValue(ProgramSizes.KeyOf(p), out var m) ? m.Bytes : p.SizeBytes;
    private DateTime? DateOf(InstalledProgram p) => _measures.TryGetValue(ProgramSizes.KeyOf(p), out var m) ? m.InstalledOn : p.InstalledOn;

    private async Task ShowInventoryAsync(bool keepStatus = false)
    {
        if (!keepStatus) SetStatus("Lendo os programas instalados...");
        _measureCts?.Cancel();
        _inventory = await Task.Run(InstalledPrograms.List);
        var programs = _inventory;
        _locations = await Task.Run(() => programs.ToDictionary(p => p, p => ProgramSizes.LocationOf(p)));
        _measures = await Task.Run(() => ProgramSizes.Load());
        _removes.Clear();
        _sizeBlocks.Clear();
        Body.Children.Clear();

        var title = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        title.Children.Add(new TextBlock { Text = "Desinstalador", FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Bottom });
        _inventoryHeader.Foreground = Brush("MutedBrush");
        title.Children.Add(UiTree.Free(_inventoryHeader));
        Body.Children.Add(title);

        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var (searchBox, search) = CompactPick.Search("Buscar programa instalado...", 240);
        _inventorySearch = search;
        search.TextChanged += (_, _) => FillInventory();
        DockPanel.SetDock(searchBox, Dock.Left);
        bar.Children.Add(searchBox);
        var sort = new ComboBox { Width = 190, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Ordenar" };
        foreach (var o in Sorts) sort.Items.Add("Ordenar: " + o);
        sort.SelectedIndex = Math.Max(0, Array.IndexOf(Sorts, _inventorySort));
        sort.SelectionChanged += (_, _) => { _inventorySort = Sorts[Math.Max(0, sort.SelectedIndex)]; FillInventory(); };
        var remeasure = CompactPick.Button("↻ Medir de novo");
        remeasure.ToolTip = "Mede de novo o tamanho de todos os programas";
        remeasure.Click += (_, _) => StartMeasuring(all: true);
        foreach (FrameworkElement b in new FrameworkElement[] { sort, remeasure }) { DockPanel.SetDock(b, Dock.Right); bar.Children.Add(b); }
        bar.Children.Add(UiTree.Free(_inventoryChips));
        Body.Children.Add(bar);
        Body.Children.Add(UiTree.Free(_driveChips));
        BuildInventoryChips();
        Body.Children.Add(UiTree.Free(_inventoryGrid));

        // rodapé: legenda, total e o botão
        var foot = new DockPanel();
        _removeButton = CompactPick.Button("Desinstalar selecionados", primary: true);
        _removeButton.MinHeight = 36;
        _removeButton.Click += async (_, _) => await UninstallSelectedAsync();
        var none = CompactPick.Button("Desmarcar");
        none.Click += (_, _) => { foreach (var b in _removes.Values) b.IsChecked = false; };
        DockPanel.SetDock(_removeButton, Dock.Right);
        DockPanel.SetDock(none, Dock.Right);
        foot.Children.Add(_removeButton);
        foot.Children.Add(none);
        var legend = new TextBlock { FontSize = 11.5, Foreground = Brush("MutedBrush"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        legend.Inlines.Add("Tamanho medido na pasta do programa · ");
        legend.Inlines.Add(new System.Windows.Documents.Run("~") { FontWeight = FontWeights.Bold, Foreground = Brush("TextBrush") });
        legend.Inlines.Add(" = estimado pelo Windows · ");
        legend.Inlines.Add(new System.Windows.Documents.Run("●") { Foreground = Orange });
        legend.Inlines.Add(" = pede confirmação na janela dele");
        foot.Children.Add(legend);
        var card = CardOf(foot);
        card.Margin = new Thickness(0, 6, 0, 0);
        Body.Children.Add(card);

        FillInventory();
        UpdateRemoveButton();
        UpdateHeader();
        StartMeasuring(all: false);
        if (!keepStatus) SetStatus("Marque os programas e clique em Desinstalar: remove sem janelas quando o programa aceita, com ponto de restauração antes.");
    }

    private void UpdateHeader()
    {
        if (_inventory is null) return;
        long total = _inventory.Sum(p => SizeOf(p) ?? 0);
        _inventoryHeader.Text = $"{_inventory.Count} programas · {DiagnosticRules.Size(total)} ocupados" + (_measuring > 0 ? $" · medindo {_measuring}..." : "");
    }

    /// <summary>Mede em segundo plano (um de cada vez, sem travar a tela) e atualiza cada cartão quando termina.</summary>
    private void StartMeasuring(bool all)
    {
        if (_inventory is null) return;
        _measureCts?.Cancel();
        var cts = _measureCts = new CancellationTokenSource();
        var todo = _inventory.Where(p => all || !_measures.ContainsKey(ProgramSizes.KeyOf(p))).OrderByDescending(p => p.SizeBytes ?? 0).ToList();
        if (todo.Count == 0) return;
        _measuring = todo.Count;
        UpdateHeader();
        foreach (var p in todo) if (_sizeBlocks.TryGetValue(ProgramSizes.KeyOf(p), out var block)) FillSize(block, p, measuring: true);
        _ = Task.Run(() =>
        {
            foreach (var program in todo)
            {
                if (cts.IsCancellationRequested) return;
                ProgramMeasure m;
                try { m = ProgramSizes.MeasureProgram(program, cts.Token); }
                catch (OperationCanceledException) { return; }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    _measures[ProgramSizes.KeyOf(program)] = m;
                    _measuring = Math.Max(0, _measuring - 1);
                    if (_sizeBlocks.TryGetValue(ProgramSizes.KeyOf(program), out var block)) FillSize(block, program, measuring: false);
                    UpdateHeader();
                    UpdateRemoveButton();
                    if (_measuring == 0) { ProgramSizes.Save(_measures); BuildInventoryChips(); }
                }));
            }
        }, cts.Token);
    }

    private void FillSize(StackPanel block, InstalledProgram p, bool measuring)
    {
        block.Children.Clear();
        if (measuring && !_measures.ContainsKey(ProgramSizes.KeyOf(p)))
        {
            var t = new TextBlock { Text = "medindo…", FontSize = 11, Foreground = Brush("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Right };
            t.Opacity = 0.7;
            block.Children.Add(t);
            return;
        }
        _measures.TryGetValue(ProgramSizes.KeyOf(p), out var m);
        long? size = m?.Bytes ?? p.SizeBytes;
        bool estimated = m is null ? p.SizeBytes is not null : m.Estimated;
        bool big = size is >= 500L * 1024 * 1024;
        block.Children.Add(new TextBlock
        {
            Text = size is { } s ? (estimated ? "~" : "") + DiagnosticRules.Size(s) : "—", FontSize = 12.5, FontWeight = FontWeights.Bold,
            Foreground = big ? Brush("AccentBrush") : Brush("TextBrush"), HorizontalAlignment = HorizontalAlignment.Right,
            ToolTip = estimated ? "Estimado pelo Windows (não deu para medir a pasta)" : "Medido na pasta do programa",
        });
        var date = m?.InstalledOn ?? p.InstalledOn;
        block.Children.Add(new TextBlock
        {
            Text = date is { } d ? (m?.DateFromFolder == true ? "pasta de " : "Instalado em ") + d.ToString("dd/MM/yyyy") : "data desconhecida",
            FontSize = 10.5, Foreground = Brush("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Right,
        });
    }

    private bool IsBig(InstalledProgram p) => SizeOf(p) is >= 500L * 1024 * 1024;
    private bool IsRecent(InstalledProgram p) => DateOf(p) is { } d && d >= DateTime.Today.AddDays(-30);

    private char? DriveOf(InstalledProgram p) => _locations.TryGetValue(p, out var l) ? l.Drive : null;

    private static string DriveName(char d)
    {
        try
        {
            var info = new System.IO.DriveInfo(d + ":\\");
            if (!info.IsReady) return $"{d}:";
            string label = info.VolumeLabel.Trim();
            string kind = info.DriveType == System.IO.DriveType.Removable ? "removível" : "";
            return $"{d}: " + (label.Length > 0 ? label : kind.Length > 0 ? kind : d == 'C' ? "Windows" : "disco");
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException) { return $"{d}:"; }
    }

    /// <summary>Cartões dos discos: todos, cada letra (programas e espaço) e local desconhecido. Clicar filtra a lista.</summary>
    private void BuildDriveChips()
    {
        _driveChips.Children.Clear();
        if (_inventory is null) return;
        var groups = new List<(char Key, string Title, List<InstalledProgram> List)> { ('*', "Todos os discos", _inventory.ToList()) };
        foreach (var g in _inventory.Where(p => DriveOf(p) is not null).GroupBy(p => DriveOf(p)!.Value).OrderBy(g => g.Key))
            groups.Add((g.Key, DriveName(g.Key), g.ToList()));
        var unknown = _inventory.Where(p => DriveOf(p) is null).ToList();
        if (unknown.Count > 0) groups.Add(('?', "Local desconhecido", unknown));
        if (groups.Count <= 2 && unknown.Count == 0) return;   // tudo num disco só: não precisa dos cartões
        foreach (var (key, title, list) in groups)
        {
            char k = key;
            long bytes = list.Sum(p => SizeOf(p) ?? 0);
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 12.5 });
            text.Children.Add(new TextBlock { Text = $"{list.Count} programa(s)" + (bytes > 0 ? " · " + DiagnosticRules.Size(bytes) : ""), FontSize = 11, Opacity = 0.8 });
            var chip = new System.Windows.Controls.Primitives.ToggleButton
            {
                Content = text, Style = (Style)FindResource("ChipToggle"), Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6), IsChecked = k == _driveFilter,
            };
            chip.Click += (_, _) => { _driveFilter = k; BuildDriveChips(); FillInventory(); };
            _driveChips.Children.Add(chip);
        }
    }

    private void BuildInventoryChips()
    {
        if (_inventory is null) return;
        BuildDriveChips();
        _inventoryChips.Children.Clear();
        var filters = new (string Name, Func<InstalledProgram, bool> Test)[]
        {
            ("Todos", _ => true), ("Grandes (+500 MB)", IsBig), ("Recentes", IsRecent), ("Microsoft Store", p => p.Source == ProgramSource.Store),
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
        if (_driveFilter == '?') list = list.Where(p => DriveOf(p) is null);
        else if (_driveFilter != '*') list = list.Where(p => DriveOf(p) == _driveFilter);
        list = _inventoryFilter switch
        {
            "Grandes (+500 MB)" => list.Where(IsBig),
            "Recentes" => list.Where(IsRecent),
            "Microsoft Store" => list.Where(p => p.Source == ProgramSource.Store),
            _ => list,
        };
        list = _inventorySort switch
        {
            "Maiores primeiro" => list.OrderByDescending(p => SizeOf(p) ?? 0),
            "Mais recentes" => list.OrderByDescending(p => DateOf(p) ?? DateTime.MinValue),
            "Mais antigos" => list.OrderBy(p => DateOf(p) ?? DateTime.MaxValue),
            _ => list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
        };
        var shown = list.Take(400).ToList();
        foreach (var program in shown)
        {
            var plan = SilentUninstall.Plan(program);
            if (!_removes.TryGetValue(program, out var box))
            {
                box = new CheckBox { IsEnabled = plan is not null };
                box.Checked += (_, _) => UpdateRemoveButton();
                box.Unchecked += (_, _) => UpdateRemoveButton();
                _removes[program] = box;
            }
            string key = ProgramSizes.KeyOf(program);
            if (!_sizeBlocks.TryGetValue(key, out var block))
            {
                block = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                _sizeBlocks[key] = block;
                FillSize(block, program, measuring: _measuring > 0);
            }
            var where = _locations.TryGetValue(program, out var loc) ? loc : (null, null);
            string detail = (where.Drive is { } dl ? $"{dl}: · " : "? · ") + (program.Source == ProgramSource.Store ? "Microsoft Store" : program.Publisher);
            var card = CompactPick.Card(box, CompactPick.ProgramIcon(program.IconPath, program.Name), program.Name, detail, block);
            card.Height = 48;
            if (plan is { Silent: false } && card.Child is DockPanel row && row.Children.OfType<StackPanel>().LastOrDefault()?.Children[0] is TextBlock name)
            {
                name.Inlines.Add(new System.Windows.Documents.Run("  ●") { Foreground = Orange, FontSize = 10 });
            }
            card.ToolTip = $"{program.Name} {program.Version}".Trim() + "\n" + (where.Folder is { } folder ? "Pasta: " + folder : where.Drive is { } d2 ? $"Disco {d2}: (a pasta não foi encontrada)" : "Local desconhecido (o programa não informou a pasta)") + "\n" + (plan is null ? "Este programa não informou como desinstalar. Use Configurações > Aplicativos." : "Remoção: " + plan.Label);
            _inventoryGrid.Children.Add(card);
        }
        if (shown.Count == 0) _inventoryGrid.Children.Add(Muted("Nenhum programa encontrado."));
    }

    private void UpdateRemoveButton()
    {
        if (_removeButton is null) return;
        var chosen = _removes.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList();
        long bytes = chosen.Sum(p => SizeOf(p) ?? 0);
        var drives = chosen.Select(DriveOf).Where(d => d is not null).Distinct().ToList();
        string where = drives.Count == 1 ? $" no {drives[0]}:" : "";
        _removeButton.Content = chosen.Count == 0 ? "Desinstalar selecionados" : $"Desinstalar {chosen.Count}" + (bytes > 0 ? " · libera " + DiagnosticRules.Size(bytes) + where : "");
        _removeButton.IsEnabled = chosen.Count > 0;
    }

    // ---------------------------------------------------------------- desinstalando

    private async Task UninstallSelectedAsync()
    {
        var chosen = _removes.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList();
        if (chosen.Count == 0) return;
        var processes = await Task.Run(ProcessPaths);
        var open = chosen.SelectMany(p => InstalledPrograms.RunningFrom(p.InstallLocation, processes).Select(x => $"{p.Name} ({x})")).ToList();
        var ask = chosen.Where(p => SilentUninstall.Plan(p) is { Silent: false }).Select(p => p.Name).ToList();
        string list = string.Join("\n", chosen.Take(12).Select(p => "• " + p.Name)) + (chosen.Count > 12 ? $"\n... e mais {chosen.Count - 12}" : "");
        string warning = open.Count > 0 ? "\n\nAbertos agora (feche antes): " + string.Join(", ", open) : "";
        string asks = ask.Count > 0 ? $"\n\nPedem confirmação na janela deles: {string.Join(", ", ask)}." : "\n\nTodos saem sem janelas para clicar.";
        if (MessageBox.Show($"Desinstalar {chosen.Count} programa(s)?\n\n{list}{warning}{asks}\n\nAntes, o MB cria um ponto de restauração. Depois procura e limpa os restos.",
                "Desinstalar", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _measureCts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var progress = new UninstallProgressView(this, chosen);
        Body.Children.Clear();
        Body.Children.Add(progress.Root);
        progress.Cancel.Click += (_, _) => { _cts?.Cancel(); progress.Cancel.IsEnabled = false; progress.Cancel.Content = "Para depois deste"; };

        progress.StepRunning(0, "Criando o ponto de restauração...");
        var (ok, error) = await Task.Run(() => MBExtreme.Core.Windows.RestorePoints.CreateNow("MB Extreme: antes de desinstalar programas"));
        if (!ok && MessageBox.Show($"Não deu para criar o ponto de restauração ({error}).\n\nDesinstalar mesmo assim?", "Ponto de restauração",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            SetStatus("Cancelado.");
            await ShowInventoryAsync(keepStatus: true);
            return;
        }
        progress.StepDone(0, ok ? "Ponto de restauração criado" : "Sem ponto de restauração", "");

        // pastas de programas que continuam instalados: um resto que contém uma delas nunca vem marcado
        var keepFolders = await Task.Run(() => (_inventory ?? Array.Empty<InstalledProgram>()).Where(p => !chosen.Contains(p))
            .Select(ProgramSizes.FolderOf).Where(f => f is not null).Select(f => f!.TrimEnd('\\') + "\\").ToList());
        var leftovers = new List<Leftover>();
        long freed = 0;
        int removed = 0, failed = 0;
        bool reboot = false;
        for (int i = 0; i < chosen.Count; i++)
        {
            var program = chosen[i];
            if (token.IsCancellationRequested) { progress.StepSkipped(i + 1); continue; }
            var op = new UninstallOperation(program);
            string? folder = ProgramSizes.FolderOf(program);
            progress.StepRunning(i + 1, op.Plan is { Silent: false } ? $"Removendo {program.Name} · termine na janela dele" : $"Removendo {program.Name}", op.Plan?.Silent == false);
            QueueItem result;
            try { result = (await Task.Run(() => OperationQueue.Shared.Run(new IOperation[] { op }, null, CancellationToken.None)))[0]; }
            catch (InvalidOperationException ex) { progress.StepFailed(i + 1, ex.Message); failed++; continue; }
            if (result.State is OperationState.Succeeded or OperationState.RebootRequired)
            {
                removed++;
                reboot |= result.State == OperationState.RebootRequired;
                long size = SizeOf(program) ?? 0;
                freed += size;
                progress.StepDone(i + 1, $"{program.Name} removido", size > 0 ? DiagnosticRules.Size(size) : "");
                progress.Freed(freed);
                if (program.Source == ProgramSource.Win32)
                    leftovers.AddRange((await Task.Run(() => LeftoverFinder.Find(program, folder))).Select(l =>
                        l.Type == LeftoverType.Folder && keepFolders.Any(k => k.StartsWith(l.Path.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                            ? l with { Sure = false, Why = "outro programa instalado usa esta pasta" } : l));
            }
            else
            {
                failed++;
                progress.StepFailed(i + 1, $"{program.Name}: {result.Message}");
            }
        }
        progress.StepRunning(chosen.Count + 1, "Procurando restos...");
        progress.StepDone(chosen.Count + 1, $"{leftovers.Count} resto(s) encontrados", "");
        SetStatus($"{removed} removido(s), {failed} com problema." + (reboot ? " Reinicie para terminar." : ""), failed == 0 ? "OkBrush" : "WarnBrush");

        _inventory = null;
        if (leftovers.Count == 0) { await ShowInventoryAsync(keepStatus: true); return; }
        if (AppServices.Settings.UninstallAutoLeftovers)
        {
            var sure = leftovers.Where(l => l.Sure).ToList();
            var (done, bad, bytes) = await Task.Run(() => DeleteLeftovers(sure, "restos de " + string.Join(", ", chosen.Select(c => c.Name).Take(3))));
            leftovers = leftovers.Where(l => !l.Sure).ToList();
            SetStatus($"{removed} removido(s) e {done} resto(s) apagados sozinhos ({DiagnosticRules.Size(bytes)})." + (bad > 0 ? $" {bad} não deu para apagar." : ""), bad == 0 ? "OkBrush" : "WarnBrush");
            if (leftovers.Count == 0) { await ShowInventoryAsync(keepStatus: true); return; }
        }
        ShowLeftovers(leftovers, removed, freed);
    }

    // ---------------------------------------------------------------- restos

    private void ShowLeftovers(IReadOnlyList<Leftover> leftovers, int removed, long freed)
    {
        Body.Children.Clear();
        var title = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        title.Children.Add(new TextBlock { Text = "Restos encontrados", FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Bottom });
        title.Children.Add(new TextBlock
        {
            Text = $"{removed} programa(s) removido(s) · {DiagnosticRules.Size(freed)} liberados · sobraram {leftovers.Count} itens ({DiagnosticRules.Size(leftovers.Sum(l => l.Bytes))})",
            FontSize = 12, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 0, 2),
        });
        Body.Children.Add(title);

        var boxes = new List<(CheckBox Box, Leftover Item)>();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var sure = leftovers.Where(l => l.Sure).ToList();
        var doubt = leftovers.Where(l => !l.Sure).ToList();
        var left = LeftoverColumn("Com certeza eram deles", "apagados ao confirmar", Color.FromRgb(0x3D, 0xDC, 0x97), sure, true, boxes);
        var right = LeftoverColumn("Confira antes", "podem ser usados por outro programa", Color.FromRgb(0xFF, 0xA9, 0x4D), doubt, false, boxes);
        var safety = new TextBlock { Text = "🛡  Antes de apagar: cópia do registro (.reg) e pastas na Lixeira. O registro volta pela Central Desfazer.", FontSize = 11.5, Foreground = Brush("MutedBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        ((StackPanel)((Border)right).Child).Children.Add(safety);
        grid.Children.Add(left);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        Body.Children.Add(grid);

        var foot = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var apply = CompactPick.Button("Apagar", primary: true);
        apply.MinHeight = 36;
        var keep = CompactPick.Button("Manter tudo");
        void Count()
        {
            var on = boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Item).ToList();
            apply.Content = $"Apagar {on.Count} itens · {DiagnosticRules.Size(on.Sum(l => l.Bytes))}";
            apply.IsEnabled = on.Count > 0;
        }
        foreach (var (box, _) in boxes) { box.Checked += (_, _) => Count(); box.Unchecked += (_, _) => Count(); }
        Count();
        keep.Click += async (_, _) => await ShowInventoryAsync(keepStatus: true);
        apply.Click += async (_, _) =>
        {
            apply.IsEnabled = false;
            var on = boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Item).ToList();
            SetStatus("Apagando os restos...");
            var (done, bad, bytes) = await Task.Run(() => DeleteLeftovers(on, "restos de programas"));
            SetStatus($"{done} resto(s) apagados ({DiagnosticRules.Size(bytes)} liberados)." + (bad > 0 ? $" {bad} não deu para apagar (em uso ou sem permissão)." : ""), bad == 0 ? "OkBrush" : "WarnBrush");
            await ShowInventoryAsync(keepStatus: true);
        };
        var auto = new CheckBox { Content = "Sempre apagar sozinho o que é certeza", IsChecked = AppServices.Settings.UninstallAutoLeftovers, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        auto.Checked += (_, _) => { AppServices.Settings.UninstallAutoLeftovers = true; AppServices.Settings.Save(); };
        auto.Unchecked += (_, _) => { AppServices.Settings.UninstallAutoLeftovers = false; AppServices.Settings.Save(); };
        DockPanel.SetDock(apply, Dock.Right);
        DockPanel.SetDock(keep, Dock.Right);
        foot.Children.Add(apply);
        foot.Children.Add(keep);
        foot.Children.Add(auto);
        Body.Children.Add(foot);
    }

    private Border LeftoverColumn(string title, string sub, Color color, IReadOnlyList<Leftover> items, bool on, List<(CheckBox, Leftover)> boxes)
    {
        var st = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var count = new TextBlock { Text = $"{items.Count} itens · {DiagnosticRules.Size(items.Sum(i => i.Bytes))}", FontSize = 11.5, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(count, Dock.Right);
        head.Children.Add(count);
        head.Children.Add(new TextBlock { Text = "●  ", Foreground = new SolidColorBrush(color), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(new TextBlock { Text = " · " + sub, FontSize = 11.5, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center });
        st.Children.Add(head);
        if (items.Count == 0) st.Children.Add(Muted("Nada aqui.", 12));
        foreach (var item in items)
        {
            var box = new CheckBox { IsChecked = on };
            var tag = CompactPick.Tag(item.Bytes > 0 ? DiagnosticRules.Size(item.Bytes) : item.Why);
            var icon = new TextBlock
            {
                Text = item.Type switch { LeftoverType.Folder => "", LeftoverType.RegistryKey => "", _ => "" },
                FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, Foreground = Brush("AccentBrush"), TextAlignment = TextAlignment.Center,
            };
            var card = CompactPick.Card(box, icon, item.Path, $"{item.TypeText} · {item.Why} · {item.Program}", tag);
            card.ToolTip = item.Path;
            st.Children.Add(card);
            boxes.Add((box, item));
        }
        var border = CardOf(st);
        border.VerticalAlignment = VerticalAlignment.Top;
        return border;
    }

    /// <summary>Apaga os restos: pastas e atalhos para a Lixeira, chaves do registro com cópia (.reg) e registro na Central Desfazer.</summary>
    private static (int Done, int Failed, long Bytes) DeleteLeftovers(IReadOnlyList<Leftover> items, string label)
    {
        int done = 0, failed = 0;
        long bytes = 0;
        foreach (var item in items.Where(i => i.Type != LeftoverType.RegistryKey))
        {
            try
            {
                if (item.Type == LeftoverType.Folder)
                {
                    if (!LeftoverGuard.CanDeleteFolder(item.Path) || !Directory.Exists(item.Path)) { failed++; continue; }
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(item.Path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                else
                {
                    if (!File.Exists(item.Path) || !item.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) { failed++; continue; }
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(item.Path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                done++;
                bytes += item.Bytes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException) { failed++; }
        }
        if (done > 0)
            OperationJournal.Shared.Record(new OperationInfo("leftovers.files", OperationKind.Cleanup, $"Restos apagados ({done} pastas e atalhos)"), OperationResult.Ok("Foram para a Lixeira."), DateTimeOffset.Now);

        var keys = items.Where(i => i.Type == LeftoverType.RegistryKey).ToList();
        if (keys.Count > 0)
        {
            string backup = RegistryBackup.NewFolder(label);
            int keyDone = 0;
            foreach (var key in keys)
            {
                if (!LeftoverGuard.CanDeleteKey(key.Path) || !RegistryBackup.Export(key.Path, backup) || !RegistryBackup.DeleteKey(key.Path)) { failed++; continue; }
                keyDone++;
            }
            done += keyDone;
            if (keyDone > 0)
                OperationJournal.Shared.Record(new OperationInfo("leftovers.registry", OperationKind.Registry, $"Registro: {keyDone} chave(s) de programas removidos", TargetId: backup, Reversible: true),
                    OperationResult.Ok("Cópia guardada antes de apagar."), DateTimeOffset.Now);
        }
        return (done, failed, bytes);
    }

    /// <summary>Tela "Desinstalando": barra com a porcentagem e as etapas (ponto de restauração, cada programa, restos).</summary>
    private sealed class UninstallProgressView
    {
        private readonly ProgramsPage _page;
        private readonly int _total;
        private readonly ProgressBar _bar = new() { Height = 10, Minimum = 0, Maximum = 100 };
        private readonly TextBlock _title = new() { FontSize = 15, FontWeight = FontWeights.SemiBold };
        private readonly TextBlock _percent = new() { FontSize = 15, FontWeight = FontWeights.Bold };
        private readonly TextBlock _sub = new() { FontSize = 11.5 };
        private readonly TextBlock _freed = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        private readonly List<(TextBlock Mark, TextBlock Text, TextBlock Right)> _steps = new();
        private readonly DateTime _start = DateTime.Now;
        private int _done;

        public StackPanel Root { get; } = new();
        public Button Cancel { get; } = CompactPick.Button("Cancelar os próximos");

        public UninstallProgressView(ProgramsPage page, IReadOnlyList<InstalledProgram> programs)
        {
            _page = page;
            _total = programs.Count + 2;
            var title = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            title.Children.Add(new TextBlock { Text = "Desinstalando", FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Bottom });
            int silent = programs.Count(p => SilentUninstall.Plan(p)?.Silent == true);
            title.Children.Add(new TextBlock
            {
                Text = silent == programs.Count ? $"{programs.Count} programa(s) · sem janelas para clicar" : $"{programs.Count} programa(s) · {programs.Count - silent} pedem confirmação",
                FontSize = 12, Foreground = page.Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 0, 2),
            });
            Root.Children.Add(title);

            var top = new StackPanel();
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            _percent.Foreground = page.Brush("AccentBrush");
            DockPanel.SetDock(_percent, Dock.Right);
            line.Children.Add(_percent);
            line.Children.Add(_title);
            top.Children.Add(line);
            top.Children.Add(_bar);
            _sub.Foreground = page.Brush("MutedBrush");
            _sub.Margin = new Thickness(0, 6, 0, 0);
            top.Children.Add(_sub);
            Root.Children.Add(page.CardOf(top));

            var list = new StackPanel();
            list.Children.Add(new TextBlock { Text = "ETAPAS", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = page.Brush("AccentBrush"), Margin = new Thickness(0, 0, 0, 6) });
            AddStep(list, "Ponto de restauração", "");
            foreach (var p in programs)
                AddStep(list, p.Name, SilentUninstall.Plan(p)?.Label ?? "");
            AddStep(list, "Procurar e limpar os restos de cada um", "");
            var card = page.CardOf(list);
            card.Margin = new Thickness(0, 10, 0, 0);
            Root.Children.Add(card);

            var foot = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(Cancel, Dock.Left);
            Cancel.Margin = new Thickness(0);
            foot.Children.Add(Cancel);
            _freed.Foreground = page.Brush("MutedBrush");
            _freed.HorizontalAlignment = HorizontalAlignment.Right;
            foot.Children.Add(_freed);
            Root.Children.Add(foot);
            Update();
        }

        private void AddStep(Panel host, string text, string mode)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var mark = new TextBlock { Text = "◻", Width = 22, FontSize = 13, Foreground = _page.Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center };
            var right = new TextBlock { FontSize = 11.5, Foreground = _page.Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(mark, Dock.Left);
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(mark);
            row.Children.Add(right);
            var t = new TextBlock { Text = text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            if (mode.Length > 0)
                t.Inlines.Add(new System.Windows.Documents.Run("   " + mode) { FontSize = 11, Foreground = mode.StartsWith("pede", StringComparison.Ordinal) ? Orange : _page.Brush("OkBrush") });
            row.Children.Add(t);
            host.Children.Add(row);
            _steps.Add((mark, t, right));
        }

        private void Update()
        {
            double pct = Math.Clamp(100.0 * _done / _total, 0, 100);
            _bar.IsIndeterminate = false;
            _bar.Value = pct;
            _percent.Text = $"{pct:0}%";
            var elapsed = DateTime.Now - _start;
            _sub.Text = $"Etapa {Math.Min(_done + 1, _total)} de {_total} · tempo decorrido {(int)elapsed.TotalMinutes} min {elapsed.Seconds:00} s";
        }

        public void StepRunning(int index, string title, bool asks = false)
        {
            _title.Text = title;
            _title.Foreground = asks ? Orange : _page.Brush("TextBrush");
            var (mark, _, right) = _steps[index];
            mark.Text = "⏳";
            mark.Foreground = _page.Brush("AccentBrush");
            right.Text = asks ? "termine na janela dele" : "";
            Update();
        }

        public void StepDone(int index, string text, string right)
        {
            var (mark, t, r) = _steps[index];
            mark.Text = "✔";
            mark.Foreground = _page.Brush("OkBrush");
            if (text.Length > 0) t.Text = text;
            r.Text = right;
            _done = Math.Max(_done, index + 1);
            Update();
            if (index == _steps.Count - 1) { _title.Text = "Concluído"; _bar.Value = 100; _percent.Text = "100%"; Cancel.Visibility = Visibility.Collapsed; }
        }

        public void StepFailed(int index, string message)
        {
            var (mark, _, r) = _steps[index];
            mark.Text = "✖";
            mark.Foreground = _page.Brush("BadBrush");
            r.Text = message;
            r.Foreground = _page.Brush("WarnBrush");
            _done = Math.Max(_done, index + 1);
            Update();
        }

        public void StepSkipped(int index)
        {
            var (mark, _, r) = _steps[index];
            mark.Text = "–";
            r.Text = "cancelado";
            _done = Math.Max(_done, index + 1);
            Update();
        }

        public void Freed(long bytes) => _freed.Text = "Liberados até agora: " + DiagnosticRules.Size(bytes);
    }
}
