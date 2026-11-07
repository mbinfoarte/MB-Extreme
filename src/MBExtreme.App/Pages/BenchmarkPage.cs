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
    private static bool _testCpu = true, _testMemory = true, _testDisk = true, _testGpu = true;
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

    // ---- em andamento
    private readonly Image _preview = new() { Stretch = Stretch.Uniform, MaxHeight = 400 };
    private readonly Border _previewCard = new() { CornerRadius = new CornerRadius(14), ClipToBounds = true, Background = Brushes.Black, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock _previewCaption = new() { FontSize = 12, Margin = new Thickness(12, 0, 0, 10), VerticalAlignment = VerticalAlignment.Bottom, Foreground = Brushes.White };
    private readonly ProgressBar _overall = new() { Height = 8, Maximum = 100, Margin = new Thickness(0, 10, 0, 4) };
    private readonly TextBlock _runTitle = new() { FontSize = 18, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _runDetail = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _runClock = new() { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly StackPanel _stepList = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _live = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Dictionary<string, (TextBlock Icon, TextBlock Name, TextBlock Detail)> _stepRows = new();
    private WriteableBitmap? _bitmap;
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private Stopwatch _runWatch = new();

    public BenchmarkPage() : base("Benchmark",
        "Mede o processador, a memória, o disco e a placa de vídeo, com um render 3D de estúdio feito pela placa. A nota é para comparar este PC com ele mesmo: antes e depois de uma mudança.")
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
        _clockTimer.Tick += (_, _) => _runClock.Text = $"{_runWatch.Elapsed:m\\:ss}";
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
        parts.Children.Add(PartChip("", "Placa de vídeo 3D", () => _testGpu, v => { _testGpu = v; _resolutionSection.Visibility = v ? Visibility.Visible : Visibility.Collapsed; }));
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
        Extra("", "Só a placa de vídeo (3D ao vivo)", RunGpuLiveAsync);
        Extra("", "Render da CPU", RunRenderAsync);
        Extra("", "CPU por núcleo", PerCoreAsync);
        var compare = ExtraButton("", "Antes e depois de um ajuste");
        compare.Click += (_, _) => MainWindow.Current?.Go("compare");
        extras.Children.Add(compare);
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
            disk is null ? null : BenchmarkDisks.DirectoryFor(disk, Path.GetTempPath()), disk?.Title, res.Width, res.Height, _testCpu, _testMemory);
    }

    private void UpdateSummary()
    {
        var o = OptionsFor(_mode);
        var parts = new List<string>();
        if (o.Cpu) parts.Add("processador");
        if (o.Memory) parts.Add("memória");
        if (o.Disk) parts.Add("disco " + (SelectedDisk?.Letter ?? ""));
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

    // ================================================================== Em andamento

    private void BuildRunPanel()
    {
        _previewCard.Child = new Grid { Children = { _preview, _previewCaption } };
        RenderIdleRun();
    }

    private void RenderIdleRun()
    {
        if (_running) return;
        _runPanel.Children.Clear();
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 30) };
        stack.Children.Add(new TextBlock { Text = "", FontFamily = new FontFamily(IconFont), FontSize = 40, HorizontalAlignment = HorizontalAlignment.Center, Foreground = (Brush)FindResource("AccentGradientBrush") });
        stack.Children.Add(new TextBlock { Text = "Nenhum teste rodando", FontSize = 17, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 4) });
        stack.Children.Add(Muted("Escolha o modo e o que testar em Novo teste. Durante o teste, aqui aparecem as etapas, a imagem 3D sendo renderizada e os sensores.", 12));
        var go = new Button { Content = "Ir para Novo teste", Style = (Style)FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
        go.Click += (_, _) => SelectTab(0);
        stack.Children.Add(go);
        _runPanel.Children.Add(CardOf(stack));
        if (_bitmap is not null)
        {
            _previewCaption.Text = "Última imagem renderizada";
            _previewCard.Visibility = Visibility.Visible;
            _runPanel.Children.Add(Free(_previewCard));
        }
    }

    private void BeginRun(string title, IReadOnlyList<string> steps)
    {
        _running = true;
        Cts = new CancellationTokenSource();
        foreach (var b in _extraButtons) b.IsEnabled = false;
        UpdateSummary();
        _runPanel.Children.Clear();
        _stepRows.Clear();
        _stepList.Children.Clear();
        _overall.Value = 0;
        _runTitle.Text = title;
        _runDetail.Text = "Preparando...";
        _runDetail.Foreground = Brush("MutedBrush");
        _runClock.Foreground = Brush("MutedBrush");
        _live.Foreground = Brush("MutedBrush");
        _live.Text = "";

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel();
        titles.Children.Add(Free(_runTitle));
        titles.Children.Add(Free(_runDetail));
        head.Children.Add(titles);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        right.Children.Add(Free(_runClock));
        var stop = new Button { Content = "Parar", Style = (Style)FindResource("GhostButton"), Margin = new Thickness(12, 0, 0, 0) };
        stop.Click += (_, _) => Cts?.Cancel();
        right.Children.Add(stop);
        Grid.SetColumn(right, 1);
        head.Children.Add(right);

        var card = new StackPanel();
        card.Children.Add(head);
        card.Children.Add(Free(_overall));
        foreach (var step in steps)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            var icon = new TextBlock { Text = "", FontFamily = new FontFamily(IconFont), FontSize = 12, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5) };
            var name = new TextBlock { Text = step, FontSize = 13, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center };
            var detail = new TextBlock { FontSize = 11.5, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            row.Children.Add(icon);
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            Grid.SetColumn(detail, 2);
            row.Children.Add(detail);
            _stepList.Children.Add(row);
            _stepRows[step] = (icon, name, detail);
        }
        card.Children.Add(Free(_stepList));
        card.Children.Add(Free(_live));
        _runPanel.Children.Add(CardOf(card));
        _previewCard.Visibility = Visibility.Collapsed;
        _runPanel.Children.Add(Free(_previewCard));
        _runWatch = Stopwatch.StartNew();
        _runClock.Text = "0:00";
        _clockTimer.Start();
        SelectTab(1);
    }

    private string? _currentStep;

    private void ReportRun(ProgressEvent e)
    {
        _overall.Value = Math.Clamp(e.Percent, 0, 100);
        _runDetail.Text = e.Message;
        if (!_stepRows.TryGetValue(e.Step, out var row)) return;
        if (e.Step == _currentStep) { row.Detail.Text = e.Message; return; }
        if (_currentStep is { } previous && _stepRows.TryGetValue(previous, out var done)) MarkDone(done);
        _currentStep = e.Step;
        row.Icon.Text = "";
        row.Icon.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00));
        Motion.Spin(row.Icon);
        row.Name.Foreground = Brush("TextBrush");
        row.Name.FontWeight = FontWeights.SemiBold;
        row.Detail.Text = e.Message;
    }

    private void MarkDone((TextBlock Icon, TextBlock Name, TextBlock Detail) row)
    {
        Motion.Stop(row.Icon);
        row.Icon.Text = "";
        row.Icon.Foreground = Brush("OkBrush");
        row.Name.FontWeight = FontWeights.Normal;
        row.Detail.Text = "Concluído";
    }

    private void EndRun(string text, string brush)
    {
        foreach (var row in _stepRows.Values)
            if (row.Icon.Text == "") { if (brush == "OkBrush") MarkDone(row); else { Motion.Stop(row.Icon); row.Icon.Text = ""; row.Icon.Foreground = Brush(brush); } }
        _currentStep = null;
        _clockTimer.Stop();
        _runDetail.Text = text;
        _runDetail.Foreground = Brush(brush);
        if (brush == "OkBrush") _overall.Value = 100;
        _running = false;
        foreach (var b in _extraButtons) b.IsEnabled = true;
        UpdateSummary();
    }


    private async Task RunAsync()
    {
        if (_running) return;
        var options = OptionsFor(_mode);
        if (!options.Cpu && !options.Memory && !options.Disk && !options.Gpu3D) return;
        if (options.Disk && SelectedDisk is { } disk && !BenchmarkDisks.HasRoom(disk, BenchmarkModes.For(options).DiskMegabytes))
        {
            SetStatus($"O disco {disk.Letter} está com pouco espaço para o arquivo de teste. Escolha outro disco ou um modo mais leve.", "WarnBrush");
            return;
        }
        string? recording = BeginRecordingIfChecked("benchmark");
        var steps = BenchmarkService.StepsFor(options);
        BeginRun($"Testando · modo {BenchmarkModes.For(options).Name}", steps);
        var token = Cts!.Token;
        if (options.Gpu3D) NewBitmap(options.GpuWidth, options.GpuHeight);

        var clock = Stopwatch.StartNew();
        var phases = new List<PhaseMark>();
        string? lastStep = null;
        var recorder = new SensorRecorder();
        using var stopSampling = new CancellationTokenSource();
        IProgress<ProgressEvent> progress = new Progress<ProgressEvent>(e =>
        {
            ReportRun(e);
            if (e.Step == BenchmarkService.StepGpu) { _previewCard.Visibility = Visibility.Visible; _previewCaption.Text = e.Message; }
            if (e.Step != lastStep) { lastStep = e.Step; phases.Add(new PhaseMark(e.Step, clock.Elapsed.TotalSeconds)); }
        });
        var sampler = SampleSensorsAsync(recorder, stopSampling.Token);
        try
        {
            var result = await Task.Run(() => BenchmarkService.Run(progress, token, options, null, options.Gpu3D ? ShowFrame : null));
            stopSampling.Cancel();
            await sampler;
            result = result with { Monitoring = recorder.Tracks(), Phases = phases };
            BenchmarkHistory.Add(result);
            _viewing = result;
            _ = SubmitAndShowAsync(result);
            EndRun(result.Score > 0 ? $"Pronto! Pontuação {result.Score:N0}." : "Pronto! (sem a nota geral: processador ou memória ficou de fora)", "OkBrush");
            _ = FillSideAsync();
            SelectTab(2);
        }
        catch (OperationCanceledException)
        {
            EndRun("Interrompido. Nada foi salvo.", "WarnBrush");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            EndRun("Não foi possível usar o disco escolhido: " + ex.Message, "BadBrush");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            EndRun("Não foi possível concluir: " + ex.Message, "BadBrush");
        }
        finally
        {
            stopSampling.Cancel();
            try { await sampler; } catch (OperationCanceledException) { }
            EndRecording(recording);
        }
    }

    /// <summary>Lê os sensores a cada segundo enquanto o teste roda (gráfico do resultado e a linha "ao vivo").</summary>
    private Task SampleSensorsAsync(SensorRecorder recorder, CancellationToken stop) => Task.Run(async () =>
    {
        try
        {
            var sensors = await AppServices.Sensors;
            while (!stop.IsCancellationRequested)
            {
                var s = sensors.Read();
                recorder.Add(s);
                if (s.Available)
                {
                    var parts = new List<string>();
                    if (s.CpuLoad is { } cl) parts.Add($"CPU {cl:0}%" + (s.CpuTemperature is { } ct ? $" · {ct:0} °C" : ""));
                    if (s.GpuLoad is { } gl) parts.Add($"GPU {gl:0}%" + (s.GpuTemperature is { } gt ? $" · {gt:0} °C" : ""));
                    if (parts.Count > 0)
                    {
                        string text = "Ao vivo: " + string.Join("   |   ", parts);
                        _ = Dispatcher.BeginInvoke(new Action(() => _live.Text = text));
                    }
                }
                await Task.Delay(1000, stop);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException) { }
    });

    /// <summary>Roda só o benchmark 3D da GPU mostrando a imagem sendo renderizada.</summary>
    private async Task RunGpuLiveAsync()
    {
        if (_running) return;
        var profile = BenchmarkModes.Get(_mode);
        var res = _resolution ?? BenchmarkResolutions.FullHd;
        BeginRun($"Placa de vídeo 3D · {res.Name}", new[] { "GPU" });
        var token = Cts!.Token;
        NewBitmap(res.Width, res.Height);
        _previewCard.Visibility = Visibility.Visible;
        string? recording = BeginRecordingIfChecked("benchmark-gpu");
        IProgress<ProgressEvent> progress = new Progress<ProgressEvent>(e => { ReportRun(e); _previewCaption.Text = e.Message; });
        var recorder = new SensorRecorder();
        using var stopSampling = new CancellationTokenSource();
        var sampler = SampleSensorsAsync(recorder, stopSampling.Token);
        try
        {
            var gpu = await Task.Run(() => Gpu3DBenchmark.Run(progress, "GPU", 0, 100, ShowFrame, token, profile.GpuSecondsPerScene, res.Width, res.Height, profile.StudioSamples));
            stopSampling.Cancel();
            await sampler;
            var result = new BenchmarkResult(DateTime.Now, Environment.ProcessorCount, 0, 0, 0, 0, 0, 0,
                Label: string.IsNullOrWhiteSpace(_label.Text) ? null : _label.Text.Trim(),
                GpuName: gpu.GpuName, Gpu3DScore: gpu.Score, GpuScenes: gpu.Scenes, Monitoring: recorder.Tracks(),
                Mode: profile.Name, Resolution: $"{res.Width}×{res.Height}");
            BenchmarkHistory.Add(result);
            _viewing = result;
            _ = SubmitAndShowAsync(result);
            EndRun($"{gpu.GpuName}: {gpu.Score:N0} pontos na GPU.", "OkBrush");
            _ = FillSideAsync();
        }
        catch (OperationCanceledException)
        {
            EndRun("Interrompido.", "WarnBrush");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            EndRun("Não foi possível rodar o 3D: " + ex.Message + " (atualize o driver da placa de vídeo)", "BadBrush");
        }
        finally
        {
            stopSampling.Cancel();
            try { await sampler; } catch (OperationCanceledException) { }
            EndRecording(recording);
        }
    }

    /// <summary>Teste de renderização da CPU: desenha uma cena por blocos (1 núcleo e depois todos) e mostra a imagem aparecendo.</summary>
    private async Task RunRenderAsync()
    {
        if (_running) return;
        BeginRun("Render da CPU", new[] { "1 núcleo", "Todos os núcleos" });
        var token = Cts!.Token;
        _previewCard.Visibility = Visibility.Visible;
        string? recording = BeginRecordingIfChecked("benchmark-render");
        var progress = new Progress<ProgressEvent>(ReportRun);
        IProgress<ProgressEvent> report = progress;
        try
        {
            NewBitmap(CpuRenderBenchmark.DefaultWidth, CpuRenderBenchmark.DefaultHeight);
            _previewCaption.Text = "Render da CPU: 1 núcleo";
            Action<double> single = p => report.Report(new ProgressEvent("1 núcleo", p / 2, $"{p:0}% da imagem"));
            Action<double> multi = p => report.Report(new ProgressEvent("Todos os núcleos", 50 + p / 2, $"{p:0}% da imagem"));
            var one = await Task.Run(() => CpuRenderBenchmark.Run(1, 6, token, ShowTile, single));
            NewBitmap(CpuRenderBenchmark.DefaultWidth, CpuRenderBenchmark.DefaultHeight);
            _previewCaption.Text = $"Render da CPU: {Environment.ProcessorCount} linhas de execução";
            var all = await Task.Run(() => CpuRenderBenchmark.Run(Environment.ProcessorCount, 8, token, ShowTile, multi));
            var result = new BenchmarkResult(DateTime.Now, Environment.ProcessorCount, 0, 0, 0, 0, 0, 0,
                Label: string.IsNullOrWhiteSpace(_label.Text) ? null : _label.Text.Trim(), RenderSingleScore: one.Score, RenderMultiScore: all.Score);
            BenchmarkHistory.Add(result);
            _viewing = result;
            EndRun($"1 núcleo {one.Score:N0} pts · todos os núcleos {all.Score:N0} pts ({all.MegaRaysPerSecond:0.0} milhões de raios por segundo).", "OkBrush");
        }
        catch (OperationCanceledException)
        {
            EndRun("Interrompido.", "WarnBrush");
        }
        finally
        {
            EndRecording(recording);
        }
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
