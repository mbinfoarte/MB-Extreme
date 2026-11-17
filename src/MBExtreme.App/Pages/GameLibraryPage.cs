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
        var leftCol = new DockPanel();
        DockPanel.SetDock(_filters, Dock.Top);
        leftCol.Children.Add(_filters);
        leftCol.Children.Add(gridScroll);
        Grid.SetColumn(leftCol, 0);
        _split.Children.Add(leftCol);

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
        Body.Children.Add(_offer);
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
            // perfis publicados no painel do administrador: busca os mais novos e avisa quais jogos daqui têm um pronto
            try
            {
                if (await GameTitleSync.RefreshAsync(AppServices.Web, MBExtreme.Core.AppInfo.FirebaseProjectId, MBExtreme.Core.AppInfo.FirestoreDatabaseId) > 0) RenderGrid();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException or System.Text.Json.JsonException) { }
            ShowProfileOffer();
        }
        finally
        {
            _busy = false;
        }
    }

    // ------------------------------------------------------------ Perfil MB (perfis publicados no site) e filtro

    private readonly Border _offer = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 10) };
    private readonly WrapPanel _filters = new() { Margin = new Thickness(0, 0, 0, 6) };
    private int _filter; // 0 todos, 1 com Perfil MB, 2 otimizados
    private bool? _driverAnswer;

    private static bool Profiled(GameSettings s) => s.OptimizedAt is not null || s.AutoProfile;

    /// <summary>Versão do perfil (muda quando o perfil é editado no site), para avisar de novo só quando algo mudou.</summary>
    private static string OfferKey(GameTitle t) =>
        t.Key + "@" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(GameTitles.ToJson(new[] { t }))))[..12];

    private List<(InstalledGame Game, GameTitle Title)> Matches() =>
        _games.Select(g => (Game: g, Title: TitleOf(g))).Where(x => x.Title is not null).Select(x => (x.Game, x.Title!)).ToList();

    private void ShowProfileOffer()
    {
        var pending = Matches().Where(m =>
        {
            var s = AppServices.GameLibrary.Get(m.Game.Id);
            return !Profiled(s) && s.ProfileOffered != OfferKey(m.Title);
        }).ToList();
        if (pending.Count == 0) { _offer.Visibility = Visibility.Collapsed; return; }

        var yellow = System.Windows.Media.Color.FromRgb(0xFF, 0xC4, 0x00);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var badge = MBExtreme.UI.Controls.GlassFx.Badge("\uE734", yellow, 34);
        badge.Margin = new Thickness(0, 0, 12, 0);
        grid.Children.Add(badge);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        string names = string.Join(", ", pending.Take(5).Select(p => p.Game.Name)) + (pending.Count > 5 ? $" e mais {pending.Count - 5}" : "");
        text.Children.Add(new TextBlock { Text = pending.Count == 1 ? $"{pending[0].Game.Name} tem Perfil MB pronto" : $"Achei {pending.Count} jogos com Perfil MB pronto", FontSize = 14, FontWeight = FontWeights.SemiBold });
        text.Children.Add(Muted(names + ". O perfil ajusta o Windows, o driver, a rede e a prioridade para cada jogo; entra quando o jogo abre e sai quando fecha.", 11.5));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var choose = SmallButton("Escolher quais", (_, _) => { SetFilter(1); });
        var apply = SmallButton(pending.Count == 1 ? "Aplicar (automático)" : $"Aplicar nos {pending.Count} (automático)", async (_, _) => await ApplyManyAsync(pending), true);
        var close = SmallButton("✕", (_, _) =>
        {
            foreach (var p in pending) AppServices.GameLibrary.Update(p.Game.Id, x => x.ProfileOffered = OfferKey(p.Title));
            _offer.Visibility = Visibility.Collapsed;
        });
        close.ToolTip = "Agora não (avisa de novo quando um perfil novo chegar)";
        buttons.Children.Add(choose);
        buttons.Children.Add(apply);
        buttons.Children.Add(close);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);

        var glass = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(1, 0) };
        glass.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x38, 0xFF, 0xC4, 0x00), 0));
        glass.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x08, 0xFF, 0xC4, 0x00), 1));
        _offer.Background = glass;
        _offer.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x70, 0xFF, 0xC4, 0x00));
        _offer.BorderThickness = new Thickness(1);
        _offer.CornerRadius = new CornerRadius(14);
        _offer.Padding = new Thickness(14, 10, 14, 10);
        _offer.Child = grid;
        _offer.Visibility = Visibility.Visible;
    }

    /// <summary>Aplica o Perfil MB (no modo automático) em vários jogos, um de cada vez.</summary>
    private async Task ApplyManyAsync(IReadOnlyList<(InstalledGame Game, GameTitle Title)> list)
    {
        if (_busy) return;
        var ask = MessageBox.Show(
            $"Aplicar o Perfil MB em {list.Count} jogo(s), no modo automático?\n\nOs ajustes do Windows entram só quando cada jogo abre e saem quando ele fecha. " +
            "Tudo fica guardado e o botão Desfazer de cada jogo volta ao que era.\n\nSim = também ajustar o driver e a placa de vídeo de cada jogo\nNão = só Windows, rede e prioridade",
            "Perfil MB", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (ask == MessageBoxResult.Cancel) return;
        _busy = true;
        _driverAnswer = ask == MessageBoxResult.Yes;
        _offer.Visibility = Visibility.Collapsed;
        int done = 0;
        try
        {
            foreach (var (game, title) in list)
            {
                SetStatus($"Aplicando o Perfil MB em {game.Name} ({++done} de {list.Count})...");
                var v = await BuildViewAsync(game, () => true);
                if (v is null) continue;
                await ApplyTitleAsync(game, v, auto: true);
                AppServices.GameLibrary.Update(game.Id, x => x.ProfileOffered = OfferKey(title));
            }
            SetStatus($"Perfil MB aplicado em {done} jogo(s), no automático.", "OkBrush");
        }
        finally
        {
            _driverAnswer = null;
            _busy = false;
            RenderGrid();
        }
    }

    private void SetFilter(int filter)
    {
        _filter = filter;
        RenderGrid();
    }

    private void RenderFilters()
    {
        _filters.Children.Clear();
        int withProfile = _games.Count(g => TitleOf(g) is not null);
        int optimized = _games.Count(g => Profiled(AppServices.GameLibrary.Get(g.Id)));
        foreach (var (i, label) in new[] { (0, $"Todos {_games.Count}"), (1, $"★ Com Perfil MB {withProfile}"), (2, $"Otimizados {optimized}") })
        {
            var chip = new System.Windows.Controls.Primitives.ToggleButton { Content = label, Style = (Style)FindResource("ChipToggle"), Margin = new Thickness(0, 0, 6, 6), IsChecked = _filter == i };
            int f = i;
            chip.Click += (_, _) => SetFilter(f);
            _filters.Children.Add(chip);
        }
    }

    private void RenderGrid()
    {
        _grid.Children.Clear();
        _cards.Clear();
        RenderFilters();
        foreach (var game in _games)
        {
            if (_filter == 1 && TitleOf(game) is null) continue;
            if (_filter == 2 && !Profiled(AppServices.GameLibrary.Get(game.Id))) continue;
            var card = BuildCard(game);
            _cards[game.Id] = card;
            _grid.Children.Add(card);
        }
        if (_selected is not null && _games.FirstOrDefault(g => g.Id == _selected.Id) is { } still && _cards.ContainsKey(still.Id)) Select(still, false);
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

        if (TitleOf(game) is not null)
            root.Children.Add(new Border
            {
                Background = Brush("AccentBrush"), CornerRadius = new CornerRadius(999), Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(6),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = "★ Perfil MB", FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.Black },
                ToolTip = Profiled(settings) ? "Perfil MB aplicado" : "Tem Perfil MB pronto: clique no jogo e em Aplicar perfil MB",
            });

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
        var view = await BuildViewAsync(game, () => _selected?.Id == game.Id);
        if (view is null || _selected?.Id != game.Id) return;
        _view = view;
        Paint();
    }

    /// <summary>Monta tudo o que o painel e o "Aplicar perfil" precisam de um jogo (perfil, plano, leituras do PC e requisitos).</summary>
    private async Task<GameView?> BuildViewAsync(InstalledGame game, Func<bool> stillWanted)
    {
        _storeInfo.TryGetValue(game.Id, out var info);
        var settings = AppServices.GameLibrary.Get(game.Id);
        var title = TitleOf(game);
        var profile = settings.ProfileKey is { } key ? GameProfiles.Find(key)
            : title is not null ? GameProfiles.Find(title.Profile)
            : game.Launcher == GameLauncher.Manual && game.ExeName is null ? GameProfiles.App
            : GameProfiles.Classify(game.ExeName, info?.Genres, info?.Categories);
        var plan = GameOptimizer.Plan(profile, _maker, _tier, onHdd: false);

        var rows = await Task.Run(() => ReadRows(game, profile));
        var netRows = await Task.Run(() => ReadNetworkRows(game));
        if (!stillWanted()) return null;

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
        return new GameView(game, info, settings, profile, plan, rows, netRows, requirements);
    }

    /// <summary>Desenha o painel do jogo: resumo e otimização na lateral, ou os detalhes ocupando o lugar da grade.</summary>
    private void Paint()
    {
        if (_view is not { } v) return;
        FitSplit();
        _sideTop.Children.Clear();
        _detail.Children.Clear();

        _sideTop.Children.Add(BuildHeader(v));

        _tab = Math.Clamp(_tab, 0, 3);
        _sideTop.Children.Add(Chips(new[] { "Resumo", "Desempenho", "Otimização", "Configurações" }, _tab, i => { _tab = i; Paint(); }));
        _detail.Children.Add(_tab switch { 0 => SummaryPanel(v), 1 => PerformancePanel(v), 2 => OptimizePanel(v), _ => ConfigPanel(v) });
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
        var s = v.Settings;
        var head = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var cover = new Border
        {
            Width = 84, Height = 126, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 12, 0), BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(1),
            Background = _coverImages.TryGetValue(game.Id, out var coverImage) ? new ImageBrush(coverImage) { Stretch = Stretch.UniformToFill } : Brush("Surface2Brush"),
        };
        head.Children.Add(cover);

        var right = new DockPanel();
        // Jogar grande + ações em ícones, embaixo
        var actions = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var play = SmallButton("▶  Jogar", (_, _) => Play(game), true);
        play.Margin = new Thickness(0);
        play.Padding = new Thickness(12, 8, 12, 8);
        play.FontSize = 14;
        actions.Children.Add(play);
        var icons = new StackPanel { Orientation = Orientation.Horizontal };
        if (AppServices.GameMeasure.GameId == game.Id)
            icons.Children.Add(IconButton("\uE71A", "Parar medição de FPS", () => AppServices.GameMeasure.Stop()));
        else if (game.ExeName is not null)
            icons.Children.Add(IconButton("\uE916", "Medir FPS: abre o jogo e mede enquanto você joga; a sessão é guardada ao fechar o jogo.", () => Play(game, true)));
        icons.Children.Add(IconButton("\uE91B", "Buscar capa na internet", async () => await PickCoverAsync(game)));
        if (Profiled(s)) icons.Children.Add(IconButton("\uE7A7", "Desfazer tudo o que o perfil mudou neste jogo", async () => await UndoTitleAsync(game)));
        Grid.SetColumn(icons, 1);
        actions.Children.Add(icons);
        DockPanel.SetDock(actions, Dock.Bottom);
        right.Children.Add(actions);

        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = game.Name, FontSize = 19, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        string played = s.MinutesPlayed < 1 ? "sem registro de tempo" : $"{s.MinutesPlayed / 60:0.#} h jogadas" + (s.LastPlayed is { } last ? $" · último: {last:dd/MM}" : "");
        titles.Children.Add(Muted($"{game.LauncherName} · {played}", 11));
        var tags = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var title = TitleOf(game);
        if (title is not null) tags.Children.Add(Tag(Profiled(s) ? "★ Perfil MB aplicado" : "★ Perfil MB disponível", true));
        tags.Children.Add(Tag(ShortTitle(v.Profile.Title), false));
        if (s.SessionActive) tags.Children.Add(Tag("● ligado agora", false, "OkBrush"));
        if (MBExtreme.Core.Gamepads.PadProfiles.ForGame(PadProfileHub.All(), game.ExeName) is { } pad) tags.Children.Add(Tag("Controle: " + pad.Name, false));
        titles.Children.Add(tags);
        right.Children.Add(titles);
        Grid.SetColumn(right, 1);
        head.Children.Add(right);
        return head;
    }

    private Border Tag(string text, bool accent, string? brush = null) => new()
    {
        CornerRadius = new CornerRadius(999), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 4), BorderThickness = new Thickness(1),
        Background = accent ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x30, 0xFF, 0xC4, 0x00)) : Brush("Surface2Brush"),
        BorderBrush = accent ? Brush("AccentBrush") : Brush("BorderBrush"),
        Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = accent ? Brush("AccentBrush") : Brush(brush ?? "TextBrush") },
    };

    private Button IconButton(string glyph, string tip, Action click)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), FontSize = 15 },
            Style = (Style)FindResource("GhostButton"), Width = 38, Height = 38, Padding = new Thickness(0), Margin = new Thickness(6, 0, 0, 0), ToolTip = tip,
        };
        System.Windows.Automation.AutomationProperties.SetName(b, tip);
        b.Click += (_, _) => click();
        return b;
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
        panel.Children.Add(ProfileCard(v));
        var tiles = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(tiles);

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
        tiles.Children.Add(StatTile("\uE73E", "Otimização", optLine, optBrush,
            $"Jogo: {(gameFix == 0 ? "tudo certo" : gameFix + " a ajustar")} · Rede: {(netFix == 0 ? "tudo certo" : netFix + " a ajustar")}",
            "Ver ajustes", () => { _tab = 2; Paint(); }));

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
        tiles.Children.Add(StatTile("\uE9D9", "Desempenho", fpsLine, fpsBrush, fpsSub, "Ver detalhes", () => { _tab = 1; Paint(); }));

        // 3) rede
        var report = AppServices.NetReports.Get(game.Id).LastOrDefault();
        tiles.Children.Add(StatTile("\uE968", "Rede e rota",
            report is null ? "Rota ainda não analisada" : $"Ping médio {report.AvgMs:0} ms · oscilação {report.JitterMs:0.#} ms · perda {report.LossPercent:0.#}%",
            report is null ? "MutedBrush" : report.LossPercent > 2 || report.JitterMs > 15 ? "WarnBrush" : "OkBrush",
            report is null ? "Entre numa partida e use Ver detalhes → Analisar rota." : $"Medido em {report.At:dd/MM HH:mm} · {report.Host}",
            "Ver detalhes", () => OpenDetails(1)));

        // 4) requisitos
        string req = info is { MinimumRequirements.Length: > 0 } ? Shorten(info.MinimumRequirements, 150) : "A loja não informou requisitos para este jogo.";
        bool hasReq = info is { MinimumRequirements.Length: > 0 };
        string reqLine = v.Requirements is { Verdict: not Fit.Unknown } rr ? rr.Summary : hasReq ? "Mínimos informados pela loja" : "Sem requisitos informados";
        string reqBrush = v.Requirements?.Verdict switch { Fit.Recommended => "OkBrush", Fit.Minimum => v.Requirements.Summary.Contains("por pouco") ? "WarnBrush" : "OkBrush", Fit.Below => "BadBrush", _ => hasReq ? "TextBrush" : "MutedBrush" };
        tiles.Children.Add(StatTile("\uE9F9", "Requisitos", reqLine, reqBrush, Shorten(req, 70), "Ver detalhes", () => OpenDetails(3)));
        panel.Children.Add(Muted((game.ExeName ?? "Executável não identificado") + (info is { Genres.Count: > 0 } ? " · " + string.Join(", ", info.Genres.Take(3)) : "") + " · clique num cartão para ver os detalhes aqui embaixo.", 10.5));
        return panel;
    }

    private static string Shorten(string text, int max)
    {
        string flat = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= max ? flat : flat[..max].TrimEnd() + "…";
    }

    /// <summary>Cartão pequeno da grade 2×2 do resumo: ícone, nome, valor e uma linha; clicar abre os detalhes.</summary>
    private Border StatTile(string glyph, string title, string line, string brush, string sub, string buttonText, Action open)
    {
        var st = new StackPanel();
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        var badge = MBExtreme.UI.Controls.GlassFx.Badge(glyph, System.Windows.Media.Color.FromRgb(0xFF, 0xC4, 0x00), 26);
        badge.Margin = new Thickness(0, 0, 8, 0);
        top.Children.Add(badge);
        top.Children.Add(new TextBlock { Text = title, FontSize = 11, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center });
        st.Children.Add(top);
        st.Children.Add(new TextBlock { Text = line, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Brush(brush), TextWrapping = TextWrapping.Wrap });
        st.Children.Add(new TextBlock { Text = sub, FontSize = 10.5, Foreground = Brush("MutedBrush"), TextWrapping = TextWrapping.Wrap, MaxHeight = 30, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) });
        var card = new Border
        {
            Background = Brush("Surface2Brush"), BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 9, 10, 9), Margin = new Thickness(0, 0, 8, 8), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = buttonText, Child = st,
        };
        card.MouseLeftButtonUp += (_, _) => open();
        MBExtreme.UI.Controls.GlassFx.Attach(card, badge, bar: false);
        return card;
    }

    /// <summary>Primeiro cartão do resumo: o Perfil MB do jogo (publicado no site) ou o perfil geral pelo tipo de jogo.</summary>
    private UIElement ProfileCard(GameView v)
    {
        var game = v.Game;
        var s = v.Settings;
        var title = TitleOf(game);
        bool applied = Profiled(s);
        var box = new StackPanel();
        var head = new DockPanel();
        var state = new TextBlock
        {
            Text = s.SessionActive ? "● ligado agora" : s.AutoProfile ? "● automático" : applied ? "✓ aplicado" : "não aplicado", FontSize = 11.5, FontWeight = FontWeights.SemiBold,
            Foreground = Brush(s.SessionActive || applied ? "OkBrush" : "WarnBrush"), VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(state, Dock.Right);
        head.Children.Add(state);
        head.Children.Add(new TextBlock
        {
            Text = title is null ? "PERFIL GERAL · " + ShortTitle(v.Profile.Title).ToUpperInvariant() : "PERFIL MB · " + title.Title.ToUpperInvariant(),
            FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = Brush("AccentBrush"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        });
        box.Children.Add(head);

        var does = new List<string> { "ajustes do Windows de " + ShortTitle(v.Profile.Title).ToLowerInvariant() };
        if (title?.Priority ?? true) does.Add("prioridade alta de CPU");
        does.Add(_maker == GpuMaker.Nvidia ? "driver no desempenho máximo" : "placa de vídeo de alto desempenho");
        if (title?.Fso == true) does.Add("tela cheia sem otimizações");
        if (title is { Reflex: true }) does.Add("tem NVIDIA Reflex");
        box.Children.Add(new TextBlock
        {
            Text = (title is null ? "Este jogo ainda não tem Perfil MB próprio; usa o perfil pelo tipo de jogo: " : "") + char.ToUpper(does[0][0]) + string.Join(", ", does)[1..] + ".",
            FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
        });
        if (title is not null)
        {
            var plan = GameTitles.Plan(title, _maker, _tier);
            if (plan.InGame.Count > 0)
            {
                box.Children.Add(new TextBlock { Text = "No menu do jogo · " + plan.TierName, FontSize = 11, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 8, 0, 2) });
                var lines = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
                foreach (string line in plan.InGame.Take(4))
                    lines.Children.Add(new TextBlock { Text = line, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = line, Margin = new Thickness(0, 0, 8, 2) });
                box.Children.Add(lines);
            }
        }
        var auto = new CheckBox
        {
            Content = "Ligar sozinho quando o jogo abrir e desfazer quando fechar", IsChecked = s.AutoProfile || s.OptimizedAt is null,
            Foreground = Brush("TextBrush"), FontSize = 11.5, Margin = new Thickness(0, 8, 0, 0),
        };
        if (applied)
            auto.Click += (_, _) =>
            {
                bool on = auto.IsChecked == true;
                AppServices.GameLibrary.Update(game.Id, x =>
                {
                    x.AutoProfile = on;
                    if (on && x.PlanTweaks.Count == 0) x.PlanTweaks = v.Plan.TweakIds.ToList();
                    if (on) x.SessionActive = x.AppliedTweaks.Count > 0;
                });
                SetStatus(on ? "Automático ligado: entra quando o jogo abre e sai quando fecha." : "Automático desligado: os ajustes ficam até você clicar em Desfazer.", "OkBrush");
            };
        box.Children.Add(auto);
        var buttons = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var apply = SmallButton(applied ? "Reaplicar perfil" : title is null ? "Aplicar perfil" : "Aplicar perfil MB", async (_, _) => await ApplyTitleAsync(game, v, auto.IsChecked == true), !applied);
        apply.Margin = new Thickness(0);
        buttons.Children.Add(apply);
        var see = SmallButton("Ver o que muda", (_, _) => { _tab = 2; Paint(); });
        Grid.SetColumn(see, 1);
        see.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(see);
        box.Children.Add(buttons);

        var glass = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(1, 1) };
        glass.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(title is null ? (byte)0x10 : (byte)0x2A, 0xFF, 0xC4, 0x00), 0));
        glass.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x08, 0xFF, 0x8A, 0x00), 1));
        return new Border
        {
            Background = glass, BorderBrush = title is null ? Brush("BorderBrush") : new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x80, 0xFF, 0xC4, 0x00)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 12, 14, 12), Child = box,
        };
    }

    // ---- aba Otimização: duas listas curtas (jogo e rede)

    private UIElement OptimizePanel(GameView v)
    {
        var game = v.Game;
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(TitleCard(v));

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

    // ---- perfil do jogo (por título: EA FC é diferente de Call of Duty)

    private UIElement TitleCard(GameView v)
    {
        var game = v.Game;
        var s = v.Settings;
        var title = TitleOf(game);
        var box = new StackPanel();

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var names = new StackPanel();
        names.Children.Add(new TextBlock
        {
            Text = title is null ? "Perfil do jogo: " + v.Profile.Title : "Perfil do jogo: " + title.Title,
            FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
        });
        string kind = title is null ? "Perfil geral pelo tipo de jogo (este título ainda não tem perfil próprio)."
            : v.Profile.Title + (title.Reflex ? " · tem NVIDIA Reflex" : "") + (title.AntiCheat.Length > 0 ? " · anti-cheat " + title.AntiCheat : "");
        names.Children.Add(Muted(kind, 11));
        head.Children.Add(names);
        bool applied = s.AutoProfile || s.OptimizedAt is not null;
        var state = new TextBlock
        {
            Text = s.SessionActive ? "● ligado agora" : s.AutoProfile ? "● automático" : applied ? "✓ aplicado" : "não aplicado",
            FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Top,
            Foreground = Brush(s.SessionActive || applied ? "OkBrush" : "MutedBrush"),
        };
        Grid.SetColumn(state, 1);
        head.Children.Add(state);
        box.Children.Add(head);

        // o que o perfil faz no PC
        var does = new List<string> { "Ajustes do Windows do tipo " + v.Profile.Title.Split('(')[0].Trim().ToLowerInvariant() };
        if (title?.Priority ?? true) does.Add("prioridade máxima de CPU sempre que o jogo abrir");
        does.Add("placa de vídeo de alto desempenho");
        if (_maker == GpuMaker.Nvidia) does.Add(title is { Reflex: false, Profile: "competitive" } ? "driver NVIDIA no desempenho máximo e baixa latência ultra" : "driver NVIDIA no desempenho máximo");
        if (title?.Fso == true) does.Add("otimizações de tela cheia desligadas");
        does.Add("Modo Jogo fecha e pausa o que sobra" + (title is { KeepRunning.Count: > 0 } ? " (sem tocar em " + string.Join(", ", title.KeepRunning.Take(3)) + ")" : ""));
        box.Children.Add(Muted("Aplica: " + string.Join(" · ", does) + ".", 11.5));

        if (title is not null)
        {
            var plan = GameTitles.Plan(title, _maker, _tier);
            box.Children.Add(new TextBlock { Text = "No menu do jogo · " + plan.TierName, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 2) });
            foreach (string line in plan.InGame) box.Children.Add(Muted("•  " + line, 11.5));
            if (plan.Tips.Count > 0)
            {
                box.Children.Add(new TextBlock { Text = "Dicas", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
                foreach (string line in plan.Tips) box.Children.Add(Muted("•  " + line, 11.5));
            }
            foreach (string w in plan.Warnings)
                box.Children.Add(new TextBlock { Text = "▲ " + w, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Foreground = Brush("WarnBrush"), Margin = new Thickness(0, 6, 0, 0) });
        }
        else
            foreach (string line in v.Plan.Advice.Take(3)) box.Children.Add(Muted("•  " + line, 11.5));

        var auto = new CheckBox
        {
            Content = "Automático: ligar ao abrir o jogo (pelo Jogar ou pela loja) e desfazer os ajustes do Windows quando ele fechar",
            IsChecked = s.AutoProfile || s.OptimizedAt is null, Foreground = Brush("TextBrush"), FontSize = 11.5, Margin = new Thickness(0, 10, 0, 0),
        };
        if (s.OptimizedAt is not null || s.AutoProfile)
            auto.Click += (_, _) =>
            {
                bool on = auto.IsChecked == true;
                AppServices.GameLibrary.Update(game.Id, x =>
                {
                    x.AutoProfile = on;
                    if (on && x.PlanTweaks.Count == 0) x.PlanTweaks = v.Plan.TweakIds.ToList();
                    if (on) x.SessionActive = x.AppliedTweaks.Count > 0;
                });
                SetStatus(on ? "Perfil automático ligado: entra quando o jogo abre e sai quando fecha." : "Perfil automático desligado: os ajustes ficam como estão até você clicar em Desfazer.", "OkBrush");
            };
        box.Children.Add(auto);
        if (game.ExeName is null)
            box.Children.Add(Muted("Não achei o executável deste jogo: o automático só funciona depois de abrir o jogo pelo Jogar uma vez.", 10.5));

        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(Tight(SmallButton(applied ? "Reaplicar perfil do jogo" : "Aplicar perfil do jogo", async (_, _) => await ApplyTitleAsync(game, v, auto.IsChecked == true), !applied)));
        if (applied) buttons.Children.Add(Tight(SmallButton("Desfazer perfil", async (_, _) => await UndoTitleAsync(game))));
        box.Children.Add(buttons);

        var card = CardOf(box);
        card.Padding = new Thickness(14, 12, 14, 12);
        card.Margin = new Thickness(0, 4, 0, 2);
        card.BorderBrush = Brush("AccentBrush");
        return card;
    }

    /// <summary>Aplica o perfil do jogo: otimização (Windows + driver + rede), prioridade de CPU, tela cheia e o modo automático.</summary>
    private async Task ApplyTitleAsync(InstalledGame game, GameView v, bool auto)
    {
        var title = TitleOf(game);
        await OptimizeAllAsync(game, v.Plan, v.Profile);
        var notes = new List<string>();
        if (game.ExeName is { } exe)
        {
            if ((title?.Priority ?? true) && !SafeIsPriority(exe))
            {
                try
                {
                    await Task.Run(() => AppServices.GamePriority.Apply(exe));
                    AppServices.GameLibrary.Update(game.Id, x => x.HighPriority = true);
                    notes.Add("prioridade máxima de CPU");
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or ArgumentException) { notes.Add("prioridade: execute como administrador"); }
            }
            if (title?.Fso == true && !AppServices.GameLibrary.Get(game.Id).FsoDisabled && await Task.Run(() => GameFiles.FindExePath(game)) is { } exePath)
            {
                try
                {
                    AppServices.GameLibrary.Update(game.Id, x => GameWindowService.SetFullscreenOptimizations(exePath, true, x));
                    notes.Add("tela cheia otimizada");
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
            }
            // o jogo entra na lista do Modo Jogo (fecha e pausa o resto quando ele abre)
            string process = Path.GetFileNameWithoutExtension(exe);
            var boost = AppServices.Boost.Settings;
            if (!boost.GameProcesses.Contains(process, StringComparer.OrdinalIgnoreCase)) { boost.GameProcesses.Add(process); boost.Save(); }
            AppServices.Boost.RefreshGames();
            if (!boost.BoostAuto) AppServices.Boost.SetAuto(true);
        }
        var applied = AppServices.GameLibrary.Get(game.Id);
        AppServices.GameLibrary.Update(game.Id, x =>
        {
            x.AutoProfile = auto;
            x.TitleKey = title?.Key;
            x.PlanTweaks = v.Plan.TweakIds.ToList();
            x.SessionActive = auto && x.AppliedTweaks.Count > 0;
        });
        // automático com o jogo fechado: os ajustes do Windows (que valem para o PC todo) só entram quando o jogo abrir
        bool running = game.ExeName is { } runningExe && IsRunning(runningExe);
        if (auto && !running) await Task.Run(() => AppServices.GameOptimization.EndSession(game.Id));
        SetStatus("Perfil do jogo aplicado" + (notes.Count > 0 ? " · " + string.Join(" · ", notes) : "") +
            (auto ? ". Automático: os ajustes do Windows entram quando o jogo abre e saem quando ele fecha; prioridade, driver e placa de vídeo ficam só para este jogo." : "."), "OkBrush");
        RefreshCard(game);
        await RenderDetailAsync(game);
    }

    private static bool IsRunning(string exe)
    {
        var procs = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe));
        foreach (var p in procs) p.Dispose();
        return procs.Length > 0;
    }

    private static bool SafeIsPriority(string exe)
    {
        try { return AppServices.GamePriority.IsApplied(exe); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { return false; }
    }

    /// <summary>Desfaz tudo o que o perfil do jogo mudou (Windows, driver, rede, prioridade e tela cheia) e desliga o automático.</summary>
    private async Task UndoTitleAsync(InstalledGame game)
    {
        var s = AppServices.GameLibrary.Get(game.Id);
        if (s.HighPriority && game.ExeName is { } exe)
        {
            try
            {
                await Task.Run(() => AppServices.GamePriority.Revert(exe));
                AppServices.GameLibrary.Update(game.Id, x => x.HighPriority = false);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException) { }
        }
        if (s.FsoDisabled && await Task.Run(() => GameFiles.FindExePath(game)) is { } exePath)
        {
            try { AppServices.GameLibrary.Update(game.Id, x => GameWindowService.SetFullscreenOptimizations(exePath, false, x)); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }
        AppServices.GameLibrary.Update(game.Id, x => { x.AutoProfile = false; x.SessionActive = false; x.TitleKey = null; });
        await UndoAsync(game);
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
        var catalog = DriverItems(game);
        var values = new Dictionary<uint, DriverValue?>();
        if (driver && game.ExeName is { } exeCat)
            foreach (var item in catalog) values[item.Id] = AppServices.NvidiaDriver.Read(exeCat, item.Id);
        return GameCompare.Build(profile, _maker, new GameSnapshot(states, titles, high, gpu, driver, power, pre, dlss, values), catalog, HasReflex(game));
    }

    /// <summary>Perfil próprio do título (EA FC, Call of Duty...), se o jogo estiver no banco.</summary>
    private static GameTitle? TitleOf(InstalledGame game) => GameTitles.Find(game.Name, game.ExeName);

    private static bool HasReflex(InstalledGame game) => TitleOf(game)?.Reflex ?? DriverCatalog.For(game.ExeName)?.HasReflex ?? false;

    /// <summary>Ajustes de driver NVIDIA do jogo: catálogo por executável + extras do perfil do título (baixa latência sem Reflex...).</summary>
    private IReadOnlyList<DriverSetting> DriverItems(InstalledGame game)
    {
        var list = DriverCatalog.SettingsFor(game.ExeName, _advanced).ToList();
        if (TitleOf(game) is { } title)
            foreach (var extra in GameTitles.Plan(title, _maker, _tier).DriverExtras)
                if ((_advanced || !extra.Advanced) && list.All(x => x.Id != extra.Id)) list.Add(extra);
        return list;
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
        var driverPlan = DriverPlan.For(profile, _maker, HasReflex(game));
        var catalogItems = _maker == GpuMaker.Nvidia ? DriverItems(game) : Array.Empty<DriverSetting>();
        var pending = (await Task.Run(() => ReadRows(game, profile))).Where(r => r.CanFix && r.Group is "Driver NVIDIA" or "Placa de vídeo").ToList();
        bool withDriver = false;
        if (_driverAnswer is { } answer) withDriver = answer && pending.Count > 0 && (!driverPlan.IsEmpty || catalogItems.Count > 0);
        else if (pending.Count > 0 && (!driverPlan.IsEmpty || catalogItems.Count > 0))
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

            // primeira vez pelo Jogar: liga o perfil automático (ajustes do Windows entram com o jogo e saem quando ele fecha)
            var st = AppServices.GameLibrary.Get(game.Id);
            if (!st.AutoProfile && st.OptimizedAt is null && st.AppliedTweaks.Count == 0)
            {
                var title = TitleOf(game);
                var profile = st.ProfileKey is { } key ? GameProfiles.Find(key)
                    : title is not null ? GameProfiles.Find(title.Profile) : GameProfiles.Classify(exe, null, null);
                var tweaks = GameOptimizer.Plan(profile, _maker, _tier, onHdd: false).TweakIds.ToList();
                AppServices.GameLibrary.Update(game.Id, x => { x.AutoProfile = true; x.PlanTweaks = tweaks; x.TitleKey = title?.Key; x.ProfileKey ??= profile.Key; });
            }
        }

        if (measure)
        {
            string? problem = AppServices.GameMeasure.Start(game, AppServices.GameLibrary.Get(game.Id).OptimizedAt is not null);
            if (problem is not null) { SetStatus(problem, "BadBrush"); return; }
        }
        else if (game.ExeName is not null && AppServices.Settings.AutoMeasureGames && !AppServices.GameMeasure.IsMeasuring)
            AppServices.GameMeasure.Start(game, AppServices.GameLibrary.Get(game.Id).OptimizedAt is not null);   // relatório da aba Desempenho, sem clicar em nada
        if (AppServices.Settings.OverlayGames.Contains(game.Id)) AppServices.Overlay.ShowOverlay();

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

    // ---- aba Desempenho: FPS, 1% e 0,1% pior, travadas, gráfico, gargalo e sessões (antes era "Gargalos e FPS")

    private UIElement PerformancePanel(GameView v)
    {
        var game = v.Game;
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var sessions = AppServices.Perf.Get(game.Id);
        var last = sessions.LastOrDefault();

        // contador na tela do jogo, por jogo
        bool overlay = AppServices.Settings.OverlayGames.Contains(game.Id);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var info = new StackPanel();
        info.Children.Add(new TextBlock { Text = "Contador na tela do jogo", FontSize = 12.5, FontWeight = FontWeights.SemiBold });
        info.Children.Add(Muted("FPS, 1% pior, gráfico e sensores por cima do jogo · Ctrl + Shift + F10 mostra e esconde", 10.5));
        row.Children.Add(info);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var toggle = new CheckBox { IsChecked = overlay, Content = overlay ? "Ligado" : "Desligado", Foreground = Brush("TextBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        toggle.Click += (_, _) =>
        {
            var list = AppServices.Settings.OverlayGames;
            if (toggle.IsChecked == true) { if (!list.Contains(game.Id)) list.Add(game.Id); } else list.Remove(game.Id);
            AppServices.Settings.Save();
            toggle.Content = toggle.IsChecked == true ? "Ligado" : "Desligado";
        };
        buttons.Children.Add(toggle);
        buttons.Children.Add(Tight(SmallButton("Ajustar", (_, _) => MainWindow.Current?.Go("overlay-setup"))));
        Grid.SetColumn(buttons, 1);
        row.Children.Add(buttons);
        var ovCard = CardOf(row);
        ovCard.Padding = new Thickness(12, 8, 8, 8);
        panel.Children.Add(ovCard);

        if (last is null)
        {
            panel.Children.Add(Muted("Ainda não há sessão medida. Jogue pelo botão Jogar (o MB mede sozinho e guarda ao fechar o jogo) ou use Medir FPS.", 11.5));
            return panel;
        }

        // números da última sessão, com a diferença para a primeira sessão sem otimização
        var before = sessions.FirstOrDefault(x => !x.Optimized && x != last);
        string Diff(double now, double? then) => then is { } t && t > 0 ? $"{now - t:+0;-0;0} vs antes" : "";
        var tiles = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(0, 8, 0, 0) };
        tiles.Children.Add(Stat("FPS médio", $"{last.AvgFps:0}", Diff(last.AvgFps, before?.AvgFps), "TextBrush"));
        tiles.Children.Add(Stat("1% pior", $"{last.Low1Fps:0}", Diff(last.Low1Fps, before?.Low1Fps), "TextBrush"));
        tiles.Children.Add(Stat("0,1% pior", last.Low01Fps > 0 ? $"{last.Low01Fps:0}" : "—", before is { Low01Fps: > 0 } && last.Low01Fps > 0 ? Diff(last.Low01Fps, before.Low01Fps) : "", "TextBrush"));
        tiles.Children.Add(Stat("Travadas", $"{last.Stutters}", "quadros lentos", last.Stutters > 10 ? "WarnBrush" : "TextBrush"));
        panel.Children.Add(tiles);

        if (last.FrameMs.Count > 10)
        {
            var chart = new StackPanel();
            var head = new DockPanel();
            var avg = new TextBlock { Text = $"média {last.AvgFrameMs:0.0} ms", FontSize = 10.5, Foreground = Brush("MutedBrush") };
            DockPanel.SetDock(avg, Dock.Right);
            head.Children.Add(avg);
            head.Children.Add(new TextBlock { Text = "Tempo de quadro da última sessão (picos = travadas)", FontSize = 10.5, Foreground = Brush("MutedBrush") });
            chart.Children.Add(head);
            chart.Children.Add(FrameChart(last.FrameMs));
            var c = CardOf(chart);
            c.Padding = new Thickness(10, 8, 10, 8);
            c.Margin = new Thickness(0, 4, 0, 6);
            panel.Children.Add(c);
        }

        if (last.Bottleneck.Length > 0)
        {
            var g = new StackPanel();
            g.Children.Add(new TextBlock { Text = "Gargalo: " + last.Bottleneck + (last.CpuAvg is { } cpu && last.GpuAvg is { } gpu ? $" · CPU {cpu:0}% · GPU {gpu:0}%" : ""), FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (last.Advice.Length > 0) g.Children.Add(Muted(last.Advice, 11.5));
            var gc = CardOf(g);
            gc.BorderBrush = Brush("AccentBrush");
            gc.Padding = new Thickness(12, 9, 12, 9);
            panel.Children.Add(gc);
        }

        // sessões
        var table = new Grid { Margin = new Thickness(0, 8, 0, 4) };
        foreach (double w in new[] { 1.3, 1.2, 0.6, 0.6, 1.6 }) table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });
        int r = 0;
        void Cell(string text, int col, string brush = "TextBrush", bool bold = false)
        {
            var t = new TextBlock { Text = text, FontSize = 11, Margin = new Thickness(0, 2, 6, 2), Foreground = Brush(brush), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetRow(t, r); Grid.SetColumn(t, col); table.Children.Add(t);
        }
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Cell("Sessão", 0, "MutedBrush", true); Cell("Duração", 1, "MutedBrush", true); Cell("FPS", 2, "MutedBrush", true); Cell("1%", 3, "MutedBrush", true); Cell("Gargalo", 4, "MutedBrush", true);
        foreach (var x in sessions.AsEnumerable().Reverse().Take(8))
        {
            r++;
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Cell($"{x.StartedAt:dd/MM HH:mm}", 0); Cell($"{x.Seconds / 60} min · {(x.Optimized ? "otimizado" : "antes")}", 1); Cell($"{x.AvgFps:0}", 2, bold: true); Cell($"{x.Low1Fps:0}", 3);
            Cell(x.Bottleneck.Length > 0 ? x.Bottleneck : "—", 4, x.Bottleneck.Contains("processador") || x.Bottleneck.Contains("térmico") ? "WarnBrush" : "MutedBrush");
        }
        panel.Children.Add(table);
        var actions = new WrapPanel();
        actions.Children.Add(Tight(SmallButton("Salvar relatório (imagem)", (_, _) => SaveImage(panel, game))));
        actions.Children.Add(Tight(SmallButton("Medir FPS agora", (_, _) => Play(game, true))));
        panel.Children.Add(actions);
        panel.Children.Add(BuildPerformance(game));
        return panel;
    }

    private Border Stat(string title, string value, string sub, string brush)
    {
        var st = new StackPanel();
        st.Children.Add(new TextBlock { Text = title, FontSize = 10.5, Foreground = Brush("MutedBrush") });
        st.Children.Add(new TextBlock { Text = value, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brush(brush) });
        if (sub.Length > 0) st.Children.Add(new TextBlock { Text = sub, FontSize = 10, Foreground = sub.StartsWith('+') ? Brush("OkBrush") : sub.StartsWith('-') ? Brush("WarnBrush") : Brush("MutedBrush") });
        return new Border { Background = Brush("Surface2Brush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 7, 9, 7), Margin = new Thickness(0, 0, 6, 6), Child = st };
    }

    private static FrameworkElement FrameChart(IReadOnlyList<double> ms)
    {
        double max = Math.Max(20, ms.Max() * 1.1);
        var line = new System.Windows.Shapes.Polyline { Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xC4, 0x00)), StrokeThickness = 1.4, Stretch = Stretch.None };
        const double width = 400, height = 70;
        for (int i = 0; i < ms.Count; i++) line.Points.Add(new System.Windows.Point(i * width / Math.Max(1, ms.Count - 1), height - ms[i] / max * height));
        var canvas = new Canvas { Width = width, Height = height, ClipToBounds = true };
        canvas.Children.Add(line);
        return new Viewbox { Stretch = Stretch.Fill, Height = 70, Child = canvas, Margin = new Thickness(0, 4, 0, 0) };
    }

    private void SaveImage(FrameworkElement panel, InstalledGame game)
    {
        try
        {
            int w = (int)Math.Max(1, panel.ActualWidth), h = (int)Math.Max(1, panel.ActualHeight);
            var bmp = new RenderTargetBitmap(w * 2, h * 2, 192, 192, PixelFormats.Pbgra32);
            var bg = new DrawingVisual();
            using (var dc = bg.RenderOpen()) dc.DrawRectangle(Brush("BgBrush"), null, new System.Windows.Rect(0, 0, w, h));
            bmp.Render(bg);
            bmp.Render(panel);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            string safe = string.Concat(game.Name.Split(Path.GetInvalidFileNameChars()));
            string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), $"MB Extreme - {safe} - {DateTime.Now:yyyy-MM-dd HHmm}.png");
            using (var fs = File.Create(file)) enc.Save(fs);
            SetStatus("Relatório salvo em Documentos: " + Path.GetFileName(file), "OkBrush");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { SetStatus("Não deu para salvar a imagem: " + ex.Message, "WarnBrush"); }
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
