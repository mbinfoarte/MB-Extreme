using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MBExtreme.Core.Recording;
using MBExtreme.Core.Settings;

namespace MBExtreme.UI.Pages;

/// <summary>Cenas e Fontes: cada cena guarda as suas fontes (tela, câmeras, Pix, abertura, faixa, logo). Com o modo estúdio, a cena escolhida só entra no ar com "Transição".</summary>
public sealed partial class StudioPage
{
    private readonly StackPanel _sceneList = new();
    private readonly StackPanel _sourceList = new();
    private readonly TextBlock _sourcesTitle = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _transition = new() { Content = "Transição ›" };
    private readonly Button _studioMode = new() { Content = "Modo estúdio" };
    private StudioSceneSetting? _airScene;

    private StudioSceneSetting CurrentScene => StudioScenes.EnsureCurrent(_s);

    private void BuildScenes()
    {
        StudioScenes.EnsureCurrent(_s);
        var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 260 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });

        // ---- cenas
        var scenes = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        tools.Children.Add(Glyph("", "Nova cena (começa vazia)", AddScene));
        tools.Children.Add(Glyph("", "Remover a cena escolhida", RemoveScene));
        tools.Children.Add(Glyph("", "Subir a cena", () => MoveScene(-1)));
        tools.Children.Add(Glyph("", "Descer a cena", () => MoveScene(1)));
        DockPanel.SetDock(tools, Dock.Right);
        head.Children.Add(tools);
        head.Children.Add(new TextBlock { Text = "Cenas", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        scenes.Children.Add(head);
        scenes.Children.Add(_sceneList);
        var scenesCard = CardOf(scenes);
        scenesCard.Padding = new Thickness(12, 10, 12, 10);
        scenesCard.Margin = new Thickness(0, 0, 10, 0);
        scenesCard.CornerRadius = new CornerRadius(12);
        grid.Children.Add(scenesCard);

        // ---- fontes da cena escolhida
        var sources = new StackPanel();
        var sourceHead = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var add = SmallButton("+ Adicionar fonte", (_, _) => { });
        add.Padding = new Thickness(10, 3, 10, 3);
        add.FontSize = 12;
        add.Click += (_, _) => ShowAddSource(add);
        DockPanel.SetDock(add, Dock.Right);
        sourceHead.Children.Add(add);
        sourceHead.Children.Add(_sourcesTitle);
        sources.Children.Add(sourceHead);
        sources.Children.Add(_sourceList);
        var sourcesCard = CardOf(sources);
        sourcesCard.Padding = new Thickness(12, 10, 12, 10);
        sourcesCard.CornerRadius = new CornerRadius(12);
        Grid.SetColumn(sourcesCard, 1);
        grid.Children.Add(sourcesCard);

        _config.Children.Add(grid);
        RebuildScenesUi();
    }

    private Button Glyph(string glyph, string tip, Action click)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12 },
            Style = (Style)FindResource("GhostButton"), ToolTip = tip, Margin = new Thickness(0, 0, 4, 0),
        };
        button.Padding = new Thickness(7, 4, 7, 4);
        button.Click += (_, _) => click();
        return button;
    }

    private void RebuildScenesUi()
    {
        var current = CurrentScene;
        bool running = _studio.IsRunning;
        if (!running) _airScene = null;
        else if (_airScene is null || !_s.StudioScenes.Contains(_airScene)) _airScene = current;

        _studioMode.Style = (Style)FindResource(_s.StudioSceneStudioMode ? "PrimaryButton" : "GhostButton");
        _studioMode.Padding = new Thickness(14, 4, 14, 4);
        _studioMode.FontSize = 12.5;

        _sceneList.Children.Clear();
        for (int i = 0; i < _s.StudioScenes.Count; i++)
        {
            int index = i;
            var scene = _s.StudioScenes[i];
            bool selected = scene == current;
            var row = new DockPanel();
            int count = scene.Items.Count;
            row.Children.Add(Right(new TextBlock { Text = count == 1 ? "1 fonte" : $"{count} fontes", FontSize = 11, Foreground = selected ? Ink : Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) }));
            if (running && scene == _airScene)
                row.Children.Add(Right(new Border
                {
                    Background = selected ? Ink : Yellow, CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = "NO AR", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = selected ? Yellow : Ink },
                }));
            row.Children.Add(new TextBlock { Text = scene.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = selected ? Ink : Brush("TextBrush"), VerticalAlignment = VerticalAlignment.Center });
            var item = new Border
            {
                Child = row, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 4), Cursor = System.Windows.Input.Cursors.Hand,
                Background = selected ? Yellow : Brush("Surface2Brush"), ToolTip = "Clique para escolher; botão direito para renomear, duplicar ou remover.",
            };
            item.MouseLeftButtonUp += async (_, _) => await SelectSceneAsync(index);
            item.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                ShowMenu(item, new (string, Action, bool)[]
                {
                    ("Renomear", () => RenameScene(index), true),
                    ("Duplicar", () => DuplicateScene(index), _s.StudioScenes.Count < StudioScenes.MaxScenes),
                    ("Remover", () => { _s.StudioSceneIndex = index; RemoveScene(); }, _s.StudioScenes.Count > 1),
                });
            };
            _sceneList.Children.Add(item);
        }

        RefreshStudioLayout();

        RebuildSources();
    }

    private static UIElement Right(UIElement element)
    {
        DockPanel.SetDock(element, Dock.Right);
        return element;
    }

    private void RebuildSources()
    {
        var scene = CurrentScene;
        _sourcesTitle.Text = $"Fontes · {scene.Name}";
        _sourceList.Children.Clear();
        if (scene.Items.Count == 0)
        {
            _sourceList.Children.Add(new Border
            {
                BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 14, 12, 14),
                Child = Muted("Esta cena ainda está vazia. Clique em \"+ Adicionar fonte\" para montar.", 12),
            });
            return;
        }

        for (int i = 0; i < scene.Items.Count; i++)
        {
            int index = i;
            var item = scene.Items[i];
            var row = new DockPanel();
            var more = Glyph("", "Mais opções", () => { });
            more.Click += (_, _) => ShowSourceMenu(more, index);
            var lockButton = Glyph(item.Locked ? "" : "", item.Locked ? "Travada: não dá para ocultar nem remover. Clique para destravar." : "Travar (evita mudar sem querer)", () =>
            {
                item.Locked = !item.Locked;
                _s.Save();
                RebuildSources();
            });
            var eye = Glyph(item.Hidden ? "" : "", item.Hidden ? "Oculta nesta cena: clique para mostrar" : "Aparece nesta cena: clique para ocultar", () =>
            {
                item.Hidden = !item.Hidden;
                SceneChanged();
            });
            eye.IsEnabled = !item.Locked && item.Kind != StudioSceneItemKind.Capture;
            if (item.Kind == StudioSceneItemKind.Capture) eye.ToolTip = "A tela ou a janela é o fundo de toda cena.";
            if (item.Hidden) eye.Opacity = 0.55;
            if (!item.Locked) lockButton.Opacity = 0.55;
            row.Children.Add(Right(more));
            row.Children.Add(Right(lockButton));
            row.Children.Add(Right(eye));
            row.Children.Add(new TextBlock
            {
                Text = StudioScenes.Title(item, _s), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Brush(item.Hidden ? "MutedBrush" : "TextBrush"),
            });
            _sourceList.Children.Add(new Border { Child = row, Background = Brush("Surface2Brush"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 3, 4, 3), Margin = new Thickness(0, 0, 0, 4) });
        }
    }

    // ---------------------------------------------------------------- ações das cenas

    private async Task SelectSceneAsync(int index)
    {
        if (index < 0 || index >= _s.StudioScenes.Count) return;
        bool changed = index != _s.StudioSceneIndex;
        _s.StudioSceneIndex = index;
        SceneChanged();
        // sem o modo estúdio, a cena entra no ar na hora (a captura reinicia por alguns segundos)
        if (changed && _studio.IsRunning && !_s.StudioSceneStudioMode) await TransitionAsync();
    }

    /// <summary>
    /// Põe no ar a cena escolhida. Com as cenas montadas juntas, só o seletor do FFmpeg muda (corte ou esmaecer, sem reiniciar).
    /// Se a montagem mudou (fonte nova, cena nova), a captura reinicia uma vez com tudo atualizado.
    /// </summary>
    private async Task TransitionAsync()
    {
        if (!_studio.IsRunning) return;
        var target = CurrentScene;
        var previous = _airScene;
        var (wanted, _) = BuildConfig(write: false);
        bool same = wanted is not null && _studio.Config is { } running && StudioController.Signature(wanted) == StudioController.Signature(running);
        if (same && _studio.Config is { LiveScenes: true })
        {
            if (await _studio.SwitchSceneAsync(_s.StudioScenes.IndexOf(target), (SceneTransition)Math.Clamp(_s.StudioSceneTransition, 0, 1)))
            {
                _airScene = target;
                SetStatus($"No ar: {target.Name}", "OkBrush");
            }
            else SetStatus("Não foi possível trocar a cena agora. Tente de novo.", "WarnBrush");
        }
        else if (same)
        {
            _airScene = target;   // as duas cenas têm as mesmas fontes: nada muda no vídeo
        }
        else
        {
            _airScene = target;   // a montagem nova já começa nesta cena
            if (!await ApplyAsync()) _airScene = previous;
        }
        RebuildScenesUi();
    }

    private void AddScene()
    {
        if (_s.StudioScenes.Count >= StudioScenes.MaxScenes) { SetStatus($"O limite é de {StudioScenes.MaxScenes} cenas.", "WarnBrush"); return; }
        var scene = new StudioSceneSetting { Name = StudioScenes.NextName(_s.StudioScenes.Select(x => x.Name)) };
        scene.Items.Add(new StudioSceneItem { Kind = StudioSceneItemKind.Capture, Locked = true });
        _s.StudioScenes.Add(scene);
        _s.StudioSceneIndex = _s.StudioScenes.Count - 1;
        SceneChanged();
        RenameScene(_s.StudioSceneIndex);
    }

    private void DuplicateScene(int index)
    {
        var from = _s.StudioScenes[index];
        var copy = new StudioSceneSetting
        {
            Name = StudioScenes.CleanName(from.Name + " (cópia)") ?? StudioScenes.NextName(_s.StudioScenes.Select(x => x.Name)),
            Items = from.Items.Select(i => new StudioSceneItem
            {
                Kind = i.Kind, Camera = i.Camera, Hidden = i.Hidden, Locked = i.Locked,
                CaptureMode = i.CaptureMode, Display = i.Display, WindowProcess = i.WindowProcess, WindowTitle = i.WindowTitle,
            }).ToList(),
        };
        _s.StudioScenes.Insert(index + 1, copy);
        _s.StudioSceneIndex = index + 1;
        SceneChanged();
    }

    private void RemoveScene()
    {
        if (_s.StudioScenes.Count <= 1) { SetStatus("Precisa ficar pelo menos uma cena.", "WarnBrush"); return; }
        var scene = CurrentScene;
        if (_studio.IsRunning && scene == _airScene) { SetStatus("Esta cena está no ar. Troque de cena antes de remover.", "WarnBrush"); return; }
        if (MessageBox.Show($"Remover a cena \"{scene.Name}\"?\n\nAs fontes dela saem junto (as câmeras e as configurações continuam no programa).",
                "Remover cena", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        StudioScenes.Remove(_s, _s.StudioSceneIndex);
        SceneChanged();
    }

    private void MoveScene(int delta)
    {
        _s.StudioSceneIndex = StudioScenes.Move(_s.StudioScenes, _s.StudioSceneIndex, delta);
        _s.Save();
        RebuildScenesUi();
    }

    private void RenameScene(int index)
    {
        if (index < 0 || index >= _s.StudioScenes.Count) return;
        var scene = _s.StudioScenes[index];
        var box = new TextBox { Text = scene.Name, MaxLength = 40, Padding = new Thickness(6, 4, 6, 4), FontSize = 14 };
        var panel = new StackPanel();
        panel.Children.Add(Muted("Nome da cena (ex.: Gravação YouTube, Tela cheia, Live):", 12));
        panel.Children.Add(box);
        box.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        ShowEditor("Nome da cena", panel, () =>
        {
            if (StudioScenes.CleanName(box.Text) is { } name) scene.Name = name;
            _s.Save();
            RebuildScenesUi();
        }, 420);
    }

    // ---------------------------------------------------------------- ações das fontes

    private void ShowAddSource(FrameworkElement anchor)
    {
        var scene = CurrentScene;
        var addable = StudioScenes.Addable(scene, _s);
        var options = addable.Select(item => (StudioScenes.Title(item, _s), (Action)(() =>
        {
            scene.Items.Add(item);
            SceneChanged();
        }), true)).ToList();
        options.Add(("Nova câmera ou celular...", () => _selectTab?.Invoke(4), true));
        if (addable.Count == 0) SetStatus("Todas as fontes já estão nesta cena. Para mais câmeras, use Gravar › Câmeras.");
        ShowMenu(anchor, options);
    }

    private void ShowSourceMenu(FrameworkElement anchor, int index)
    {
        var scene = CurrentScene;
        if (index < 0 || index >= scene.Items.Count) return;
        var item = scene.Items[index];
        var options = new List<(string, Action, bool)>
        {
            ("Propriedades", () => OpenSourceSettings(item), true),
        };
        if (item.Kind is StudioSceneItemKind.Camera or StudioSceneItemKind.Capture)
            options.Add(("Filtros", () => OpenSourceFilters(item), true));
        options.Add(("Subir", () => { StudioScenes.Move(scene.Items, index, -1); _s.Save(); RebuildSources(); }, index > 0));
        options.Add(("Descer", () => { StudioScenes.Move(scene.Items, index, 1); _s.Save(); RebuildSources(); }, index < scene.Items.Count - 1));
        options.Add((item.Locked ? "Remover (destrave antes)" : "Remover desta cena", () => { scene.Items.RemoveAt(index); SceneChanged(); }, !item.Locked));
        ShowMenu(anchor, options);
    }

    /// <summary>Abre onde a fonte é configurada (as configurações são as mesmas em todas as cenas).</summary>
    private void OpenSourceSettings(StudioSceneItem item)
    {
        switch (item.Kind)
        {
            case StudioSceneItemKind.Camera:
                var cam = _s.StudioCameras.FirstOrDefault(c => c.Name == item.Camera);
                if (cam is null) { _selectTab?.Invoke(4); return; }
                ShowEditor(StudioScenes.Title(item, _s), CameraExtras(cam));
                break;
            case StudioSceneItemKind.Pix:
                ShowEditor("Pix na live", _tabPix, () => { BuildPix(); UpdateGhosts(); });
                break;
            case StudioSceneItemKind.Capture:
                ShowEditor("Tela ou janela desta cena", CaptureEditor(item), SceneChanged, 560);
                break;
            default:
                _selectTab?.Invoke(5);
                break;
        }
    }

    private void OpenSourceFilters(StudioSceneItem item)
    {
        if (item.Kind == StudioSceneItemKind.Capture) { _selectTab?.Invoke(2); return; }
        var cam = _s.StudioCameras.FirstOrDefault(c => c.Name == item.Camera);
        if (cam is null) { _selectTab?.Invoke(4); return; }
        ShowEditor(StudioScenes.Title(item, _s) + " · filtros", CameraExtras(cam));
    }

    // ---------------------------------------------------------------- aplicar e sincronizar

    /// <summary>A cena mudou: liga e desliga as fontes conforme ela e atualiza o que depende disso.</summary>
    private void SceneChanged()
    {
        StudioScenes.Apply(CurrentScene, _s);
        _s.Save();
        RebuildCameras();
        BuildChannel();
        BuildPixCard();
        UpdateGhosts();
        RebuildScenesUi();
    }

    /// <summary>Algo foi ligado ou desligado direto nas abas: a cena escolhida passa a lembrar.</summary>
    private void SyncScene()
    {
        StudioScenes.Sync(CurrentScene, _s);
        RebuildScenesUi();
    }

    /// <summary>Menu pequeno no estilo do programa (preto), abaixo do botão.</summary>
    private void ShowMenu(FrameworkElement anchor, IEnumerable<(string Label, Action Act, bool Enabled)> options)
    {
        var list = new StackPanel { MinWidth = 200 };
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        foreach (var (label, act, enabled) in options)
        {
            var action = act;
            var button = new Button
            {
                Content = label, Style = (Style)FindResource("GhostButton"), HorizontalContentAlignment = HorizontalAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = enabled, BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 0, 2),
            };
            button.Padding = new Thickness(10, 5, 10, 5);
            button.FontSize = 12.5;
            button.Click += (_, _) => { popup.IsOpen = false; action(); };
            list.Children.Add(button);
        }
        popup.Child = new Border
        {
            Background = Brush("SurfaceBrush"), BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6), Child = list,
        };
        popup.IsOpen = true;
    }
}
