using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MBExtreme.Core.Network;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>Aba Jogos e servidores: mapa com setas até cada servidor e a lista dos mais perto de você.</summary>
public sealed partial class InternetTestPage
{
    private const string Near = "Perto de mim";
    private const string AllProviders = "Todos";
    private readonly NetMapControl _gameMap = new() { Height = 440 };
    private readonly List<PingTarget> _catalog = GameServers.Static.ToList();
    private readonly HashSet<string> _picked = new();
    private readonly Dictionary<string, PingResult> _results = new();
    private readonly HashSet<string> _testing = new();
    private readonly StackPanel _gameRows = new();
    private readonly TextBlock _explain = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _catalogNote = new() { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private string _region = Near;
    private string _provider = AllProviders;
    private bool _catalogLoading, _catalogLoaded;
    private bool _gamesRunning;

    private UIElement GamesTab()
    {
        if (!_catalogLoaded && !_catalogLoading) _ = LoadCatalogAsync();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(440) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        // mapa
        var mapPanel = new StackPanel();
        var mapHead = new Grid();
        mapHead.ColumnDefinitions.Add(new ColumnDefinition());
        mapHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mapHead.Children.Add(Title("Mapa da sua conexão", "Setas do seu provedor até cada servidor. A cor é o ping."));
        var scope = Segmented(new[] { "Brasil", "Mundo" }, _gameMap.Scope == MapScope.World ? 1 : 0, i => _gameMap.Scope = i == 1 ? MapScope.World : MapScope.Brazil);
        scope.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(scope, 1);
        mapHead.Children.Add(scope);
        mapPanel.Children.Add(mapHead);
        mapPanel.Children.Add(Free(_gameMap));
        mapPanel.Children.Add(Legend());
        grid.Children.Add(CardOf(mapPanel));

        // lista
        var list = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Title("Servidores perto de você", "Escolhidos pela sua localização. Marque quantos quiser e teste."));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        actions.Children.Add(Small("Testar marcados", true, async () => await PingGamesAsync(_catalog.Where(t => _picked.Contains(t.Key)).ToList())));
        actions.Children.Add(Small("Testar os 10 mais perto", false, async () =>
        {
            var nearest = NearestTargets(10);
            foreach (var t in nearest) _picked.Add(t.Key);
            await PingGamesAsync(nearest);
        }));
        Grid.SetColumn(actions, 1);
        head.Children.Add(actions);
        list.Children.Add(head);

        var filters = new Grid { Margin = new Thickness(0, 2, 0, 6) };
        filters.ColumnDefinitions.Add(new ColumnDefinition());
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        filters.Children.Add(Chips(new[] { Near }.Concat(GameServers.Regions).ToList(), _region, r =>
        {
            _region = r;
            if (r != Near && r != GameServers.Brazil && _gameMap.Scope == MapScope.Brazil) { _gameMap.Scope = MapScope.World; SelectTab("games"); return; }
            FillGameRows();
        }, r => r == Near ? r : $"{r} ({_catalog.Count(t => t.Group == r)})"));
        var providers = Chips(new[] { AllProviders }.Concat(GameServers.Providers).ToList(), _provider, p => { _provider = p; FillGameRows(); });
        Grid.SetColumn(providers, 1);
        filters.Children.Add(providers);
        list.Children.Add(filters);
        list.Children.Add(HeaderRow());
        list.Children.Add(Free(_gameRows));
        _explain.Foreground = Brush("MutedBrush");
        list.Children.Add(Free(_explain));
        _catalogNote.Foreground = Brush("MutedBrush");
        list.Children.Add(Free(_catalogNote));
        var listCard = CardOf(list);
        listCard.Margin = new Thickness(14, 0, 0, 0);
        Grid.SetColumn(listCard, 1);
        grid.Children.Add(listCard);

        FillGameRows();
        UpdateCatalogNote();
        return grid;
    }

    private Button Small(string text, bool primary, Func<Task> click)
    {
        var b = new Button { Content = text, Style = (Style)FindResource(primary ? "PrimaryButton" : "GhostButton"), Padding = new Thickness(12, 5, 12, 5), FontSize = 12.5, Margin = new Thickness(8, 0, 0, 0) };
        b.Click += async (_, _) => await click();
        return b;
    }

    private WrapPanel Chips(IReadOnlyList<string> options, string selected, Action<string> onPick, Func<string, string>? label = null)
    {
        var panel = new WrapPanel();
        var chips = new List<ToggleButton>();
        foreach (var option in options)
        {
            string o = option;
            var chip = new ToggleButton { Style = (Style)FindResource("ChipToggle"), Content = label?.Invoke(o) ?? o, IsChecked = o == selected, Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(10, 2, 10, 2), FontSize = 11.5 };
            chip.Click += (_, _) =>
            {
                foreach (var c in chips) c.IsChecked = ReferenceEquals(c, chip);
                onPick(o);
            };
            chips.Add(chip);
            panel.Children.Add(chip);
        }
        return panel;
    }

    private UIElement Legend()
    {
        var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (level, text) in new[] { (0, "até 30 ms"), (1, "até 60"), (2, "até 100"), (3, "mais ou sem resposta") })
        {
            row.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = LevelBrush(level), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = text, FontSize = 11, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 0, 12, 0) });
        }
        return row;
    }

    private static readonly GridLength[] Columns =
    {
        new(26), new(1, GridUnitType.Star), new(170), new(70), new(58), new(78),
    };

    private Grid NewRowGrid()
    {
        var g = new Grid();
        foreach (var w in Columns) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        return g;
    }

    private UIElement HeaderRow()
    {
        var g = NewRowGrid();
        g.Margin = new Thickness(0, 0, 0, 2);
        string[] titles = { "", "Servidor", "Ping", "Variação", "Perda", "Distância" };
        for (int i = 0; i < titles.Length; i++)
        {
            var t = new TextBlock { Text = titles[i], FontSize = 11, Foreground = Brush("MutedBrush") };
            Grid.SetColumn(t, i);
            g.Children.Add(t);
        }
        return g;
    }

    /// <summary>Os mais perto de você (as referências entram sempre). Sem localização: os do Brasil.</summary>
    private IReadOnlyList<PingTarget> NearestTargets(int count) => double.IsNaN(_userLat)
        ? _catalog.Where(t => t.Group == GameServers.Brazil).Take(count).ToList()
        : GameServers.Nearest(_catalog, _userLat, _userLon, count);

    private void PickNearestIfEmpty()
    {
        if (_picked.Count > 0) return;
        foreach (var t in NearestTargets(6)) _picked.Add(t.Key);
    }

    private IEnumerable<PingTarget> Visible()
    {
        var source = _region == Near ? NearestTargets(12) : _catalog.Where(t => t.Group == _region).ToList();
        if (_provider != AllProviders) source = source.Where(t => t.Provider == _provider).ToList();
        // os marcados e já medidos ficam em cima, do menor ping para o maior
        return source.OrderBy(t => _results.TryGetValue(t.Key, out var r) && r.Median is double m ? m : double.MaxValue)
            .ThenBy(t => double.IsNaN(_userLat) || double.IsNaN(t.Lat) ? 0 : GameServers.DistanceKm(_userLat, _userLon, t.Lat, t.Lon));
    }

    private void RefreshGames()
    {
        if (_tab == "games") FillGameRows();
    }

    private void FillGameRows()
    {
        _gameRows.Children.Clear();
        var rows = Visible().ToList();
        if (rows.Count == 0)
        {
            _gameRows.Children.Add(Muted(_catalogLoading ? "Carregando servidores..." : "Nenhum servidor nesse filtro.", 12));
            return;
        }
        double? nearestKm = null;
        foreach (var t in rows)
        {
            double? km = double.IsNaN(_userLat) || double.IsNaN(t.Lat) ? null : GameServers.DistanceKm(_userLat, _userLon, t.Lat, t.Lon);
            nearestKm = km is double k && (nearestKm is null || k < nearestKm) ? k : nearestKm;
        }
        foreach (var t in rows) _gameRows.Children.Add(GameRow(t, nearestKm));
    }

    private UIElement GameRow(PingTarget t, double? nearestKm)
    {
        var g = NewRowGrid();
        var target = t;
        var box = new CheckBox { IsChecked = _picked.Contains(t.Key), VerticalAlignment = VerticalAlignment.Center };
        box.Click += (_, _) => { if (box.IsChecked == true) _picked.Add(target.Key); else _picked.Remove(target.Key); };
        g.Children.Add(box);

        double? km = double.IsNaN(_userLat) || double.IsNaN(t.Lat) ? null : GameServers.DistanceKm(_userLat, _userLon, t.Lat, t.Lon);
        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new TextBlock { Text = t.Name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
        if (t.Provider.Length > 0) line.Children.Add(Pill(t.Provider, "#26262C", "#C9C9D1"));
        if (km is double d && nearestKm is double n && Math.Abs(d - n) < 1) line.Children.Add(Pill("mais perto", "#16301F", "#3DDC97"));
        name.Children.Add(line);
        if (t.Note.Length > 0) name.Children.Add(new TextBlock { Text = t.Note, FontSize = 10.5, Foreground = Brush("MutedBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(name, 1);
        g.Children.Add(name);

        _results.TryGetValue(t.Key, out var r);
        bool testing = _testing.Contains(t.Key);
        var ping = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var barBack = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Brush("Surface2Brush") };
        var bar = new Border { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        var barHost = new Grid();
        barHost.Children.Add(barBack);
        barHost.Children.Add(bar);
        ping.Children.Add(barHost);
        string pingText = testing ? "medindo..." : r is null ? "—" : r.Median is double m ? $"{m:0} ms" : "sem resposta";
        var pingValue = new TextBlock { Text = pingText, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 0) };
        if (r is not null && !testing)
        {
            int level = GameServers.Level(r.Median);
            pingValue.Foreground = LevelBrush(level);
            bar.Background = LevelBrush(level);
            double fraction = r.Median is double mm ? Math.Clamp(mm / 150.0, 0.03, 1) : 1;
            barHost.SizeChanged += (_, e) => bar.Width = e.NewSize.Width * fraction;
        }
        else pingValue.Foreground = Brush("MutedBrush");
        ping.Children.Add(pingValue);
        Grid.SetColumn(ping, 2);
        g.Children.Add(ping);

        AddCell(g, 3, r?.Jitter is double j ? $"{j:0} ms" : "—");
        AddCell(g, 4, r is null ? "—" : $"{(r.Sent == 0 ? 0 : 100.0 * r.Lost / r.Sent):0}%");
        AddCell(g, 5, km is double dist ? (dist < 1 ? "—" : $"{dist:N0} km") : "perto", "MutedBrush");

        return new Border { Child = g, Padding = new Thickness(2, 6, 2, 6), BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Brush("Surface2Brush") };
    }

    private void AddCell(Grid g, int column, string text, string brush = "TextBrush")
    {
        var t = new TextBlock { Text = text, FontSize = 12, Foreground = Brush(brush), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(t, column);
        g.Children.Add(t);
    }

    private static Border Pill(string text, string back, string fore) => new()
    {
        Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 0, 6, 1), CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Center,
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(back)),
        Child = new TextBlock { Text = text, FontSize = 10, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fore)) },
    };

    /// <summary>Busca os relays da Valve e os servidores do LibreSpeed (listas públicas que mudam com o tempo).</summary>
    private async Task LoadCatalogAsync()
    {
        _catalogLoading = true;
        UpdateCatalogNote();
        var valveTask = GameServers.ValveAsync(Http, CancellationToken.None);
        var libreTask = _libreAll is not null ? Task.FromResult(_libreAll) : LibreSpeed.ListAsync(Http, CancellationToken.None);
        var valve = await valveTask;
        var libre = await libreTask;
        _libreAll ??= libre;
        int before = _catalog.Count;
        foreach (var t in valve.Concat(GameServers.FromLibreSpeed(libre)))
            if (!_catalog.Any(c => c.Key == t.Key)) _catalog.Add(t);
        _catalogLoading = false;
        _catalogLoaded = true;
        _valveCount = valve.Count;
        _libreCount = _catalog.Count(t => t.Provider == "LibreSpeed");
        if (_catalog.Count > before) { _picked.Clear(); PickNearestIfEmpty(); }
        UpdateCatalogNote();
        RefreshGames();
    }

    private int _valveCount, _libreCount;

    private void UpdateCatalogNote()
    {
        _catalogNote.Text = _catalogLoading
            ? "Buscando os servidores da Valve (Steam) e do LibreSpeed..."
            : $"{_catalog.Count} servidores: regiões da AWS e da Oracle, {_valveCount} cidades da Valve (CS2, Dota 2) e {_libreCount} servidores do LibreSpeed com cidade conhecida. " +
              "A sua posição vem do IP do provedor (cidade aproximada). Os da Valve usam o ping comum; os outros, uma conexão segura (porta 443).";
    }

    private async Task PingGamesAsync(IReadOnlyList<PingTarget> targets)
    {
        if (targets.Count == 0) { Say("Marque pelo menos um servidor.", "WarnBrush"); return; }
        if (_gamesRunning) return;
        _gamesRunning = true;
        Say($"Medindo {targets.Count} servidor(es), 8 de cada vez...", "MutedBrush");
        foreach (var t in targets) { _testing.Add(t.Key); _results.Remove(t.Key); }
        _gameMap.ClearLinks();
        foreach (var t in targets.Where(t => !double.IsNaN(t.Lat)))
            _gameMap.SetLink(new GeoPoint(t.Key, t.Lat, t.Lon), LinkMode.Ping, NetMapControl.Ping, t.Name);
        RefreshGames();

        using var gate = new SemaphoreSlim(8);
        var jobs = targets.Select(async t =>
        {
            await gate.WaitAsync();
            try
            {
                var r = await NetMap.PingAsync(t, 6, CancellationToken.None);
                _results[t.Key] = r;
                _testing.Remove(t.Key);
                if (!double.IsNaN(t.Lat))
                {
                    var color = LevelColors[GameServers.Level(r.Median)];
                    _gameMap.SetLink(new GeoPoint(t.Key, t.Lat, t.Lon), LinkMode.Idle, color, $"{ShortName(t)} · {(r.Median is double m ? $"{m:0} ms" : "sem resposta")}");
                }
                RefreshGames();
            }
            finally { gate.Release(); }
        }).ToList();
        await Task.WhenAll(jobs);
        _gamesRunning = false;

        // a frase sobre o servidor de jogo mais importante medido (São Paulo, se estiver na lista)
        var measured = targets.Select(t => (t, r: _results.GetValueOrDefault(t.Key))).Where(x => x.r?.Median is not null && !double.IsNaN(x.t.Lat)).ToList();
        var main = measured.FirstOrDefault(x => x.t.Key == "aws-sa-east-1");
        if (main.t is null) main = measured.OrderBy(x => x.r!.Median).FirstOrDefault();
        if (main.t is not null && !double.IsNaN(_userLat))
        {
            double km = GameServers.DistanceKm(_userLat, _userLon, main.t.Lat, main.t.Lon);
            _explain.Text = GameServers.Explain(ShortName(main.t), main.r!.Median!.Value, km) + "  Veja a rota completa na aba Roteador e rota.";
            _routeTarget = main.t;
        }
        Say($"Ping de {targets.Count} servidor(es) medido.", "OkBrush");
    }

    private static string ShortName(PingTarget t) => t.Provider is "AWS" or "Oracle" or "Valve" ? $"{t.Name} · {t.Provider}" : t.Name;
}
