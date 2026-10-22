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

namespace MBExtreme.UI.Pages;

/// <summary>
/// Biblioteca de jogos em grade de capas (Steam, Epic, GOG e os que você adicionar). Ao escolher um jogo: abrir com o Modo Jogo,
/// otimizar por tipo de jogo e marca da placa de vídeo, prioridade de CPU e requisitos informados pela loja.
/// </summary>
public sealed class GameLibraryPage : ToolPageBase
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

    public GameLibraryPage() : base("Biblioteca de jogos",
        "Seus jogos instalados. Escolha um para abrir com o Modo Jogo, otimizar o PC para o tipo de jogo e a sua placa de vídeo, e ver os requisitos informados pela loja.")
    {
        AddButton("Procurar jogos", async (_, _) => await ScanAsync());
        AddButton("Adicionar jogo ou programa...", (_, _) => AddManual(), false);

        var tier = CardOf(_tierText);
        tier.Padding = new Thickness(12, 6, 12, 6);
        Body.Children.Add(tier);
        Body.Children.Add(_detail);
        Body.Children.Add(_grid);
        Body.Children.Add(Muted("Steam, Epic e GOG são lidos só para listar os jogos; nenhum arquivo deles é alterado. Os ajustes de otimização usam o catálogo do MB Extreme (com backup e desfazer).", 11));
        Loaded += async (_, _) =>
        {
            await LoadHardwareAsync();
            await ScanAsync();
        };
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
        }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException)
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
            _steamPath = await Task.Run(GameScanner.SteamPath);
            _games = await Task.Run(() => GameScanner.Scan(AppServices.GameLibrary));
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
        if (_selected is null) _detail.Children.Clear();
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
                Background = Brush("AccentBrush"), CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(6),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = "⚡", FontSize = 11, Foreground = System.Windows.Media.Brushes.Black }, ToolTip = "Otimizado pelo MB Extreme",
            });

        var card = new Border
        {
            Width = 138, Height = 207, Margin = new Thickness(0, 0, 10, 10), CornerRadius = new CornerRadius(10), Cursor = System.Windows.Input.Cursors.Hand,
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
            if (game.SteamAppId is { } appId)
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

        _detail.Children.Clear();
        var panel = new StackPanel();

        string played = settings.MinutesPlayed < 1 ? "sem registro de tempo" : $"{settings.MinutesPlayed / 60:0.#} h jogadas" + (settings.LastPlayed is { } last ? $" · último: {last:dd/MM}" : "");
        panel.Children.Add(new TextBlock { Text = game.Name, FontSize = 20, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(Muted($"{game.LauncherName} · {played}" + (game.ExeName is null ? " · executável não identificado" : $" · {game.ExeName}") + (info is { Genres.Count: > 0 } ? " · " + string.Join(", ", info.Genres.Take(4)) : ""), 11));
        if (info is { ShortDescription.Length: > 0 }) panel.Children.Add(Muted(info.ShortDescription, 11));

        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
        var play = SmallButton("Jogar", (_, _) => Play(game), true);
        play.Margin = new Thickness(0, 0, 8, 6);
        buttons.Children.Add(play);
        bool optimized = settings.OptimizedAt is not null;
        buttons.Children.Add(Tight(SmallButton(optimized ? "Reaplicar otimização" : "Otimizar para este jogo", async (_, _) => await OptimizeAsync(game, plan), !optimized)));
        if (optimized) buttons.Children.Add(Tight(SmallButton("Desfazer otimização", async (_, _) => await UndoAsync(game))));
        if (game.ExeName is not null)
        {
            bool applied = false;
            try { applied = AppServices.GamePriority.IsApplied(game.ExeName); } catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { }
            buttons.Children.Add(Tight(SmallButton(applied ? "Prioridade alta: ligada" : "Prioridade alta de CPU", (_, _) => TogglePriority(game, applied), applied)));
        }
        if (game.Launcher == GameLauncher.Manual)
            buttons.Children.Add(Tight(SmallButton("Remover da lista", (_, _) => { AppServices.GameLibrary.RemoveManual(game.LaunchTarget ?? ""); _selected = null; _ = ScanAsync(); })));
        panel.Children.Add(buttons);

        panel.Children.Add(Muted("Tipo de jogo (define o pacote de otimização):", 11));
        var profiles = GameProfiles.All.ToList();
        panel.Children.Add(Chips(profiles.Select(p => p.Title.Split('(')[0].Trim()).ToList(), Math.Max(0, profiles.FindIndex(p => p.Key == profile.Key)), i =>
        {
            AppServices.GameLibrary.Update(game.Id, s => { s.ProfileKey = profiles[i].Key; if (game.ExeName is not null) s.ExeName = game.ExeName; });
            _ = RenderDetailAsync(game);
        }));
        panel.Children.Add(Muted(profile.Description, 11));

        var tweakNames = plan.TweakIds.Select(id => AppServices.Tweaks.FirstOrDefault(t => t.Id == id)).Where(t => t is { Risk: MBExtreme.Core.Tweaks.TweakRisk.Low } t2 && t2.IsAvailable).Select(t => "• " + t!.Title).ToList();
        if (tweakNames.Count > 0) panel.Children.Add(new Expander { Header = $"O que será ajustado no Windows ({tweakNames.Count})", Content = Muted(string.Join("\n", tweakNames), 11), Foreground = Brush("TextBrush"), Margin = new Thickness(0, 4, 0, 0) });
        panel.Children.Add(new Expander { Header = "Dicas de configuração do jogo para o seu hardware", Content = Muted(string.Join("\n\n", plan.Advice), 11), Foreground = Brush("TextBrush"), IsExpanded = true });
        if (info is { MinimumRequirements.Length: > 0 } or { RecommendedRequirements.Length: > 0 })
        {
            var req = new StackPanel();
            if (info!.MinimumRequirements.Length > 0) req.Children.Add(Muted(info.MinimumRequirements, 11));
            if (info.RecommendedRequirements.Length > 0) req.Children.Add(Muted("\n" + info.RecommendedRequirements, 11));
            panel.Children.Add(new Expander { Header = "Requisitos informados pela loja (Steam)", Content = req, Foreground = Brush("TextBrush") });
        }

        _detail.Children.Add(CardOf(panel));
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

    private async Task OptimizeAsync(InstalledGame game, OptimizationPlan plan)
    {
        SetStatus("Aplicando a otimização...");
        var result = await Task.Run(() => AppServices.GameOptimization.Apply(game.Id, game.ExeName, plan));
        var parts = new List<string>();
        if (result.Applied.Count > 0) parts.Add($"{result.Applied.Count} ajuste(s) ligado(s)");
        if (result.AlreadyOn.Count > 0) parts.Add($"{result.AlreadyOn.Count} já estava(m) ligado(s)");
        if (result.Failed.Count > 0) parts.Add($"{result.Failed.Count} falhou (execute como administrador)");
        SetStatus((parts.Count == 0 ? "Nada a ajustar." : string.Join(" · ", parts) + ".") + " Alguns ajustes valem para o PC todo; \"Desfazer otimização\" volta ao que era.", result.Failed.Count > 0 ? "WarnBrush" : "OkBrush");
        RefreshCard(game);
        await RenderDetailAsync(game);
    }

    private async Task UndoAsync(InstalledGame game)
    {
        var result = await Task.Run(() => AppServices.GameOptimization.Undo(game.Id));
        SetStatus($"{result.Applied.Count} ajuste(s) desfeito(s)" + (result.AlreadyOn.Count > 0 ? $"; {result.AlreadyOn.Count} mantido(s) porque outro jogo ainda usa" : "") + ".", "OkBrush");
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

    private void Play(InstalledGame game)
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

        try
        {
            Process.Start(new ProcessStartInfo(game.LaunchTarget) { UseShellExecute = true, WorkingDirectory = game.Launcher == GameLauncher.Manual ? game.InstallPath : "" });
            SetStatus(game.ExeName is null
                ? $"Abrindo {game.Name}..."
                : $"Abrindo {game.Name}. O Modo Jogo liga sozinho quando o jogo aparecer e desliga quando você fechar.", "OkBrush");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            SetStatus("Não foi possível abrir: " + ex.Message, "BadBrush");
        }
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
