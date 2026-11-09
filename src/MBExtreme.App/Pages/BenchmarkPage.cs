using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MBExtreme.Core.Benchmark;
using MBExtreme.Core.Fps;
using MBExtreme.Core.Progress;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Benchmark em abas: Novo teste (modo, o que testar, disco e resolução), Em andamento (etapas, imagem 3D ao vivo e sensores),
/// Resultados, Histórico e Comparar. Do lado direito: o seu sistema, a pontuação com a comparação automática e dicas.
/// </summary>
public sealed partial class BenchmarkPage : ToolPageBase
{
    private const string IconFont = "Segoe MDL2 Assets";
    private static BenchmarkMode _mode = BenchmarkMode.Padrao;
    private static bool _testCpu = true, _testMemory = true, _testDisk = true, _testGpu = true, _testAi = true, _testDaily = true;
    private static string? _diskRoot;
    private static BenchmarkResolution? _resolution;

    private readonly Grid _layout = new();
    private readonly StackPanel _left = new();
    private readonly StackPanel _side = new();
    private readonly ContentControl _tabHost = new();
    private readonly List<RadioButton> _tabs = new();
    private readonly StackPanel _newPanel = new();
    private readonly StackPanel _runPanel = new();
    private readonly StackPanel _resultsPanel = new();
    private readonly StackPanel _historyPanel = new();
    private readonly StackPanel _comparePanel = new();
    private readonly TextBox _label = new() { MinWidth = 260, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _summary = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _start = new() { Content = "Iniciar teste", Padding = new Thickness(26, 10, 26, 10), FontSize = 14 };
    private readonly List<Button> _extraButtons = new();
    private readonly StackPanel _diskSection = new();
    private readonly StackPanel _resolutionSection = new();
    private CheckBox _record = null!;
    private int _tab;
    private bool _running;
    private BenchmarkResult? _viewing;


    public BenchmarkPage() : base("Benchmark",
        "Mede a velocidade real do PC: processador, memória, disco, inteligência artificial, uso do dia a dia e a placa de vídeo em 3D numa janela separada. A nota é para comparar este PC com ele mesmo: meça, faça a mudança e meça de novo no mesmo modo.")
    {
        _layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        _left.Margin = new Thickness(0, 0, 16, 0);
        _layout.Children.Add(_left);
        Grid.SetColumn(_side, 1);
        _layout.Children.Add(_side);
        Body.Margin = new Thickness(0, 4, 0, 0);
        Body.Children.Add(_layout);
        SizeChanged += (_, e) => Arrange(e.NewSize.Width);

        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        AddTab(bar, "", "Novo teste");
        AddTab(bar, "", "Em andamento");
        AddTab(bar, "", "Resultados");
        AddTab(bar, "", "Histórico");
        AddTab(bar, "", "Comparar");
        var barLine = new Border { BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(0, 0, 0, 1), Child = bar, Margin = new Thickness(0, 0, 0, 14) };
        _left.Children.Add(barLine);
        _left.Children.Add(_tabHost);

        BuildNewTest();
        BuildRunPanel();
        _clockTimer.Tick += (_, _) => UpdateClock();
        Loaded += (_, _) =>
        {
            SelectTab(_tab);
            _ = FillSideAsync();
        };
        Unloaded += (_, _) => _clockTimer.Stop();
    }

    /// <summary>Em janelas estreitas o painel lateral vai para baixo.</summary>
    private void Arrange(double width)
    {
        bool narrow = width < 980;
        _layout.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(300);
        if (narrow)
        {
            if (_layout.Children.Contains(_side)) { _layout.Children.Remove(_side); _left.Children.Add(_side); _side.Margin = new Thickness(0, 12, 0, 0); }
        }
        else if (_left.Children.Contains(_side))
        {
            _left.Children.Remove(_side);
            _side.Margin = new Thickness(0);
            Grid.SetColumn(_side, 1);
            _layout.Children.Add(_side);
        }
        _left.Margin = narrow ? new Thickness(0) : new Thickness(0, 0, 16, 0);
    }

    private void AddTab(Panel bar, string glyph, string title)
    {
        int index = _tabs.Count;
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily(IconFont), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) });
        content.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
        var tab = new RadioButton { Style = (Style)FindResource("PageTab"), Content = content, GroupName = "bench-tabs" };
        System.Windows.Automation.AutomationProperties.SetName(tab, title);
        tab.Checked += (_, _) => SelectTab(index);
        _tabs.Add(tab);
        bar.Children.Add(tab);
    }

    private void SelectTab(int index)
    {
        _tab = index;
        if (_tabs[index].IsChecked != true) _tabs[index].IsChecked = true;
        StackPanel panel = index switch
        {
            0 => _newPanel,
            1 => _runPanel,
            2 => _resultsPanel,
            3 => _historyPanel,
            _ => _comparePanel,
        };
        if (index == 2) RenderResults();
        if (index == 3) RenderHistoryTab();
        if (index == 4) RenderCompare(null, null);
        if (index == 1 && !_running) RenderIdleRun();
        if (!ReferenceEquals(_tabHost.Content, panel))
        {
            _tabHost.Content = panel;
            Motion.Enter(panel, fromY: 8);
        }
    }

    // ================================================================== Novo teste

    private void BuildNewTest()
    {
        _newPanel.Children.Clear();
        _newPanel.Children.Add(SectionTitle("", "Modo do teste", "Cada modo exige mais do PC que o anterior e demora mais, em troca de uma nota mais estável."));
        var modes = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 6) };
        var modeCards = new List<ToggleButton>();
        foreach (var profile in BenchmarkModes.All)
        {
            var card = ModeCard(profile);
            card.Margin = new Thickness(0, 0, 8, 8);
            card.Click += (_, _) =>
            {
                _mode = profile.Mode;
                foreach (var c in modeCards) c.IsChecked = ReferenceEquals(c, card);
                BuildDiskSection();
                UpdateSummary();
            };
            modeCards.Add(card);
            modes.Children.Add(card);
        }
        _newPanel.Children.Add(modes);

        _newPanel.Children.Add(SectionTitle("", "O que testar", "Desmarque o que não quiser medir agora. A nota geral só sai com processador e memória."));
        var parts = new WrapPanel();
        parts.Children.Add(PartChip("", "Processador", () => _testCpu, v => _testCpu = v));
        parts.Children.Add(PartChip("", "Memória", () => _testMemory, v => _testMemory = v));
        parts.Children.Add(PartChip("", "Disco", () => _testDisk, v => { _testDisk = v; _diskSection.Visibility = v ? Visibility.Visible : Visibility.Collapsed; }));
        parts.Children.Add(PartChip("\uE99A", "Inteligência artificial", () => _testAi, v => _testAi = v));
        parts.Children.Add(PartChip("\uE8A5", "Uso do dia a dia (pesado)", () => _testDaily, v => _testDaily = v));
        parts.Children.Add(PartChip("", "Placa de vídeo 3D (janela separada)", () => _testGpu, v => { _testGpu = v; _resolutionSection.Visibility = v ? Visibility.Visible : Visibility.Collapsed; }));
        _newPanel.Children.Add(parts);

        BuildDiskSection();
        _diskSection.Visibility = _testDisk ? Visibility.Visible : Visibility.Collapsed;
        _newPanel.Children.Add(_diskSection);
        BuildResolutionSection();
        _resolutionSection.Visibility = _testGpu ? Visibility.Visible : Visibility.Collapsed;
        _newPanel.Children.Add(_resolutionSection);

        _newPanel.Children.Add(SectionTitle("", "Nome desta medição (opcional)", "Ex.: \"Antes do perfil Jogo\". O nome aparece no histórico e na comparação."));
        _newPanel.Children.Add(_label);
        _record = RecordOption();
        _newPanel.Children.Add(_record);

        // barra de início: resumo + botão
        var start = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        start.ColumnDefinitions.Add(new ColumnDefinition());
        start.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        start.Children.Add(_summary);
        _start.Style = (Style)FindResource("PrimaryButton");
        _start.Click += async (_, _) => await RunAsync();
        Grid.SetColumn(_start, 1);
        start.Children.Add(_start);
        var startCard = CardOf(start);
        startCard.Background = (Brush)FindResource("GlassSelectedBrush");
        startCard.BorderBrush = (Brush)FindResource("AccentGradientBrush");
        _newPanel.Children.Add(startCard);

        _newPanel.Children.Add(SectionTitle("", "Testes avulsos", "Um teste só, mostrando a imagem sendo desenhada."));
        var extras = new WrapPanel();
        void Extra(string glyph, string text, Func<Task> run)
        {
            var b = ExtraButton(glyph, text);
            b.Click += async (_, _) => await run();
            _extraButtons.Add(b);
            extras.Children.Add(b);
        }
        Extra("", "Só a placa de vídeo (janela 3D)", RunGpuLiveAsync);
        Extra("", "Render da CPU", RunRenderAsync);
        Extra("", "CPU por núcleo", PerCoreAsync);
        var compare = ExtraButton("", "Antes e depois de um ajuste");
        compare.Click += (_, _) => MainWindow.Current?.Go("compare");
        extras.Children.Add(compare);
        var stress = ExtraButton("", "Teste de estresse (estabilidade e calor)");
        stress.Click += (_, _) => MainWindow.Current?.Go("stress");
        extras.Children.Add(stress);
        _newPanel.Children.Add(extras);
        UpdateSummary();
    }

    private FrameworkElement SectionTitle(string glyph, string title, string detail)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 14, 0, 8) };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily(IconFont), FontSize = 14, Foreground = (Brush)FindResource("AccentGradientBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        head.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(head);
        if (detail.Length > 0) stack.Children.Add(Muted(detail, 11.5));
        return stack;
    }

    private ToggleButton ModeCard(BenchmarkProfile p)
    {
        var stack = new StackPanel();
        var top = new DockPanel();
        string glyph = p.Mode switch { BenchmarkMode.Rapido => "", BenchmarkMode.Padrao => "", _ => "" };
        var icon = new Border { Style = (Style)FindResource("ToolIconBox"), Child = new TextBlock { Text = glyph, FontFamily = new FontFamily(IconFont), FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0B)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        top.Children.Add(icon);
        var time = new TextBlock { Text = BenchmarkModes.Duration(BenchmarkModes.EstimatedSeconds(OptionsFor(p.Mode))), FontSize = 11, Foreground = Brush("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(time, Dock.Right);
        top.Children.Insert(0, time);
        stack.Children.Add(top);
        stack.Children.Add(new TextBlock { Text = p.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
        stack.Children.Add(new TextBlock { Text = p.Description, FontSize = 11.5, Foreground = Brush("MutedBrush"), TextWrapping = TextWrapping.Wrap });
        // carga: 1 a 3 barrinhas
        var load = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        load.Children.Add(new TextBlock { Text = "Exige do PC ", FontSize = 11, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center });
        for (int i = 0; i <= (int)BenchmarkMode.Preciso; i++)
            load.Children.Add(new Border { Width = 16, Height = 5, CornerRadius = new CornerRadius(2.5), Margin = new Thickness(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center,
                Background = i <= (int)p.Mode ? (Brush)FindResource("AccentGradientBrush") : Brush("BorderBrush") });
        stack.Children.Add(load);
        stack.Children.Add(new TextBlock { Text = $"Disco: {(p.DiskMegabytes >= 1024 ? p.DiskMegabytes / 1024 + " GB" : p.DiskMegabytes + " MB")} · 3D: {p.GpuSecondsPerScene} s por cena · render: {p.StudioSamples} amostras", FontSize = 10.5, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
        var card = new ToggleButton { Style = (Style)FindResource("SelectCard"), Content = stack, IsChecked = p.Mode == _mode };
        System.Windows.Automation.AutomationProperties.SetName(card, "Modo " + p.Name);
        return card;
    }

    private ToggleButton PartChip(string glyph, string text, Func<bool> get, Action<bool> set)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily(IconFont), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) });
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
        var chip = new ToggleButton { Style = (Style)FindResource("ChipToggle"), Content = content, IsChecked = get() };
        System.Windows.Automation.AutomationProperties.SetName(chip, "Testar " + text);
        chip.Click += (_, _) =>
        {
            set(chip.IsChecked == true);
            UpdateSummary();
        };
        return chip;
    }

    private Button ExtraButton(string glyph, string text)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily(IconFont), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return new Button { Style = (Style)FindResource("GhostButton"), Content = content, Margin = new Thickness(0, 0, 8, 8) };
    }

    private IReadOnlyList<BenchmarkDisk> _disks = Array.Empty<BenchmarkDisk>();

    private void BuildDiskSection()
    {
        _diskSection.Children.Clear();
        _diskSection.Children.Add(SectionTitle("", "Disco para o teste", "Escolha em qual disco gravar e ler o arquivo de teste. Ele é apagado no fim."));
        try { _disks = BenchmarkDisks.List(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _disks = Array.Empty<BenchmarkDisk>(); }
        if (_disks.Count == 0)
        {
            _diskSection.Children.Add(Muted("Nenhum disco fixo encontrado. O teste usa a pasta temporária do Windows."));
            return;
        }
        if (_diskRoot is null || _disks.All(d => d.Root != _diskRoot)) _diskRoot = _disks[0].Root;

        var grid = new UniformGrid { Columns = Math.Min(3, Math.Max(1, _disks.Count)) };
        var cards = new List<ToggleButton>();
        int needed = BenchmarkModes.Get(BenchmarkMode.Preciso).DiskMegabytes;
        foreach (var disk in _disks)
        {
            var stack = new StackPanel();
            var head = new DockPanel();
            head.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily(IconFont), FontSize = 20, Foreground = (Brush)FindResource("AccentGradientBrush"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            var names = new StackPanel();
            names.Children.Add(new TextBlock { Text = disk.Title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            names.Children.Add(new TextBlock { Text = (disk.IsSystem ? "Disco do Windows · " : "") + disk.Format, FontSize = 11, Foreground = Brush("MutedBrush") });
            head.Children.Add(names);
            stack.Children.Add(head);
            var bar = new ProgressBar { Height = 5, Maximum = 100, Value = disk.UsedPercent, Margin = new Thickness(0, 10, 0, 4) };
            stack.Children.Add(bar);
            stack.Children.Add(new TextBlock { Text = $"{Gb(disk.FreeBytes)} livres de {Gb(disk.TotalBytes)}", FontSize = 11, Foreground = Brush("MutedBrush") });
            bool room = BenchmarkDisks.HasRoom(disk, BenchmarkModes.Get(_mode).DiskMegabytes);
            if (!room) stack.Children.Add(new TextBlock { Text = "Pouco espaço livre para o teste", FontSize = 11, Foreground = Brush("WarnBrush") });
            else if (!BenchmarkDisks.HasRoom(disk, needed)) stack.Children.Add(new TextBlock { Text = "Sem espaço para o modo Preciso", FontSize = 11, Foreground = Brush("WarnBrush") });

            var card = new ToggleButton { Style = (Style)FindResource("SelectCard"), Content = stack, IsChecked = disk.Root == _diskRoot, IsEnabled = room, Margin = new Thickness(0, 0, 8, 8) };
            System.Windows.Automation.AutomationProperties.SetName(card, "Testar o disco " + disk.Title);
            card.Click += (_, _) =>
            {
                _diskRoot = disk.Root;
                foreach (var c in cards) c.IsChecked = ReferenceEquals(c, card);
                UpdateSummary();
            };
            cards.Add(card);
            grid.Children.Add(card);
        }
        _diskSection.Children.Add(grid);
        if (_disks.Any(d => !d.IsSystem))
            _diskSection.Children.Add(Muted("Nos outros discos o arquivo vai para a pasta \"MBExtreme-teste-de-disco\", criada e apagada pelo teste.", 11));
    }

    private static string Gb(long bytes) => bytes >= 1L << 40 ? $"{bytes / (double)(1L << 40):0.#} TB" : $"{bytes / (double)(1L << 30):0} GB";

    private IReadOnlyList<BenchmarkResolution> _resolutions = Array.Empty<BenchmarkResolution>();

    private void BuildResolutionSection()
    {
        _resolutionSection.Children.Clear();
        var (w, h, cw, ch) = NativeDisplay.PrimaryMonitor();
        _resolutions = BenchmarkResolutions.For(w, h);
        _resolution = _resolution is not null && _resolutions.Contains(_resolution) ? _resolution : BenchmarkResolutions.Suggested(_resolutions);
        string monitor = w > 0 ? $"Seu monitor chega a {w}×{h}" + (cw > 0 && (cw != w || ch != h) ? $" (agora em {cw}×{ch})" : "") + ". " : "";
        _resolutionSection.Children.Add(SectionTitle("", "Resolução das cenas 3D",
            monitor + "Quad HD e 4K aparecem quando a tela aguenta. As notas são convertidas para 1080p, então resoluções diferentes ainda dão para comparar."));
        var wrap = new WrapPanel();
        var chips = new List<ToggleButton>();
        foreach (var r in _resolutions)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = r.Name, FontWeight = FontWeights.SemiBold });
            content.Children.Add(new TextBlock { Text = $"{r.Width}×{r.Height}", FontSize = 10.5, Opacity = 0.75 });
            var chip = new ToggleButton { Style = (Style)FindResource("ChipToggle"), Content = content, IsChecked = r == _resolution };
            System.Windows.Automation.AutomationProperties.SetName(chip, "Resolução " + r.Label);
            chip.Click += (_, _) =>
            {
                _resolution = r;
                foreach (var c in chips) c.IsChecked = ReferenceEquals(c, chip);
                UpdateSummary();
            };
            chips.Add(chip);
            wrap.Children.Add(chip);
        }
        _resolutionSection.Children.Add(wrap);
    }

    private BenchmarkDisk? SelectedDisk => _disks.FirstOrDefault(d => d.Root == _diskRoot);

    private BenchmarkOptions OptionsFor(BenchmarkMode mode)
    {
        var res = _resolution ?? BenchmarkResolutions.FullHd;
        var disk = SelectedDisk;
        return new BenchmarkOptions(_testDisk, _testGpu, false, string.IsNullOrWhiteSpace(_label.Text) ? null : _label.Text.Trim(), mode,
            disk is null ? null : BenchmarkDisks.DirectoryFor(disk, Path.GetTempPath()), disk?.Title, res.Width, res.Height, _testCpu, _testMemory,
            _testAi, _testDaily, _resolutions.Count > 0 ? _resolutions : null);
    }

    private void UpdateSummary()
    {
        var o = OptionsFor(_mode);
        var parts = new List<string>();
        if (o.Cpu) parts.Add("processador");
        if (o.Memory) parts.Add("memória");
        if (o.Disk) parts.Add("disco " + (SelectedDisk?.Letter ?? ""));
        if (o.Cpu && o.Ai) parts.Add("IA");
        if (o.Cpu && o.Daily) parts.Add("dia a dia");
        if (o.Gpu3D) parts.Add($"placa de vídeo em {(_resolution ?? BenchmarkResolutions.FullHd).Name}");
        bool any = parts.Count > 0;
        _start.IsEnabled = any && !_running;
        _summary.Inlines.Clear();
        if (!any)
        {
            _summary.Inlines.Add(new System.Windows.Documents.Run("Escolha pelo menos uma parte para testar.") { Foreground = Brush("WarnBrush") });
            return;
        }
        _summary.Inlines.Add(new System.Windows.Documents.Run($"Modo {BenchmarkModes.Get(_mode).Name}") { FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)) });
        _summary.Inlines.Add(new System.Windows.Documents.Run($" · {string.Join(", ", parts)}") { Foreground = Brush("TextBrush") });
        _summary.Inlines.Add(new System.Windows.Documents.LineBreak());
        _summary.Inlines.Add(new System.Windows.Documents.Run($"Duração estimada {BenchmarkModes.Duration(BenchmarkModes.EstimatedSeconds(o))}" + (o.Cpu && o.Memory ? "" : " · sem a nota geral")) { Foreground = Brush("MutedBrush"), FontSize = 12 });
    }

    private void NewBitmap(int width, int height) => Dispatcher.Invoke(() =>
    {
        _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        _preview.Source = _bitmap;
    });

    private void ShowTile(RenderTile tile) => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (_bitmap is null) return;
        _bitmap.WritePixels(new Int32Rect(tile.X, tile.Y, tile.Width, tile.Height), tile.Bgra, tile.Width * 4, 0);
    }));

    private void ShowFrame(byte[] bgra, int width, int height) => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
        {
            _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            _preview.Source = _bitmap;
        }

        _bitmap.WritePixels(new Int32Rect(0, 0, width, height), bgra, width * 4, 0);
    }));

    private async Task PerCoreAsync()
    {
        if (_running) return;
        ShowProgress("CPU por núcleo", "Núcleos");
        var token = Cts!.Token;
        IProgress<ProgressEvent> progress = new Progress<ProgressEvent>(Progress.Report);
        try
        {
            var cores = await Task.Run(() => BenchmarkService.PerCore(progress, "Núcleos", token));
            double best = cores.Max(c => c.Mops);
            double worst = cores.Min(c => c.Mops);
            string lines = string.Join("\n", cores.Select(c =>
                $"Núcleo {c.Core + 1,2}: {c.Mops,6:0.0} Mop/s  {new string('█', (int)Math.Round(c.Mops / best * 24))}"));
            Progress.Finish($"Mais forte {best:0.0} · mais fraco {worst:0.0} Mop/s (diferença {(best - worst) / best * 100:0.#}%)", true, lines);
        }
        catch (OperationCanceledException)
        {
            Progress.Finish("Interrompido.", false);
        }
    }
}
