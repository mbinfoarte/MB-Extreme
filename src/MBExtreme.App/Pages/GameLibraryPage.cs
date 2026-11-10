using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MBExtreme.Core.Games;
using MBExtreme.Core.Network;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Biblioteca de jogos em grade de capas (Steam, Epic, GOG e os que você adicionar). Ao escolher um jogo: abrir com o Modo Jogo,
/// otimizar por tipo de jogo e marca da placa de vídeo, prioridade de CPU e requisitos informados pela loja.
/// </summary>
public sealed partial class GameLibraryPage : ToolPageBase
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly SemaphoreSlim CoverGate = new(3, 3);
    private readonly TextBlock _tierText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly WrapPanel _grid = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly StackPanel _detail = new();
    private readonly Dictionary<string, Border> _cards = new();
    private readonly Dictionary<string, SteamStoreInfo?> _storeInfo = new();
    private IReadOnlyList<InstalledGame> _games = Array.Empty<InstalledGame>();
    private InstalledGame? _selected;
    private GpuMaker _maker = GpuMaker.Unknown;
    private int _tier = 2;
    private string _steamPath = "";
    private bool _busy;
    private bool _advanced;
    private int _tab;
    private readonly StackPanel _sideTop = new();
    private readonly Grid _split = new();
    private readonly ColumnDefinition _gridColumn = new() { Width = new GridLength(1, GridUnitType.Star) };
    private readonly ColumnDefinition _gapColumn = new() { Width = new GridLength(14) };
    private readonly ColumnDefinition _sideColumn = new() { Width = new GridLength(430) };
    private ScrollViewer? _gridScroll;
    private readonly Dictionary<string, ImageSource> _coverImages = new();

    public GameLibraryPage() : base("Biblioteca de jogos",
        "Seus jogos instalados. Escolha um para abrir com o Modo Jogo, otimizar o PC para o tipo de jogo e a sua placa de vídeo, e ver os requisitos informados pela loja.")
    {
        AddButton("Procurar jogos", async (_, _) => await ScanAsync());
        AddButton("Adicionar jogo ou programa...", (_, _) => AddManual(), false);

        var tier = CardOf(_tierText);
        tier.Padding = new Thickness(12, 6, 12, 6);
        Body.Children.Add(tier);
        BuildSplit();
        Body.Children.Add(Muted("Steam, Epic e GOG são lidos só para listar os jogos; nenhum arquivo deles é alterado. Os ajustes de otimização usam o catálogo do MB Extreme (com backup e desfazer).", 11));
        Unloaded += (_, _) =>
        {
            AppServices.GameMeasure.Changed -= OnMeasureChanged;
            AppServices.GameMeasure.Finished -= OnMeasureFinished;
        };
        SizeChanged += (_, _) => FitSplit();
        Loaded += async (_, _) =>
        {
            FitSplit();
            AppServices.GameMeasure.Changed -= OnMeasureChanged;
            AppServices.GameMeasure.Finished -= OnMeasureFinished;
            AppServices.GameMeasure.Changed += OnMeasureChanged;
            AppServices.GameMeasure.Finished += OnMeasureFinished;
            await LoadHardwareAsync();
            await ScanAsync();
        };
    }

    // ------------------------------------------------------------ layout: grade à esquerda, painel do jogo à direita

    private void BuildSplit()
    {
        _split.ColumnDefinitions.Add(_gridColumn);
        _split.ColumnDefinitions.Add(_gapColumn);
        _split.ColumnDefinitions.Add(_sideColumn);

        var gridScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _grid };
        _gridScroll = gridScroll;
        Grid.SetColumn(gridScroll, 0);
        _split.Children.Add(gridScroll);

        var side = new Grid();
        side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        side.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        side.Children.Add(_sideTop);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _detail, Margin = new Thickness(0, 6, 0, 0) };
        Grid.SetRow(scroll, 1);
        side.Children.Add(scroll);
        var card = CardOf(side);
        card.Margin = new Thickness(0);
        Grid.SetColumn(card, 2);
        _split.Children.Add(card);

        ShowPlaceholder();
        Body.Children.Add(_split);
    }

    /// <summary>A grade e o painel rolam cada um por conta própria, na altura que sobra da janela.</summary>
    private void FitSplit()
    {
        _split.Height = Math.Max(440, ActualHeight - 300);
        _gridColumn.Width = new GridLength(1, GridUnitType.Star);
        _gapColumn.Width = new GridLength(14);
        _sideColumn.Width = new GridLength(ActualWidth < 1050 ? 340 : 430);
        if (_gridScroll is not null) _gridScroll.Visibility = Visibility.Visible;
    }

    private void ShowPlaceholder()
    {
        _inline = -1;
        _view = null;
        FitSplit();
        _sideTop.Children.Clear();
        _detail.Children.Clear();
        _sideTop.Children.Add(new TextBlock { Text = "Escolha um jogo", FontSize = 18, FontWeight = FontWeights.SemiBold });
        _sideTop.Children.Add(Muted("Clique na capa de um jogo para ver o resumo dele aqui ao lado: jogar, otimizar, medir FPS, rede e requisitos.", 11));
    }

    // ------------------------------------------------------------ hardware

    private async Task LoadHardwareAsync()
    {
        try
        {
            var hw = await AppServices.Hardware();
            var advice = GameAdvisor.Recommend(hw);
            _tier = GameAdvisor.Tier(hw);
            var gpu = hw.Gpus.OrderByDescending(g => g.VramBytes).FirstOrDefault();
            string n = gpu?.Name ?? "";
            _maker = n.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || n.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ? GpuMaker.Nvidia
                : n.Contains("AMD", StringComparison.OrdinalIgnoreCase) || n.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? GpuMaker.Amd
                : n.Contains("Intel", StringComparison.OrdinalIgnoreCase) || n.Contains("Arc", StringComparison.OrdinalIgnoreCase) ? GpuMaker.Intel : GpuMaker.Unknown;
            _tierText.Text = $"Seu PC: {advice.Tier} · {(gpu?.Name ?? "placa de vídeo não identificada")}. {advice.Headline}";
            _nvidiaName = hw.Gpus.Select(g => g.Name).FirstOrDefault(name => name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || name.Contains("GeForce", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException or System.Security.SecurityException or UnauthorizedAccessException or IOException or System.Management.ManagementException)
        {
            _tierText.Text = "Não foi possível ler o hardware agora.";
        }
    }

    // ------------------------------------------------------------ lista e grade

    private async Task ScanAsync()
    {
        if (_busy) return;
        _busy = true;
        SetStatus("Procurando jogos...");
        try
        {
            try
            {
                _steamPath = await Task.Run(GameScanner.SteamPath);
                _games = await Task.Run(() => GameScanner.Scan(AppServices.GameLibrary));
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                SetStatus("Não foi possível procurar os jogos agora: " + ex.Message, "BadBrush");
                return;
            }
            SetStatus(_games.Count == 0 ? "Nenhum jogo encontrado. Use Adicionar jogo ou programa." : $"{_games.Count} item(ns) encontrado(s).", _games.Count == 0 ? "WarnBrush" : "OkBrush");
            RenderGrid();
        }
        finally
        {
            _busy = false;
        }
    }

    private void RenderGrid()
    {
        _grid.Children.Clear();
        _cards.Clear();
        foreach (var game in _games)
        {
            var card = BuildCard(game);
            _cards[game.Id] = card;
            _grid.Children.Add(card);
        }
        if (_selected is not null && _games.FirstOrDefault(g => g.Id == _selected.Id) is { } still) Select(still, false);
        else _selected = null;
        if (_selected is null) ShowPlaceholder();
    }

    private Border BuildCard(InstalledGame game)
    {
        var settings = AppServices.GameLibrary.Get(game.Id);
        var root = new Grid();
        var fallback = new TextBlock
        {
            Text = game.Name, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontSize = 14, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10), Foreground = Brush("TextBrush"),
        };
        var icon = new System.Windows.Controls.Image { Width = 56, Height = 56, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 46, 0, 0), Visibility = Visibility.Collapsed };
        var cover = new Border { CornerRadius = new CornerRadius(10), Visibility = Visibility.Collapsed };
        root.Children.Add(fallback);
        root.Children.Add(icon);
        root.Children.Add(cover);

        var label = new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(0, 0, 10, 10), Padding = new Thickness(8, 14, 8, 6),
            Background = new LinearGradientBrush(System.Windows.Media.Color.FromArgb(0, 0, 0, 0), System.Windows.Media.Color.FromArgb(230, 0, 0, 0), 90),
            Child = new TextBlock { Text = game.Name, FontSize = 11, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = System.Windows.Media.Brushes.White },
        };
        root.Children.Add(label);

        if (settings.OptimizedAt is not null)
            root.Children.Add(new Border
            {
                // fundo amarelo e raio preto (ícone da fonte do Windows, para não virar emoji colorido)
                Background = Brush("AccentBrush"), CornerRadius = new CornerRadius(12), Width = 24, Height = 24, Margin = new Thickness(6),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = "\uE945", FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), FontSize = 13, Foreground = System.Windows.Media.Brushes.Black,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
                ToolTip = "Otimizado pelo MB Extreme",
            });

        var card = new Border
        {
            Width = 112, Height = 168, Margin = new Thickness(0, 0, 10, 10), CornerRadius = new CornerRadius(10), Cursor = System.Windows.Input.Cursors.Hand,
            Background = Brush("Surface2Brush"), BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(1), Child = root, ToolTip = game.Name,
        };
        card.MouseLeftButtonUp += (_, _) => Select(game, true);
        _ = LoadCoverAsync(game, cover, icon, fallback);
        return card;
    }

    private async Task LoadCoverAsync(InstalledGame game, Border cover, System.Windows.Controls.Image icon, TextBlock fallback)
    {
        await CoverGate.WaitAsync();
        try
        {
            string? file = null;
            string appCache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "covers");
            file = CoverSearch.FindCustom(appCache, game.Id); // a capa que o usuário escolheu vale mais que a automática
            if (file is null && game.SteamAppId is { } appId)
            {
                file = await Task.Run(() => GameCovers.FindSteamLocal(_steamPath, appId));
                file ??= await GameCovers.DownloadSteamAsync(Http, appId, appCache);
            }

            if (file is not null)
            {
                var bitmap = await Task.Run(() => LoadBitmap(file));
                if (bitmap is not null)
                {
                    cover.Background = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
                    _coverImages[game.Id] = bitmap;
                    cover.Visibility = Visibility.Visible;
                    fallback.Visibility = Visibility.Collapsed;
                    return;
                }
            }

            var iconSource = await Task.Run(() => ExtractIcon(game));
            if (iconSource is not null)
            {
                icon.Source = iconSource;
                icon.Visibility = Visibility.Visible;
                fallback.VerticalAlignment = VerticalAlignment.Bottom;
                fallback.Margin = new Thickness(10, 10, 10, 40);
                fallback.FontSize = 12;
            }
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or UnauthorizedAccessException) { }
        finally
        {
            CoverGate.Release();
        }
    }

    private static BitmapSource? LoadBitmap(string file)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;   // capa trocada no mesmo arquivo aparece na hora
            bitmap.DecodePixelWidth = 300;
            bitmap.UriSource = new Uri(file);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or InvalidOperationException) { return null; }
    }

    private static BitmapSource? ExtractIcon(InstalledGame game)
    {
        try
        {
            string? exe = game.Launcher == GameLauncher.Manual ? game.LaunchTarget : null;
            if (exe is null && game.ExeName is { } name && Directory.Exists(game.InstallPath))
                exe = Directory.EnumerateFiles(game.InstallPath, name, new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true }).FirstOrDefault();
            if (exe is null || !File.Exists(exe)) return null;
            using var ico = Icon.ExtractAssociatedIcon(exe);
            if (ico is null) return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or Win32Exception) { return null; }
    }

    // ------------------------------------------------------------ painel do jogo

    private void Select(InstalledGame game, bool scroll)
    {
        _selected = game;
        foreach (var (id, card) in _cards)
        {
            bool on = id == game.Id;
            card.BorderBrush = Brush(on ? "AccentBrush" : "BorderBrush");
            card.BorderThickness = new Thickness(on ? 2 : 1);
        }
        _ = RenderDetailAsync(game);
    }

    private sealed record GameView(
        InstalledGame Game, SteamStoreInfo? Info, GameSettings Settings, GameProfile Profile, OptimizationPlan Plan,
        IReadOnlyList<SettingRow> Rows, IReadOnlyList<SettingRow> NetRows, RequirementReport? Requirements = null);

    private GameView? _view;
    private int _inline = -1; // detalhe aberto dentro do painel (-1 = nenhum)
    private int _detailTab;

    private async Task RenderDetailAsync(InstalledGame game)
    {
        // informações da loja (gêneros, requisitos): consulta uma vez e guarda em disco
        if (game.SteamAppId is { } appId && !_storeInfo.ContainsKey(game.Id))
        {
            _storeInfo[game.Id] = null;
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "games-info");
            _storeInfo[game.Id] = await SteamStore.GetAsync(Http, appId, folder);
        }
        if (_selected?.Id != game.Id) return;
        _storeInfo.TryGetValue(game.Id, out var info);

        var settings = AppServices.GameLibrary.Get(game.Id);
        var profile = settings.ProfileKey is { } key ? GameProfiles.Find(key)
            : game.Launcher == GameLauncher.Manual && game.ExeName is null ? GameProfiles.App
            : GameProfiles.Classify(game.ExeName, info?.Genres, info?.Categories);
        var plan = GameOptimizer.Plan(profile, _maker, _tier, onHdd: false);

        var rows = await Task.Run(() => ReadRows(game, profile));
        var netRows = await Task.Run(() => ReadNetworkRows(game));
        if (_selected?.Id != game.Id) return;

        // compara o PC com os requisitos da loja (estimativa pelo nível do processador e da placa)
        RequirementReport? requirements = null;
        if (info is { MinimumRequirements.Length: > 0 } or { RecommendedRequirements.Length: > 0 })
        {
            try
            {
                var hw = await AppServices.Hardware();
                requirements = RequirementChecker.Check(info!.MinimumRequirements, info.RecommendedRequirements, MachineProfile.From(hw));
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Management.ManagementException or AggregateException) { }
        }
        if (_selected?.Id != game.Id) return;

        _view = new GameView(game, info, settings, profile, plan, rows, netRows, requirements);
        Paint();
    }

    /// <summary>Desenha o painel do jogo: resumo e otimização na lateral, ou os detalhes ocupando o lugar da grade.</summary>
    private void Paint()
    {
        if (_view is not { } v) return;
        FitSplit();
        _sideTop.Children.Clear();
        _detail.Children.Clear();

        _sideTop.Children.Add(BuildHeader(v));
        _sideTop.Children.Add(BuildActions(v));

        _tab = Math.Clamp(_tab, 0, 2);
        _sideTop.Children.Add(Chips(new[] { "Resumo", "Otimização", "Configurações" }, _tab, i => { _tab = i; Paint(); }));
        _detail.Children.Add(_tab == 0 ? SummaryPanel(v) : _tab == 1 ? OptimizePanel(v) : ConfigPanel(v));
        if (_inline >= 0)
        {
            // detalhes abrem aqui mesmo, logo abaixo, sem trocar a tela
            string[] names = { "Ajustes do jogo", "Rede e rota", "Desempenho (FPS)", "Requisitos" };
            _detailTab = _inline;
            var head = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(new TextBlock { Text = names[Math.Clamp(_inline, 0, 3)], FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var close = SmallButton("Recolher", (_, _) => { _inline = -1; Paint(); });
            close.Padding = new Thickness(10, 3, 10, 3);
            close.FontSize = 11.5;
            Grid.SetColumn(close, 1);
            head.Children.Add(close);
            _detail.Children.Add(head);
            _detail.Children.Add(DetailSection(v));
        }
    }

    private void OpenDetails(int detailTab)
    {
        _inline = _inline == detailTab ? -1 : detailTab;
        Paint();
    }

    // ---- topo: capa, nome, Jogar

    private UIElement BuildHeader(GameView v)
    {
        var game = v.Game;
        var head = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (_coverImages.TryGetValue(game.Id, out var coverImage))
            head.Children.Add(new Border { Width = 54, Height = 81, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 10, 0), Background = new ImageBrush(coverImage) { Stretch = Stretch.UniformToFill } });

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = game.Name, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var s = v.Settings;
        string played = s.MinutesPlayed < 1 ? "sem registro de tempo" : $"{s.MinutesPlayed / 60:0.#} h jogadas" + (s.LastPlayed is { } last ? $" · último: {last:dd/MM}" : "");
        titles.Children.Add(Muted($"{game.LauncherName} · {played}", 11));
        Grid.SetColumn(titles, 1);
        head.Children.Add(titles);

        var play = SmallButton("Jogar", (_, _) => Play(game), true);
        play.Margin = new Thickness(10, 0, 0, 0);
        play.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(play, 2);
        head.Children.Add(play);
        return head;
    }

    // ---- ações: otimizar, medir FPS e desfazer

    private UIElement BuildActions(GameView v)
    {
        var game = v.Game;
        var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 4) };
        bool optimized = v.Settings.OptimizedAt is not null;
        buttons.Children.Add(Tight(SmallButton(optimized ? "Reaplicar otimização" : "Otimizar", async (_, _) => await OptimizeAllAsync(game, v.Plan, v.Profile), !optimized)));

        if (AppServices.GameMeasure.GameId == game.Id)
            buttons.Children.Add(Tight(SmallButton("Parar medição", (_, _) => AppServices.GameMeasure.Stop(), true)));
        else if (game.ExeName is not null)
        {
            var measure = Tight(SmallButton("Medir FPS", (_, _) => Play(game, true)));
            measure.ToolTip = "Abre o jogo e mede o FPS enquanto você joga; a sessão é guardada ao fechar o jogo.";
            buttons.Children.Add(measure);
        }

        if (optimized) buttons.Children.Add(Tight(SmallButton("Desfazer", async (_, _) => await UndoAsync(game))));
        var cover = Tight(SmallButton("Buscar capa", async (_, _) => await PickCoverAsync(game)));
        cover.ToolTip = "Procura a capa deste jogo na internet para você escolher.";
        buttons.Children.Add(cover);
        return buttons;
    }

    private async Task PickCoverAsync(InstalledGame game)
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "covers");
        var picker = new CoverPickerWindow(Http, game.Name, AppServices.Settings, CoverSearch.FindCustom(folder, game.Id) is not null) { Owner = Window.GetWindow(this) };
        if (picker.ShowDialog() != true) return;
        if (picker.RemoveCustom)
        {
            CoverSearch.RemoveCustom(folder, game.Id);
            _coverImages.Remove(game.Id);
            SetStatus($"{game.Name} voltou a usar a capa automática.", "OkBrush");
            RenderGrid();
            return;
        }
        string? file;
        if (picker.ChosenFile is { } local)
        {
            var saved = WebCoverSearch.SaveFromFile(local, folder, game.Id);
            if (saved.File is null) { SetStatus("Não deu para usar essa imagem: " + saved.Error, "WarnBrush"); return; }
            file = saved.File;
        }
        else if (picker.ChosenUrl is { } url)
        {
            SetStatus("Baixando a imagem...");
            var saved = await WebCoverSearch.SaveFromUrlAsync(Http, url, folder, game.Id);
            if (saved.File is null) { SetStatus("Não deu para usar esse link: " + saved.Error, "WarnBrush"); return; }
            file = saved.File;
        }
        else
        {
            if (picker.Chosen is not { } chosen) return;
            SetStatus("Baixando a capa...");
            file = await CoverSearch.SaveCustomAsync(Http, chosen, folder, game.Id);
            if (file is null) { SetStatus("Não foi possível baixar essa capa. Tente outra.", "WarnBrush"); return; }
        }
        _coverImages.Remove(game.Id);
        SetStatus($"Capa de {game.Name} atualizada.", "OkBrush");
        RenderGrid();
    }

    /// <summary>Otimiza o jogo e a rede dele de uma vez (cada parte continua desfazível pelo "Desfazer").</summary>
    private async Task OptimizeAllAsync(InstalledGame game, OptimizationPlan plan, GameProfile profile)
    {
        await OptimizeAsync(game, plan, profile);
        if (_view is { } v && v.NetRows.Any(r => r.CanFix))
            await OptimizeNetworkAsync(game, profile);
    }

    // ---- aba Resumo: poucos cartões, cada um com "Ver detalhes"

    private UIElement SummaryPanel(GameView v)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var game = v.Game;
        var info = v.Info;
        panel.Children.Add(Muted((game.ExeName ?? "Executável não identificado") + (info is { Genres.Count: > 0 } ? " · " + string.Join(", ", info.Genres.Take(3)) : ""), 11));

        // 1) otimização
        int gameFix = v.Rows.Count(r => r.CanFix || r.State == RowState.Different);
        int netFix = v.NetRows.Count(r => r.CanFix || r.State == RowState.Different);
        int fix = gameFix + netFix;
        bool optimized = v.Settings.OptimizedAt is not null;
        string optLine, optBrush;
        if (optimized && fix == 0) { optLine = "✓ Otimizado · tudo certo"; optBrush = "OkBrush"; }
        else if (optimized) { optLine = $"Otimizado, mas {fix} ponto(s) mudaram desde então"; optBrush = "WarnBrush"; }
        else if (fix == 0) { optLine = "Já está como o recomendado"; optBrush = "OkBrush"; }
        else { optLine = $"{fix} ponto(s) para otimizar"; optBrush = "WarnBrush"; }
        panel.Children.Add(InfoCard("Otimização", optLine, optBrush,
            $"Jogo: {(gameFix == 0 ? "tudo certo" : gameFix + " a ajustar")} · Rede: {(netFix == 0 ? "tudo certo" : netFix + " a ajustar")}",
            "Ver ajustes", () => { _tab = 1; Paint(); }));

        // 2) desempenho (FPS)
        var sessions = AppServices.Perf.Get(game.Id);
        var cmp = PerfCompare.Build(sessions);
        string fpsLine, fpsBrush = "TextBrush", fpsSub;
        if (cmp.Before is not null && cmp.After is not null)
        {
            fpsLine = $"FPS médio {cmp.Before.AvgFps:0} → {cmp.After.AvgFps:0} ({cmp.AvgChangePercent:+0;-0;0}%)";
            fpsBrush = cmp.Verdict == Verdict.Worse ? "WarnBrush" : "OkBrush";
            fpsSub = $"1% pior {cmp.Before.Low1Fps:0} → {cmp.After.Low1Fps:0} ({cmp.LowChangePercent:+0;-0;0}%) · antes e depois da otimização";
        }
        else if (sessions.LastOrDefault() is { } last)
        {
            fpsLine = $"FPS médio {last.AvgFps:0} · 1% pior {last.Low1Fps:0}";
            fpsSub = "Falta medir o outro lado (antes ou depois de otimizar) para comparar.";
        }
        else
        {
            fpsLine = "Ainda não medido";
            fpsBrush = "MutedBrush";
            fpsSub = game.ExeName is null ? "Não sei qual é o executável deste jogo." : "Clique em Medir FPS, jogue 2 a 3 minutos e feche o jogo.";
        }
        panel.Children.Add(InfoCard("Desempenho (FPS)", fpsLine, fpsBrush, fpsSub, "Ver detalhes", () => OpenDetails(2)));

        // 3) rede
        var report = AppServices.NetReports.Get(game.Id).LastOrDefault();
        panel.Children.Add(InfoCard("Rede e rota",
            report is null ? "Rota ainda não analisada" : $"Ping médio {report.AvgMs:0} ms · oscilação {report.JitterMs:0.#} ms · perda {report.LossPercent:0.#}%",
            report is null ? "MutedBrush" : report.LossPercent > 2 || report.JitterMs > 15 ? "WarnBrush" : "OkBrush",
            report is null ? "Entre numa partida e use Ver detalhes → Analisar rota." : $"Medido em {report.At:dd/MM HH:mm} · {report.Host}",
            "Ver detalhes", () => OpenDetails(1)));

        // 4) requisitos
        string req = info is { MinimumRequirements.Length: > 0 } ? Shorten(info.MinimumRequirements, 150) : "A loja não informou requisitos para este jogo.";
        bool hasReq = info is { MinimumRequirements.Length: > 0 };
        string reqLine = v.Requirements is { Verdict: not Fit.Unknown } rr ? rr.Summary : hasReq ? "Mínimos informados pela loja" : "Sem requisitos informados";
        string reqBrush = v.Requirements?.Verdict switch { Fit.Recommended => "OkBrush", Fit.Minimum => v.Requirements.Summary.Contains("por pouco") ? "WarnBrush" : "OkBrush", Fit.Below => "BadBrush", _ => hasReq ? "TextBrush" : "MutedBrush" };
        panel.Children.Add(InfoCard("Requisitos", reqLine, reqBrush, req, "Ver detalhes", () => OpenDetails(3)));
        return panel;
    }

    private static string Shorten(string text, int max)
    {
        string flat = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= max ? flat : flat[..max].TrimEnd() + "…";
    }

    private Border InfoCard(string title, string line, string brush, string sub, string buttonText, Action open)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, FontSize = 11, Foreground = Brush("MutedBrush") });
        text.Children.Add(new TextBlock { Text = line, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Brush(brush), TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = sub, FontSize = 11, Foreground = Brush("MutedBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        grid.Children.Add(text);
        var button = SmallButton(buttonText, (_, _) => open());
        button.Padding = new Thickness(10, 4, 10, 4);
        button.FontSize = 11.5;
        button.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        var card = CardOf(grid);
        card.Padding = new Thickness(12, 8, 8, 8);
        card.Margin = new Thickness(0, 6, 0, 0);
        return card;
    }

    // ---- aba Otimização: duas listas curtas (jogo e rede)

    private UIElement OptimizePanel(GameView v)
    {
        var game = v.Game;
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };

        bool nvidia = _maker == GpuMaker.Nvidia;
        panel.Children.Add(Group("Jogo", v.Rows.Where(r => r.Group != "Driver NVIDIA").ToList(), () => OpenDetails(0)));
        if (nvidia)
        {
            // as configurações que antes só apareciam no Painel de Controle da NVIDIA, direto aqui
            panel.Children.Add(Group("Placa de vídeo (NVIDIA)", v.Rows.Where(r => r.Group == "Driver NVIDIA").ToList(), () => OpenDetails(0)));
            var adv = new CheckBox
            {
                Content = "Incluir ajustes avançados do driver (experimentais; cada um tem explicação e é desfeito pelo \"Desfazer\")",
                IsChecked = _advanced, Foreground = Brush("TextBrush"), FontSize = 11.5, Margin = new Thickness(0, 6, 0, 0),
            };
            adv.Click += async (_, _) => { _advanced = adv.IsChecked == true; await RenderDetailAsync(game); };
            panel.Children.Add(adv);
            panel.Children.Add(Muted("Vale só para este jogo e é gravado no próprio driver, sem abrir o Painel de Controle da NVIDIA. \"Otimizar\" aplica tudo, guarda o valor de antes e \"Desfazer\" devolve.", 10.5));
        }
        panel.Children.Add(Group("Rede", v.NetRows.Where(r => r.Recommended != "Precisa de servidor").ToList(), () => OpenDetails(1)));

        var more = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        if (game.ExeName is not null)
        {
            bool applied = false;
            try { applied = AppServices.GamePriority.IsApplied(game.ExeName); } catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { }
            more.Children.Add(Tight(SmallButton(applied ? "Prioridade alta: ligada" : "Prioridade alta de CPU", (_, _) => TogglePriority(game, applied), applied)));
        }
        if (game.Launcher == GameLauncher.Manual)
            more.Children.Add(Tight(SmallButton("Remover da lista", (_, _) => { AppServices.GameLibrary.RemoveManual(game.LaunchTarget ?? ""); _selected = null; _inline = -1; _ = ScanAsync(); })));
        panel.Children.Add(more);
        return panel;
    }

    /// <summary>Lista curta: nome do ajuste e o estado (✓ OK, ▲ Desligado...). O texto longo fica no tooltip e em "Ver detalhes".</summary>
    private UIElement Group(string title, IReadOnlyList<SettingRow> rows, Action details)
    {
        int fix = rows.Count(r => r.CanFix || r.State == RowState.Different);
        var box = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 10, 0) });
        label.Children.Add(new TextBlock { Text = fix == 0 ? "✓ tudo certo" : $"▲ {fix} a ajustar", FontSize = 12, Foreground = Brush(fix == 0 ? "OkBrush" : "WarnBrush"), VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(label);
        var button = SmallButton("Ver detalhes", (_, _) => details());
        button.Padding = new Thickness(10, 3, 10, 3);
        button.FontSize = 11.5;
        Grid.SetColumn(button, 1);
        head.Children.Add(button);
        box.Children.Add(head);

        foreach (var r in rows)
        {
            var line = new Grid { Margin = new Thickness(0, 3, 0, 0) };
            line.ColumnDefinitions.Add(new ColumnDefinition());
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            string tip = string.IsNullOrEmpty(r.Note) ? r.Title : r.Title + "\n" + r.Note;
            line.Children.Add(new TextBlock { Text = ShortTitle(r.Title), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = tip, Margin = new Thickness(0, 0, 8, 0) });
            string state = r.State == RowState.Ok ? "✓ OK" : r.State == RowState.Different ? "▲ " + r.Current : r.Current;
            string brush = r.State == RowState.Ok ? "OkBrush" : r.State == RowState.Different ? "WarnBrush" : "MutedBrush";
            var status = new TextBlock { Text = state, FontSize = 12, Foreground = Brush(brush), ToolTip = tip, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 170 };
            Grid.SetColumn(status, 1);
            line.Children.Add(status);
            box.Children.Add(line);
        }

        if (rows.Count == 0) box.Children.Add(Muted("Nada para mostrar aqui.", 11));
        return box;
    }

    private static string ShortTitle(string title)
    {
        string t = title.Split('(')[0].Trim();
        if (t.Length == 0) t = title;
        return t.Length <= 46 ? t : t[..45].TrimEnd() + "…";
    }

    // ---- detalhes (painel expandido no lugar da grade)

    private UIElement DetailSection(GameView v)
    {
        var game = v.Game;
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        switch (_detailTab)
        {
            case 0:
                panel.Children.Add(BuildTable(v.Rows));
                if (_maker == GpuMaker.Nvidia)
                {
                    var adv = new CheckBox
                    {
                        Content = "Incluir ajustes avançados do driver (experimentais; cada um tem explicação e é desfeito pelo \"Desfazer\")",
                        IsChecked = _advanced, Foreground = Brush("TextBrush"), FontSize = 11.5, Margin = new Thickness(0, 2, 0, 6),
                    };
                    adv.Click += async (_, _) => { _advanced = adv.IsChecked == true; await RenderDetailAsync(game); };
                    panel.Children.Add(adv);
                    if (DriverCatalog.For(game.ExeName) is { } cat)
                        panel.Children.Add(Muted($"Perfil de driver do catálogo: {cat.Title}. Fonte dos valores: NVIDIA Profile Inspector (licença MIT) e guias da comunidade; veja docs/PERFIS-DRIVER-NVIDIA.md.", 10.5));
                }
                var tweakNames = v.Plan.TweakIds.Select(id => AppServices.Tweaks.FirstOrDefault(t => t.Id == id)).Where(t => t is { Risk: MBExtreme.Core.Tweaks.TweakRisk.Low } t2 && t2.IsAvailable).Select(t => "• " + t!.Title).ToList();
                if (tweakNames.Count > 0) panel.Children.Add(new Expander { Header = $"O que será ajustado no Windows ({tweakNames.Count})", Content = Muted(string.Join("\n", tweakNames), 11), Foreground = Brush("TextBrush"), Margin = new Thickness(0, 4, 0, 0) });
                panel.Children.Add(new Expander { Header = "Dicas de configuração do jogo para o seu hardware", Content = Muted(string.Join("\n\n", v.Plan.Advice), 11), Foreground = Brush("TextBrush") });
                break;
            case 1:
                panel.Children.Add(BuildTable(v.NetRows, "Rede do jogo"));
                panel.Children.Add(BuildNetwork(game, v.Settings.OptimizedAt is not null));
                break;
            case 2:
                panel.Children.Add(BuildPerformance(game));
                break;
            default:
                var info = v.Info;
                if (info is { ShortDescription.Length: > 0 }) panel.Children.Add(Muted(info.ShortDescription, 11.5));
                if (v.Requirements is { Verdict: not Fit.Unknown } or { Minimum.Count: > 0 }) panel.Children.Add(RequirementTable(v.Requirements!));
                if (info is { MinimumRequirements.Length: > 0 } or { RecommendedRequirements.Length: > 0 })
                {
                    if (info!.MinimumRequirements.Length > 0) panel.Children.Add(Muted("\n" + info.MinimumRequirements, 11.5));
                    if (info.RecommendedRequirements.Length > 0) panel.Children.Add(Muted("\n" + info.RecommendedRequirements, 11.5));
                }
                else panel.Children.Add(Muted("A loja não informou requisitos para este jogo.", 11.5));
                break;
        }

        return panel;
    }

    /// <summary>Seu PC contra o que o jogo pede: cada item com ✓, ≈ (por pouco) ou ✗.</summary>
    private UIElement RequirementTable(RequirementReport report)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 4) };
        string brush = report.Verdict switch { Fit.Recommended => "OkBrush", Fit.Minimum => "OkBrush", Fit.Below => "BadBrush", _ => "MutedBrush" };
        panel.Children.Add(new TextBlock { Text = report.Summary, FontWeight = FontWeights.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = Brush(brush) });
        void Section(string title, IReadOnlyList<RequirementItem> items)
        {
            if (items.Count == 0) return;
            panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
            foreach (var item in items)
            {
                string mark = item.Status switch { ItemStatus.Ok => "✓", ItemStatus.Close => "≈", ItemStatus.Below => "✗", ItemStatus.Info => "·", _ => "?" };
                string color = item.Status switch { ItemStatus.Ok => "OkBrush", ItemStatus.Close => "WarnBrush", ItemStatus.Below => "BadBrush", _ => "MutedBrush" };
                var line = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
                var head = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
                head.Inlines.Add(new System.Windows.Documents.Run(mark + "  ") { Foreground = Brush(color), FontWeight = FontWeights.Bold });
                head.Inlines.Add(new System.Windows.Documents.Run(item.Name + ": ") { FontWeight = FontWeights.SemiBold });
                head.Inlines.Add(new System.Windows.Documents.Run($"pede {item.Required} · você tem {item.Yours}") { Foreground = Brush("MutedBrush") });
                line.Children.Add(head);
                if (item.Note is { Length: > 0 } note && item.Status is not ItemStatus.Ok) line.Children.Add(Muted(note, 11));
                panel.Children.Add(line);
            }
        }
        Section("Mínimo", report.Minimum);
        Section("Recomendado", report.Recommended);
        panel.Children.Add(Muted("A comparação de processador e placa de vídeo é uma estimativa pelo nível do modelo (tabela do MB Extreme), não um teste. Memória, memória de vídeo e Windows são números exatos.", 11));
        return panel;
    }

    private IReadOnlyList<SettingRow> ReadNetworkRows(InstalledGame game)
    {
        var states = new Dictionary<string, bool?>();
        var titles = new Dictionary<string, string>();
        foreach (string id in GameNetworkPlan.TweakIds)
        {
            var tweak = AppServices.Tweaks.FirstOrDefault(t => t.Id == id);
            if (tweak is null || !tweak.IsAvailable) continue;
            titles[id] = tweak.Title;
            try { states[id] = tweak.IsApplied(); } catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or InvalidOperationException) { states[id] = null; }
        }
        bool canQos = QosCommands.IsSafeExe(game.ExeName);
        bool? qos = null;
        if (canQos) try { qos = AppServices.GameQos.IsActive(game.ExeName!); } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        return GameNetworkPlan.Rows(states, titles, qos, canQos);
    }

    private async Task OptimizeNetworkAsync(InstalledGame game, GameProfile profile)
    {
        SetStatus("Otimizando a rede deste jogo...");
        var plan = new OptimizationPlan(profile, GameNetworkPlan.TweakIds, Array.Empty<string>());
        var result = await Task.Run(() => AppServices.GameOptimization.Apply(game.Id, game.ExeName, plan));
        var parts = new List<string>();
        if (result.Applied.Count > 0) parts.Add($"{result.Applied.Count} ajuste(s) de rede ligado(s)");
        if (result.AlreadyOn.Count > 0) parts.Add($"{result.AlreadyOn.Count} já estava(m) ligado(s)");
        if (result.Failed.Count > 0) parts.Add($"{result.Failed.Count} falhou (execute como administrador)");
        if (QosCommands.IsSafeExe(game.ExeName))
        {
            try
            {
                await Task.Run(() => AppServices.GameQos.Apply(game.ExeName!));
                AppServices.GameLibrary.Update(game.Id, s => s.QosApplied = true);
                parts.Add("prioridade de rede do jogo ligada");
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                parts.Add("QoS: " + ex.Message);
            }
        }
        SetStatus((parts.Count == 0 ? "Nada a ajustar." : string.Join(" · ", parts) + ".") + " \"Desfazer otimização\" volta ao que era. Não troca a rota dos pacotes: isso precisa de servidor.", result.Failed.Count > 0 ? "WarnBrush" : "OkBrush");
        RefreshCard(game);
        await RenderDetailAsync(game);
    }

    private IReadOnlyList<SettingRow> ReadRows(InstalledGame game, GameProfile profile)
    {
        var states = new Dictionary<string, bool?>();
        var titles = new Dictionary<string, string>();
        foreach (string id in profile.TweakIds)
        {
            var tweak = AppServices.Tweaks.FirstOrDefault(t => t.Id == id);
            if (tweak is null || !tweak.IsAvailable) continue;
            titles[id] = tweak.Title;
            try { states[id] = tweak.IsApplied(); } catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or InvalidOperationException) { states[id] = null; }
        }

        bool? high = null;
        if (game.ExeName is not null)
            try { high = AppServices.GamePriority.IsApplied(game.ExeName); } catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { }

        string? exePath = GameFiles.FindExePath(game);
        GpuPreferenceMode? gpu = exePath is null ? null : new RegistryGpuPreferenceStore().Get(exePath);
        bool driver = _maker == GpuMaker.Nvidia && AppServices.NvidiaDriver.Available;
        DriverValue? power = null, pre = null;
        if (driver && game.ExeName is { } exe)
        {
            power = AppServices.NvidiaDriver.Read(exe, DriverSettingIds.PowerMode);
            pre = AppServices.NvidiaDriver.Read(exe, DriverSettingIds.PreRenderLimit);
        }
        string? dlss = _maker == GpuMaker.Nvidia ? DlssInfo.Find(game.InstallPath) : null;
        var catalog = DriverCatalog.SettingsFor(game.ExeName, _advanced);
        var values = new Dictionary<uint, DriverValue?>();
        if (driver && game.ExeName is { } exeCat)
            foreach (var item in catalog) values[item.Id] = AppServices.NvidiaDriver.Read(exeCat, item.Id);
        return GameCompare.Build(profile, _maker, new GameSnapshot(states, titles, high, gpu, driver, power, pre, dlss, values), catalog, DriverCatalog.For(game.ExeName)?.HasReflex ?? false);
    }

    private UIElement BuildTable(IReadOnlyList<SettingRow> rows, string heading = "Configuração atual × recomendada")
    {
        var box = new StackPanel { Margin = new Thickness(0, 6, 0, 4) };
        int fix = rows.Count(r => r.CanFix || r.State == RowState.Different);
        box.Children.Add(new TextBlock
        {
            Text = fix == 0 ? $"{heading}: tudo certo" : $"{heading}: {fix} ponto(s) para ajustar",
            FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4),
        });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        void Cell(string text, int row, int col, string brush, bool bold = false, string? tip = null)
        {
            var t = new TextBlock { Text = text, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 8, 2), Foreground = Brush(brush), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
            if (!string.IsNullOrEmpty(tip)) t.ToolTip = tip;
            Grid.SetRow(t, row);
            Grid.SetColumn(t, col);
            grid.Children.Add(t);
        }
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Cell("Configuração", 0, 0, "MutedBrush", true);
        Cell("Agora", 0, 1, "MutedBrush", true);
        Cell("Recomendado", 0, 2, "MutedBrush", true);
        for (int i = 0; i < rows.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var r = rows[i];
            string now = r.State switch { RowState.Ok => "OkBrush", RowState.Different => "WarnBrush", _ => "TextBrush" };
            Cell(r.Title, i + 1, 0, "TextBrush", false, r.Note);
            Cell((r.State == RowState.Ok ? "✓ " : r.State == RowState.Different ? "▲ " : "") + r.Current, i + 1, 1, now, false, r.Note);
            Cell(r.Recommended, i + 1, 2, "TextBrush", false, r.Note);
        }
        box.Children.Add(grid);
        box.Children.Add(Muted("Passe o mouse sobre uma linha para ver a explicação. \"Otimizar\" liga o que está em amarelo; nada é aplicado sem você clicar, e o valor de antes fica guardado para desfazer.", 10.5));
        return box;
    }

    private static Button Tight(Button b)
    {
        b.Margin = new Thickness(0, 0, 8, 6);
        return b;
    }

    private WrapPanel Chips(IReadOnlyList<string> options, int selected, Action<int> onChange)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
        var buttons = new List<Button>();
        void Paint(int index)
        {
            for (int i = 0; i < buttons.Count; i++) buttons[i].Style = (Style)FindResource(i == index ? "PrimaryButton" : "GhostButton");
            foreach (var b in buttons) { b.Padding = new Thickness(11, 4, 11, 4); b.FontSize = 12; }
        }
        for (int i = 0; i < options.Count; i++)
        {
            int index = i;
            var button = new Button { Content = options[i], Margin = new Thickness(0, 0, 6, 6) };
            button.Click += (_, _) => { Paint(index); onChange(index); };
            buttons.Add(button);
            panel.Children.Add(button);
        }
        Paint(selected);
        return panel;
    }

    // ------------------------------------------------------------ ações

    private async Task OptimizeAsync(InstalledGame game, OptimizationPlan plan, GameProfile profile)
    {
        var catalogProfile = DriverCatalog.For(game.ExeName);
        var driverPlan = DriverPlan.For(profile, _maker, catalogProfile?.HasReflex ?? false);
        var catalogItems = _maker == GpuMaker.Nvidia ? DriverCatalog.SettingsFor(game.ExeName, _advanced) : Array.Empty<DriverSetting>();
        var pending = (await Task.Run(() => ReadRows(game, profile))).Where(r => r.CanFix && r.Group is "Driver NVIDIA" or "Placa de vídeo").ToList();
        bool withDriver = false;
        if (pending.Count > 0 && (!driverPlan.IsEmpty || catalogItems.Count > 0))
        {
            var ask = MessageBox.Show(
                "Além dos ajustes do Windows, posso mudar estas configurações só para " + game.Name + ":\n\n" +
                string.Join("\n", pending.Select(r => $"• {r.Title}: {r.Current} → {r.Recommended}")) +
                "\n\nO valor de antes fica guardado e \"Desfazer otimização\" devolve. Mudar agora?",
                "Configurações do driver e da placa de vídeo", MessageBoxButton.YesNo, MessageBoxImage.Question);
            withDriver = ask == MessageBoxResult.Yes;
        }

        SetStatus("Aplicando a otimização...");
        var result = await Task.Run(() => AppServices.GameOptimization.Apply(game.Id, game.ExeName, plan));
        var parts = new List<string>();
        if (result.Applied.Count > 0) parts.Add($"{result.Applied.Count} ajuste(s) ligado(s)");
        if (result.AlreadyOn.Count > 0) parts.Add($"{result.AlreadyOn.Count} já estava(m) ligado(s)");
        if (result.Failed.Count > 0) parts.Add($"{result.Failed.Count} falhou (execute como administrador)");
        if (withDriver && game.ExeName is { } exeName)
        {
            string? exePath = await Task.Run(() => GameFiles.FindExePath(game));
            var drv = await Task.Run(() => AppServices.GameDriver.Apply(game.Id, exeName, exePath, driverPlan, catalogItems));
            if (drv.Applied.Count > 0) parts.Add($"{drv.Applied.Count} configuração(ões) de driver/placa");
            if (drv.Failed.Count > 0) parts.Add("driver: " + string.Join("; ", drv.Failed));
        }
        SetStatus((parts.Count == 0 ? "Nada a ajustar." : string.Join(" · ", parts) + ".") + " Alguns ajustes valem para o PC todo; \"Desfazer otimização\" volta ao que era.", result.Failed.Count > 0 ? "WarnBrush" : "OkBrush");
        RefreshCard(game);
        await RenderDetailAsync(game);
    }

    private async Task UndoAsync(InstalledGame game)
    {
        var result = await Task.Run(() => AppServices.GameOptimization.Undo(game.Id));
        if (AppServices.GameLibrary.Get(game.Id).QosApplied && QosCommands.IsSafeExe(game.ExeName))
        {
            try
            {
                await Task.Run(() => AppServices.GameQos.Remove(game.ExeName!));
                AppServices.GameLibrary.Update(game.Id, s => s.QosApplied = false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { SetStatus("Não foi possível remover a prioridade de rede: " + ex.Message, "WarnBrush"); }
        }
        var drvUndo = await Task.Run(() => AppServices.GameDriver.Undo(game.Id));
        SetStatus($"{result.Applied.Count + drvUndo.Applied.Count} ajuste(s) desfeito(s)" + (drvUndo.Failed.Count > 0 ? " (driver: " + string.Join("; ", drvUndo.Failed) + ")" : "") + (result.AlreadyOn.Count > 0 ? $"; {result.AlreadyOn.Count} mantido(s) porque outro jogo ainda usa" : "") + ".", "OkBrush");
        RefreshCard(game);
        await RenderDetailAsync(game);
    }

    private void RefreshCard(InstalledGame game)
    {
        if (!_cards.ContainsKey(game.Id)) return;
        var fresh = BuildCard(game);
        int at = _grid.Children.IndexOf(_cards[game.Id]);
        _grid.Children.RemoveAt(at);
        _grid.Children.Insert(at, fresh);
        _cards[game.Id] = fresh;
        if (_selected?.Id == game.Id) { fresh.BorderBrush = Brush("AccentBrush"); fresh.BorderThickness = new Thickness(2); }
    }

    private void TogglePriority(InstalledGame game, bool applied)
    {
        try
        {
            if (applied) AppServices.GamePriority.Revert(game.ExeName!);
            else AppServices.GamePriority.Apply(game.ExeName!);
            AppServices.GameLibrary.Update(game.Id, s => { s.HighPriority = !applied; s.ExeName = game.ExeName; });
            SetStatus(applied ? $"Prioridade de {game.Name} voltou ao normal." : $"{game.Name} abrirá com prioridade alta de CPU.", "OkBrush");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            SetStatus("Não foi possível alterar (execute o MB Extreme como administrador): " + ex.Message, "BadBrush");
        }
        _ = RenderDetailAsync(game);
    }

    private void Play(InstalledGame game, bool measure = false)
    {
        if (string.IsNullOrWhiteSpace(game.LaunchTarget))
        {
            SetStatus("Não sei como abrir este jogo. Abra pela loja dele.", "WarnBrush");
            return;
        }

        if (game.ExeName is { } exe)
        {
            string process = Path.GetFileNameWithoutExtension(exe);
            AppServices.GameLibrary.Update(game.Id, s => s.ExeName = exe);
            var settings = AppServices.Boost.Settings;
            if (!settings.GameProcesses.Contains(process, StringComparer.OrdinalIgnoreCase))
            {
                settings.GameProcesses.Add(process);
                settings.Save();
            }
            AppServices.Boost.RefreshGames();
            if (!settings.BoostAuto) AppServices.Boost.SetAuto(true);
        }

        if (measure)
        {
            string? problem = AppServices.GameMeasure.Start(game, AppServices.GameLibrary.Get(game.Id).OptimizedAt is not null);
            if (problem is not null) { SetStatus(problem, "BadBrush"); return; }
        }

        try
        {
            if (LaunchWithOptions(game)) { if (measure) _ = RenderDetailAsync(game); return; }
            Process.Start(new ProcessStartInfo(game.LaunchTarget) { UseShellExecute = true, WorkingDirectory = game.Launcher == GameLauncher.Manual ? game.InstallPath : "" });
            SetStatus(game.ExeName is null
                ? $"Abrindo {game.Name}..."
                : $"Abrindo {game.Name}. O Modo Jogo liga sozinho quando o jogo aparecer e desliga quando você fechar.", "OkBrush");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            if (measure) AppServices.GameMeasure.Stop();
            SetStatus("Não foi possível abrir: " + ex.Message, "BadBrush");
        }
        if (measure) _ = RenderDetailAsync(game);
    }

    // ------------------------------------------------------------ rede: rota, ping e atraso sob carga

    private CancellationTokenSource? _netCts;
    private readonly TextBlock _netStatus = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(0, 6, 0, 4) };
    private readonly StackPanel _netResult = new();
    private string _lastHost = "";
    private string _manualHost = "";

    private UIElement BuildNetwork(InstalledGame game, bool optimized)
    {
        var box = new StackPanel();
        box.Children.Add(Muted("Descobre com quais servidores o jogo está falando, mede ping, perda e oscilação e mostra o caminho salto a salto para achar onde está o atraso. Só mede; não muda nada no PC. Abra o jogo e entre numa partida antes de analisar.", 11));

        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        bool running = _netCts is not null;
        buttons.Children.Add(Tight(SmallButton("Analisar rota do jogo", async (_, _) => await AnalyzeGameAsync(game, optimized), !running)));
        buttons.Children.Add(Tight(SmallButton("Teste de atraso sob carga", async (_, _) => await BufferbloatAsync())));
        if (running) buttons.Children.Add(Tight(SmallButton("Parar", (_, _) => _netCts?.Cancel())));
        box.Children.Add(buttons);

        var hostRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        hostRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hostRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var host = new TextBox { Text = _manualHost, ToolTip = "Endereço ou IP de um servidor para testar (ex.: o IP que o jogo mostra)" };
        host.TextChanged += (_, _) => _manualHost = host.Text;
        hostRow.Children.Add(host);
        var test = SmallButton("Testar este endereço", async (_, _) => await ProbeManualAsync(host.Text));
        test.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(test, 1);
        hostRow.Children.Add(test);
        box.Children.Add(hostRow);

        // os mesmos blocos de texto são reaproveitados a cada desenho do painel: soltar do desenho anterior
        (_netStatus.Parent as Panel)?.Children.Remove(_netStatus);
        (_netResult.Parent as Panel)?.Children.Remove(_netResult);
        box.Children.Add(_netStatus);
        box.Children.Add(_netResult);

        var reports = AppServices.NetReports.Get(game.Id);
        var cmp = NetCompare.Build(reports);
        box.Children.Add(new TextBlock { Text = "Ping antes e depois da otimização", FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 2) });
        box.Children.Add(Muted(cmp.Message, 11));
        foreach (string w in cmp.Warnings) box.Children.Add(Muted("• " + w, 10.5));
        if (reports.Count > 0)
        {
            var clear = SmallButton("Apagar medidas de rede deste jogo", (_, _) => { AppServices.NetReports.Clear(game.Id); _ = RenderDetailAsync(game); });
            clear.Margin = new Thickness(0, 4, 0, 0);
            box.Children.Add(clear);
        }
        box.Children.Add(Muted("Trocar o caminho dos pacotes (rota diferente da do seu provedor) precisa de um servidor próprio de repasse; veja docs/REDE-PRIVADA-JOGOS.md.", 10.5));
        return box;
    }

    private void NetSay(string text, string brush = "MutedBrush")
    {
        _netStatus.Text = text;
        _netStatus.Foreground = Brush(brush);
    }

    private async Task AnalyzeGameAsync(InstalledGame game, bool optimized)
    {
        if (_netCts is not null) return;
        if (game.ExeName is null) { NetSay("Não sei qual é o executável deste jogo.", "WarnBrush"); return; }
        var pids = GameConnections.PidsOf(game.ExeName);
        if (pids.Count == 0) { NetSay($"O jogo não está aberto. Abra {game.Name} (botão Jogar), entre numa partida e clique em Analisar de novo.", "WarnBrush"); return; }

        _netCts = new CancellationTokenSource();
        var token = _netCts.Token;
        _netResult.Children.Clear();
        var watcher = new UdpEndpointWatcher();
        try
        {
            string udpNote = "";
            try { watcher.Start(); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                udpNote = " (sem permissão para ver o tráfego UDP; execute como administrador)";
            }

            for (int s = 8; s > 0; s--)
            {
                NetSay($"Observando as conexões do jogo ({s} s)...{udpNote}");
                await Task.Delay(1000, token);
            }

            var all = new List<RemoteEndpoint>();
            all.AddRange(await Task.Run(() => GameConnections.TcpEndpointsOf(pids), token));
            all.AddRange(watcher.EndpointsOf(pids));
            var targets = GameEndpoints.Top(all, 2);
            if (targets.Count == 0)
            {
                NetSay("Não vi conexões do jogo com a internet. Entre numa partida online e tente de novo." + udpNote, "WarnBrush");
                return;
            }

            bool first = true;
            foreach (var target in targets)
            {
                string address = target.Address.ToString();
                var progress = new Progress<string>(t => NetSay($"{address}:{target.Port} ({target.Protocol}) — {t}"));
                var diagnosis = await RouteProbe.RunAsync(address, target.Protocol == "TCP" ? target.Port : 0, 15, 4, progress, token);
                RenderDiagnosis(diagnosis, $"{target.Protocol} porta {target.Port}");
                if (first && diagnosis.Samples > 0 && diagnosis.Verdict != "Sem resposta")
                {
                    AppServices.NetReports.Add(game.Id, new NetReport
                    {
                        At = DateTime.Now, Host = address, Optimized = optimized, AvgMs = Math.Round(diagnosis.Average, 1),
                        JitterMs = Math.Round(diagnosis.Jitter, 1), LossPercent = Math.Round(diagnosis.LossPercent, 1), Samples = diagnosis.Samples,
                    });
                    _lastHost = address;
                }
                first = false;
            }
            NetSay("Análise concluída e guardada para comparar antes e depois.", "OkBrush");
        }
        catch (OperationCanceledException)
        {
            NetSay("Análise parada.");
        }
        finally
        {
            watcher.Dispose();
            _netCts = null;
        }
        if (_selected?.Id == game.Id) await RenderDetailAsync(game);
    }

    private async Task ProbeManualAsync(string text)
    {
        string host = text.Trim();
        if (_netCts is not null) return;
        if (!NetAnalyzer.IsValidHost(host)) { NetSay("Escreva um endereço ou IP válido.", "WarnBrush"); return; }
        _netCts = new CancellationTokenSource();
        _netResult.Children.Clear();
        try
        {
            var diagnosis = await RouteProbe.RunAsync(host, 443, 15, 4, new Progress<string>(t => NetSay($"{host} — {t}")), _netCts.Token);
            RenderDiagnosis(diagnosis, "teste manual");
            _lastHost = host;
            NetSay("Teste concluído.", "OkBrush");
        }
        catch (OperationCanceledException) { NetSay("Teste parado."); }
        finally { _netCts = null; }
    }

    private async Task BufferbloatAsync()
    {
        if (_netCts is not null) return;
        _netCts = new CancellationTokenSource();
        _netResult.Children.Clear();
        try
        {
            string target = string.IsNullOrEmpty(_lastHost) ? "1.1.1.1" : _lastHost;
            var result = await Bufferbloat.RunAsync(target, Http, new Progress<string>(t => NetSay(t)), _netCts.Token);
            if (result is null) { NetSay("Sem resposta do servidor de teste (" + target + "). Verifique a internet.", "WarnBrush"); return; }
            var card = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
            card.Children.Add(new TextBlock { Text = $"Atraso sob carga: nota {result.Grade}", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush(result.Grade is "A" or "B" ? "OkBrush" : result.Grade == "C" ? "WarnBrush" : "BadBrush") });
            card.Children.Add(Muted($"Ping parado: {result.IdleMs:0} ms · durante download: {(result.DownloadLoadedMs is { } d ? d.ToString("0") + " ms" : "sem medida")} · durante upload: {(result.UploadLoadedMs is { } u ? u.ToString("0") + " ms" : "sem medida")}", 11));
            card.Children.Add(Muted(result.Advice, 11));
            _netResult.Children.Add(card);
            NetSay("Teste concluído (usa o servidor de velocidade da Cloudflare só como carga).", "OkBrush");
        }
        catch (OperationCanceledException) { NetSay("Teste parado."); }
        finally { _netCts = null; }
    }

    private void RenderDiagnosis(RouteDiagnosis d, string origin)
    {
        var card = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        string verdictBrush = d.Verdict is "Excelente" or "Bom" ? "OkBrush" : d.Verdict == "Regular" ? "WarnBrush" : "BadBrush";
        card.Children.Add(new TextBlock { Text = $"{d.Host} · {d.Verdict}", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush(verdictBrush) });
        card.Children.Add(Muted($"{origin} · média {d.Average:0.#} ms (mín {d.Min:0.#}, máx {d.Max:0.#}) · oscilação {d.Jitter:0.#} ms · perda {d.LossPercent:0.#}% em {d.Samples} testes", 11));
        foreach (var f in d.Findings)
        {
            string brush = f.Level == FindingLevel.Bad ? "BadBrush" : f.Level == FindingLevel.Warn ? "WarnBrush" : "MutedBrush";
            card.Children.Add(new TextBlock { Text = (f.Level == FindingLevel.Info ? "• " : "▲ ") + f.Title, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Brush(brush), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
            card.Children.Add(Muted(f.Detail, 10.5));
        }
        if (d.Hops.Count > 0)
        {
            string lines = string.Join("\n", d.Hops.Select(h => h.Received == 0 ? $"{h.Hop,2}  {h.Address,-18}  sem resposta" : $"{h.Hop,2}  {h.Address,-18}  {h.Average,6:0.#} ms  perda {h.LossPercent:0}%"));
            card.Children.Add(new Expander { Header = $"Caminho salto a salto ({d.Hops.Count})", Foreground = Brush("TextBrush"), Content = new TextBlock { Text = lines, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 10.5, Foreground = Brush("MutedBrush") } });
        }
        _netResult.Children.Add(card);
    }

    private void OnMeasureChanged(object? sender, EventArgs e) => SetStatus(AppServices.GameMeasure.Status, "OkBrush");

    private void OnMeasureFinished(string gameId, PerfSession? session)
    {
        if (_selected?.Id == gameId) _ = RenderDetailAsync(_selected);
    }

    private UIElement BuildPerformance(InstalledGame game)
    {
        var sessions = AppServices.Perf.Get(game.Id);
        var box = new StackPanel { Margin = new Thickness(0, 6, 0, 4) };
        box.Children.Add(new TextBlock { Text = "Desempenho medido (FPS)", FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var cmp = PerfCompare.Build(sessions);
        if (cmp.Before is not null && cmp.After is not null)
        {
            var grid = new Grid();
            for (int c = 0; c < 4; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(c == 0 ? 1.4 : 1, GridUnitType.Star) });
            void Cell(string text, int row, int col, string brush = "TextBrush", bool bold = false)
            {
                var t = new TextBlock { Text = text, FontSize = 11.5, Margin = new Thickness(0, 2, 8, 2), Foreground = Brush(brush), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
                Grid.SetRow(t, row); Grid.SetColumn(t, col); grid.Children.Add(t);
            }
            for (int r = 0; r < 4; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Cell("", 0, 0); Cell("Antes", 0, 1, "MutedBrush", true); Cell("Depois", 0, 2, "MutedBrush", true); Cell("Mudança", 0, 3, "MutedBrush", true);
            string good = cmp.Verdict == Verdict.Worse ? "WarnBrush" : "OkBrush";
            Cell("FPS médio", 1, 0); Cell($"{cmp.Before.AvgFps:0.#}", 1, 1); Cell($"{cmp.After.AvgFps:0.#}", 1, 2); Cell($"{cmp.AvgChangePercent:+0.#;-0.#;0}%", 1, 3, good, true);
            Cell("1% pior", 2, 0); Cell($"{cmp.Before.Low1Fps:0.#}", 2, 1); Cell($"{cmp.After.Low1Fps:0.#}", 2, 2); Cell($"{cmp.LowChangePercent:+0.#;-0.#;0}%", 2, 3, good, true);
            Cell("Duração", 3, 0); Cell($"{cmp.Before.Seconds} s", 3, 1); Cell($"{cmp.After.Seconds} s", 3, 2);
            box.Children.Add(grid);
        }
        box.Children.Add(Muted(cmp.Message, 11));
        foreach (string w in cmp.Warnings) box.Children.Add(Muted("• " + w, 10.5));
        box.Children.Add(Muted(sessions.Count == 0
            ? "Use \"Medir FPS\": o MB Extreme abre o jogo, mede enquanto você joga e guarda a sessão ao fechar o jogo. Faça o mesmo trecho (2 a 3 minutos) antes e depois de otimizar. A medição lê eventos do Windows, sem mexer no jogo (antitrapaça), e precisa de administrador."
            : $"{sessions.Count} sessão(ões) guardada(s). Para comparar com justiça, jogue o mesmo mapa/trecho.", 10.5));
        if (sessions.Count > 0)
        {
            var clear = SmallButton("Apagar medidas deste jogo", (_, _) => { AppServices.Perf.Clear(game.Id); _ = RenderDetailAsync(game); });
            clear.Margin = new Thickness(0, 4, 0, 0);
            box.Children.Add(clear);
        }
        return box;
    }

    private void AddManual()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Programas (*.exe)|*.exe", Title = "Escolha o executável do jogo ou programa" };
        if (dialog.ShowDialog() != true) return;
        string name = Path.GetFileNameWithoutExtension(dialog.FileName);
        AppServices.GameLibrary.AddManual(name, dialog.FileName);
        _ = ScanAsync();
    }
}
