using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Games;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Aba "Configurações" do jogo: tipo de janela (e opções de inicialização), DLSS pelo driver (modelo, qualidade, geração de quadros,
/// Ray Reconstruction) e perfis prontos de driver ou importados de um .nip do NVIDIA Profile Inspector. Tudo vai para o perfil do próprio
/// jogo e volta com "Desfazer".
/// </summary>
public sealed partial class GameLibraryPage
{
    private string? _nvidiaName;

    private sealed record ConfigScan(GameEngine Engine, DlssFiles Dlss, string? ExePath, (string Model, string Mode, int FrameGen, bool Rr) Current);

    private readonly Dictionary<string, ConfigScan> _configScan = new();
    private readonly HashSet<string> _configScanning = new();

    private UIElement ConfigPanel(GameView v)
    {
        var game = v.Game;
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        if (!_configScan.TryGetValue(game.Id, out var scan))
        {
            panel.Children.Add(Muted("Lendo a pasta do jogo e o perfil dele no driver...", 11.5));
            if (_configScanning.Add(game.Id)) _ = ScanConfigAsync(game);
            return panel;
        }
        panel.Children.Add(WindowSection(game, v.Settings, scan));
        if (_maker == GpuMaker.Nvidia)
        {
            panel.Children.Add(DlssSection(game, scan));
            panel.Children.Add(PresetSection(game, v.Settings, scan));
        }
        else
            panel.Children.Add(CardOf(Muted("DLSS e perfis de driver são da NVIDIA. Nesta placa, use o FSR ou o XeSS no menu de gráficos do próprio jogo.", 11.5)));
        return panel;
    }

    private async Task ScanConfigAsync(InstalledGame game)
    {
        try
        {
            var scan = await Task.Run(() =>
            {
                var engine = GameWindow.DetectEngine(game.InstallPath);
                var dlss = DlssTuning.Scan(game.InstallPath);
                string? exePath = GameFiles.FindExePath(game);
                var current = game.ExeName is { } exe && AppServices.GameDriver.DriverAvailable
                    ? DlssTuning.Current(id => AppServices.GameDriver.ReadSpecific(exe, id))
                    : ("game", "game", 0, false);
                return new ConfigScan(engine, dlss, exePath, current);
            });
            _configScan[game.Id] = scan;
        }
        finally { _configScanning.Remove(game.Id); }
        if (_selected?.Id == game.Id && _tab == 2) Paint();
    }

    private void Rescan(InstalledGame game)
    {
        _configScan.Remove(game.Id);
        Paint();
    }

    // ------------------------------------------------------------ tipo de janela

    private UIElement WindowSection(InstalledGame game, GameSettings settings, ConfigScan scan)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Tipo de janela", FontSize = 14, FontWeight = FontWeights.SemiBold });
        var modes = new[] { GameWindowMode.Padrao, GameWindowMode.TelaCheia, GameWindowMode.SemBordas, GameWindowMode.Janela };
        stack.Children.Add(Chips(modes.Select(GameWindow.Name).ToList(), Array.IndexOf(modes, settings.WindowMode), i => SetWindowMode(game, modes[i], scan)));

        string how = WindowHow(game, settings.WindowMode, scan);
        stack.Children.Add(Muted($"Motor: {GameWindow.EngineName(scan.Engine)}. {how}", 11));

        var argsRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var save = SmallButton("Salvar", (_, _) => { });
        save.Padding = new Thickness(12, 3, 12, 3);
        DockPanel.SetDock(save, Dock.Right);
        var box = new TextBox { Text = settings.ExtraArgs ?? "", Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(0, 0, 8, 0), ToolTip = "Ex.: -novid -high +fps_max 0" };
        save.Click += (_, _) =>
        {
            string clean = GameWindow.CleanArgs(box.Text);
            AppServices.GameLibrary.Update(game.Id, s => s.ExtraArgs = clean.Length == 0 ? null : clean);
            box.Text = clean;
            SetStatus(clean.Length == 0 ? "Opções de inicialização apagadas." : $"Opções salvas: {clean}. Valem quando abrir o jogo pelo MB Extreme.", "OkBrush");
        };
        argsRow.Children.Add(save);
        argsRow.Children.Add(new TextBlock { Text = "Opções de inicialização:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), FontSize = 12 });
        argsRow.Children.Add(box);
        stack.Children.Add(argsRow);

        if (scan.ExePath is { } exePath)
        {
            var fso = new CheckBox
            {
                Content = "Desativar as otimizações de tela cheia do Windows para este jogo (ajuda em alguns jogos antigos e no atraso em tela cheia)",
                IsChecked = settings.FsoDisabled, Foreground = Brush("TextBrush"), FontSize = 11.5, Margin = new Thickness(0, 8, 0, 0),
            };
            fso.Click += (_, _) =>
            {
                bool on = fso.IsChecked == true;
                try
                {
                    AppServices.GameLibrary.Update(game.Id, s => GameWindowService.SetFullscreenOptimizations(exePath, on, s));
                    SetStatus(on ? "Otimizações de tela cheia desativadas para este jogo." : "Otimizações de tela cheia voltaram ao normal.", "OkBrush");
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
                {
                    SetStatus("Não foi possível mudar: " + ex.Message, "BadBrush");
                }
            };
            stack.Children.Add(fso);
        }
        return CardOf(stack);
    }

    private static string WindowHow(InstalledGame game, GameWindowMode mode, ConfigScan scan)
    {
        if (mode == GameWindowMode.Padrao) return "O jogo abre do jeito que ele guardou.";
        bool unrealIni = scan.Engine == GameEngine.Unreal && mode == GameWindowMode.SemBordas;
        if (unrealIni) return "Gravado no arquivo de configuração do jogo (com cópia de segurança). Vale mesmo abrindo pela loja.";
        if (GameWindow.LaunchArgs(scan.Engine, mode) is null)
            return "Este jogo não aceita escolher a janela por fora: mude no menu de vídeo do próprio jogo.";
        return game.Launcher switch
        {
            GameLauncher.Steam => "Vale ao clicar em Jogar aqui no MB Extreme (a Steam recebe a opção).",
            GameLauncher.Epic => "A Epic não passa opções ao jogo: mude no menu de vídeo do jogo (ou use a Steam/atalho manual).",
            _ => "Vale ao clicar em Jogar aqui no MB Extreme.",
        };
    }

    private void SetWindowMode(InstalledGame game, GameWindowMode mode, ConfigScan scan)
    {
        string? note = null;
        try
        {
            AppServices.GameLibrary.Update(game.Id, s =>
            {
                s.WindowMode = mode;
                if (scan.Engine != GameEngine.Unreal) return;
                if (mode == GameWindowMode.Padrao) { if (GameWindowService.RestoreUnreal(s)) note = "O arquivo de configuração do jogo voltou ao que era."; }
                else if (GameWindowService.ApplyUnreal(game, mode, s) is { } ini) note = "Gravado em " + Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(ini))))) + " (GameUserSettings.ini).";
                else if (mode == GameWindowMode.SemBordas) note = "Abra o jogo uma vez para ele criar o arquivo de configuração e escolha de novo.";
            });
            SetStatus($"{game.Name}: {GameWindow.Name(mode)}. " + (note ?? ""), "OkBrush");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Não foi possível gravar: " + ex.Message, "BadBrush");
        }
        Paint();
    }

    /// <summary>Abre o jogo com as opções de janela/inicialização, quando houver. Devolve false para abrir do jeito normal.</summary>
    private bool LaunchWithOptions(InstalledGame game)
    {
        var settings = AppServices.GameLibrary.Get(game.Id);
        var engine = _configScan.TryGetValue(game.Id, out var scan) ? scan.Engine : GameWindow.DetectEngine(game.InstallPath);
        string window = GameWindow.LaunchArgs(engine, settings.WindowMode) ?? "";
        string args = GameWindow.CleanArgs((window + " " + (settings.ExtraArgs ?? "")).Trim());
        if (args.Length == 0) return false;
        var plan = GameWindow.LaunchPlan(game, GameWindowService.SteamExe(), args, scan?.ExePath ?? GameFiles.FindExePath(game));
        if (plan is not { } p) return false;
        Process.Start(new ProcessStartInfo(p.File, p.Arguments) { UseShellExecute = false, WorkingDirectory = p.WorkingDirectory });
        SetStatus($"Abrindo {game.Name} com: {args}", "OkBrush");
        return true;
    }

    // ------------------------------------------------------------ DLSS

    private UIElement DlssSection(InstalledGame game, ConfigScan scan)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "DLSS (pelo driver da NVIDIA)", FontSize = 14, FontWeight = FontWeights.SemiBold });
        var rtx = RtxCapability.FromName(_nvidiaName);
        stack.Children.Add(Muted(DlssTuning.Compatibility(rtx, scan.Dlss), 11));
        if (rtx is null || !scan.Dlss.HasAny) return CardOf(stack);
        if (game.ExeName is null)
        {
            stack.Children.Add(Muted("Abra o jogo uma vez pelo MB Extreme (ou adicione o executável) para o programa saber qual perfil do driver usar.", 11));
            return CardOf(stack);
        }
        if (!AppServices.GameDriver.DriverAvailable)
        {
            stack.Children.Add(Muted("O driver da NVIDIA não respondeu. Atualize o driver e abra o MB Extreme como administrador.", 11));
            return CardOf(stack);
        }

        ComboBox Combo(IEnumerable<string> items, int selected)
        {
            var c = new ComboBox { Margin = new Thickness(0, 2, 0, 6), MinWidth = 260, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var i in items) c.Items.Add(i);
            c.SelectedIndex = Math.Max(0, selected);
            return c;
        }
        var models = DlssTuning.Models;
        var model = Combo(models.Select(m => m.Title), models.ToList().FindIndex(m => m.Key == scan.Current.Model));
        var modelInfo = Muted(models[Math.Max(0, model.SelectedIndex)].Detail, 10.5);
        model.SelectionChanged += (_, _) => modelInfo.Text = models[Math.Max(0, model.SelectedIndex)].Detail;
        stack.Children.Add(Muted("Modelo", 11));
        stack.Children.Add(model);
        stack.Children.Add(modelInfo);

        var modes = DlssTuning.Modes;
        var mode = Combo(modes.Select(m => m.Title), modes.ToList().FindIndex(m => m.Key == scan.Current.Mode));
        stack.Children.Add(Muted("Qualidade", 11));
        stack.Children.Add(mode);

        var fgOptions = new List<(string Title, int Value)> { ("Como o jogo escolher", 0) };
        if (rtx.FrameGeneration && scan.Dlss.FrameGeneration is not null)
        {
            fgOptions.Add(("Ligada · modelo mais recente (2x)", 1));
            if (rtx.MultiFrame) { fgOptions.Add(("Múltipla 3x (RTX 50)", 3)); fgOptions.Add(("Múltipla 4x (RTX 50)", 4)); }
        }
        var fg = Combo(fgOptions.Select(o => o.Title), fgOptions.FindIndex(o => o.Value == scan.Current.FrameGen));
        stack.Children.Add(Muted(rtx.FrameGeneration ? "Geração de quadros" : "Geração de quadros (só RTX 40 e 50)", 11));
        fg.IsEnabled = fgOptions.Count > 1;
        stack.Children.Add(fg);

        CheckBox? rr = null;
        if (scan.Dlss.RayReconstruction is not null)
        {
            rr = new CheckBox { Content = "Ray Reconstruction com o modelo mais recente", IsChecked = scan.Current.Rr, Foreground = Brush("TextBrush"), FontSize = 11.5, Margin = new Thickness(0, 0, 0, 6) };
            stack.Children.Add(rr);
        }

        var buttons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        buttons.Children.Add(Tight(SmallButton("Aplicar DLSS", async (_, _) =>
        {
            var plan = DlssTuning.Plan(models[Math.Max(0, model.SelectedIndex)].Key, modes[Math.Max(0, mode.SelectedIndex)].Key,
                fgOptions[Math.Max(0, fg.SelectedIndex)].Value, rr?.IsChecked == true, rtx, scan.Dlss);
            await ApplyDriverAsync(game, scan, plan.Write, plan.Remove, "DLSS");
        }, true)));
        buttons.Children.Add(Tight(SmallButton("Voltar ao DLSS do jogo", async (_, _) => await ApplyDriverAsync(game, scan, Array.Empty<DriverSetting>(), DlssIds.All, "DLSS"))));
        stack.Children.Add(buttons);
        stack.Children.Add(Muted("Teste primeiro fora de partidas online. Se o jogo travar ou a imagem piorar, use \"Voltar ao DLSS do jogo\".", 10.5));
        return CardOf(stack);
    }

    private async Task ApplyDriverAsync(InstalledGame game, ConfigScan scan, IReadOnlyList<DriverSetting> write, IReadOnlyList<uint> remove, string what)
    {
        string exe = game.ExeName!;
        SetStatus($"Gravando {what} no perfil de {game.Name} no driver...");
        var result = await Task.Run(() =>
        {
            var reset = AppServices.GameDriver.Reset(game.Id, exe, remove.Where(id => write.All(w => w.Id != id)).ToList());
            var applied = write.Count == 0 ? new DriverApplyResult(Array.Empty<string>(), Array.Empty<string>())
                : AppServices.GameDriver.Apply(game.Id, exe, scan.ExePath, new DriverPlan(null, null, null), write);
            return (Done: reset.Applied.Concat(applied.Applied).ToList(), Failed: reset.Failed.Concat(applied.Failed).ToList());
        });
        if (result.Failed.Count > 0) SetStatus($"{what}: {result.Failed.Count} item(ns) não gravaram: {result.Failed[0]}", "BadBrush");
        else SetStatus(result.Done.Count == 0 ? $"{what}: nada mudou (já estava assim)." : $"{what} gravado no perfil de {game.Name}. Feche e abra o jogo para valer.", "OkBrush");
        Rescan(game);
    }

    // ------------------------------------------------------------ perfis prontos e .nip

    private UIElement PresetSection(InstalledGame game, GameSettings settings, ConfigScan scan)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Perfil de driver (estilo NVIDIA Profile Inspector)", FontSize = 14, FontWeight = FontWeights.SemiBold });
        if (game.ExeName is null || !AppServices.GameDriver.DriverAvailable)
        {
            stack.Children.Add(Muted(game.ExeName is null ? "Abra o jogo uma vez pelo MB Extreme para o programa saber o executável dele." : "O driver da NVIDIA não respondeu.", 11));
            return CardOf(stack);
        }
        var presets = DriverPresets.All;
        int current = presets.ToList().FindIndex(p => p.Key == settings.DriverPresetKey);
        var list = new StackPanel();
        var chosen = new int[] { Math.Max(0, current) };
        var detail = Muted("", 10.5);
        void Describe() => detail.Text = presets[chosen[0]].Description + "\n" + string.Join("\n", presets[chosen[0]].Settings.Select(s => $"• {s.Title}: {s.ValueText}"));
        for (int i = 0; i < presets.Count; i++)
        {
            int index = i;
            var radio = new RadioButton { Content = presets[i].Title + (i == current ? "  (aplicado)" : ""), GroupName = "preset-" + game.Id, IsChecked = i == chosen[0], Foreground = Brush("TextBrush"), FontSize = 12, Margin = new Thickness(0, 3, 0, 0) };
            radio.Checked += (_, _) => { chosen[0] = index; Describe(); };
            list.Children.Add(radio);
        }
        Describe();
        stack.Children.Add(list);
        stack.Children.Add(detail);

        var fpsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        fpsRow.Children.Add(new TextBlock { Text = "Limite de FPS do driver (0 = sem limite):", VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Margin = new Thickness(0, 0, 8, 0) });
        var fps = new TextBox { Text = "0", Width = 60, Padding = new Thickness(6, 3, 6, 3) };
        fpsRow.Children.Add(fps);
        stack.Children.Add(fpsRow);

        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(Tight(SmallButton("Aplicar perfil", async (_, _) =>
        {
            var preset = presets[chosen[0]];
            var items = preset.Settings.ToList();
            if (int.TryParse(fps.Text.Trim(), out int limit) && limit > 0) items.Add(DriverPresets.FpsLimit(limit));
            AppServices.GameLibrary.Update(game.Id, s => s.DriverPresetKey = preset.Key);
            await ApplyDriverAsync(game, scan, items, Array.Empty<uint>(), "Perfil " + preset.Title);
        }, true)));
        buttons.Children.Add(Tight(SmallButton("Importar .nip...", async (_, _) => await ImportNipAsync(game, scan))));
        buttons.Children.Add(Tight(SmallButton("Desfazer tudo do driver neste jogo", async (_, _) =>
        {
            var undo = await Task.Run(() => AppServices.GameDriver.Undo(game.Id));
            AppServices.GameLibrary.Update(game.Id, s => s.DriverPresetKey = null);
            SetStatus(undo.Failed.Count > 0 ? "Parte não voltou: " + undo.Failed[0] : $"Driver de {game.Name} de volta ao que era ({undo.Applied.Count} item(ns)).", undo.Failed.Count > 0 ? "BadBrush" : "OkBrush");
            Rescan(game);
        })));
        stack.Children.Add(buttons);
        stack.Children.Add(Muted("Só o perfil deste jogo muda; o global do driver fica como está. Os valores de antes ficam guardados para desfazer.", 10.5));
        return CardOf(stack);
    }

    private async Task ImportNipAsync(InstalledGame game, ConfigScan scan)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Perfil do NVIDIA Profile Inspector (*.nip)|*.nip|Todos os arquivos|*.*", Title = "Importar perfil .nip" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        string xml;
        try
        {
            var info = new FileInfo(dialog.FileName);
            if (info.Length > 20_000_000) { SetStatus("Arquivo grande demais para um .nip.", "BadBrush"); return; }
            xml = await File.ReadAllTextAsync(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetStatus("Não foi possível ler: " + ex.Message, "BadBrush"); return; }

        var profiles = DriverPresets.ParseNip(xml);
        if (profiles.Count == 0) { SetStatus("Esse arquivo não é um .nip válido.", "BadBrush"); return; }
        var profile = DriverPresets.PickFor(profiles, game.ExeName) ?? profiles[0];
        if (profile.Settings.Count == 0) { SetStatus("O .nip não tem configurações que dê para aplicar.", "WarnBrush"); return; }

        string mismatch = profile.Executables.Count > 0 && !profile.Executables.Contains(game.ExeName!, StringComparer.OrdinalIgnoreCase)
            ? $"\n\nAtenção: este perfil foi feito para {string.Join(", ", profile.Executables.Take(3))}, não para {game.ExeName}." : "";
        string lines = string.Join("\n", profile.Settings.Take(25).Select(s => $"• {s.Name}: {s.Value}")) + (profile.Settings.Count > 25 ? $"\n… e mais {profile.Settings.Count - 25}" : "");
        string skipped = profile.Skipped > 0 ? $"\n\n{profile.Skipped} item(ns) que não são números foram ignorados." : "";
        var answer = MessageBox.Show(Window.GetWindow(this), $"Aplicar o perfil \"{profile.Name}\" em {game.Name}?\n\n{lines}{skipped}{mismatch}\n\nOs valores atuais ficam guardados e dá para desfazer.",
            "Importar perfil .nip", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        var items = profile.Settings.Select(s => new DriverSetting(s.Id, s.Value, s.Name, s.Value.ToString(), "", false)).ToList();
        AppServices.GameLibrary.Update(game.Id, s => s.DriverPresetKey = "nip");
        await ApplyDriverAsync(game, scan, items, Array.Empty<uint>(), "Perfil .nip");
    }
}
