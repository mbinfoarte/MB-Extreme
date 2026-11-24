using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Recording;
using MBExtreme.Core.Settings;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Modos prontos (Leve, Equilibrado, Qualidade, Ultra) da Live e da Gravação, com os ajustes finos escondidos atrás de "Editar",
/// e as plataformas da live com seleção múltipla: a primeira marcada é a principal; cada outra abre o seu bloco de chave e o
/// mesmo vídeo vai para todas ao mesmo tempo.
/// </summary>
public sealed partial class StudioPage
{
    private bool _keyLoadedOnce;

    /// <summary>Carrega a chave salva (protegida pelo Windows) uma vez só.</summary>
    private void EnsureKeyLoaded()
    {
        if (_keyLoadedOnce) return;
        _keyLoadedOnce = true;
        if (SecretStore.Unprotect(_s.StudioProtectedKey) is { } saved)
        {
            _key.Password = saved;
            _remember.IsChecked = true;
        }
    }

    // ---------------------------------------------------------------- modos

    private UIElement ModeCards(bool live)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        string encoderNote = _encoders.Count == 0 ? "detectando a placa de vídeo..." : "codificador escolhido pelo seu hardware";
        var title = new TextBlock { Text = (live ? "MODO DA LIVE" : "MODO DA GRAVAÇÃO") + "  ·  " + encoderNote, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brush("AccentBrush"), VerticalAlignment = VerticalAlignment.Center };
        head.Children.Add(title);
        panel.Children.Add(head);

        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(0, 0, -8, 0) };
        int selected = Math.Clamp(live ? _s.StudioLiveMode : _s.StudioRecordMode, 0, 3);
        foreach (var mode in StudioModes.All)
        {
            var plan = Plan(mode, live);
            bool on = (int)mode == selected;
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = StudioModes.Title(mode), FontSize = 14, FontWeight = FontWeights.Bold, Foreground = on ? Brush("AccentBrush") : Brush("TextBrush") });
            stack.Children.Add(new TextBlock { Text = plan?.Hint ?? "", FontSize = 11, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap });
            if (plan is not null)
            {
                foreach (var (label, value) in new[] { ("Imagem", $"{plan.Height}p · {plan.Fps} fps"), (live ? "Envio" : "Taxa", $"{plan.Mbps} Mbps"), ("Codec", plan.Encoder.Title) })
                {
                    var row = new DockPanel();
                    var v = new TextBlock { Text = value, FontSize = 11.5, Foreground = Brush("TextBrush"), TextTrimming = TextTrimming.CharacterEllipsis };
                    DockPanel.SetDock(v, Dock.Right);
                    row.Children.Add(v);
                    row.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Foreground = Brush("MutedBrush") });
                    stack.Children.Add(row);
                }
            }
            var card = new Border
            {
                Child = stack, Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 8, 0), CornerRadius = new CornerRadius(12),
                Background = on ? new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xC4, 0x00)) : Brush("SurfaceBrush"),
                BorderBrush = on ? Brush("AccentBrush") : Brush("BorderBrush"), BorderThickness = new Thickness(on ? 1.5 : 1), Cursor = System.Windows.Input.Cursors.Hand,
            };
            var m = mode;
            card.MouseLeftButtonUp += (_, _) => ApplyMode(m, live);
            grid.Children.Add(card);
        }
        panel.Children.Add(grid);
        return panel;
    }

    private ModePlan? Plan(StudioMode mode, bool live)
    {
        if (_encoders.Count == 0) return null;
        // com a placa de captura, a resolução de origem é a dela (ex.: 4K), não a do monitor
        int sourceHeight = _sourceTab == 2 && CardSource() is { } card ? card.Area.Height : MBExtreme.UI.Recording.ScreenInfo.Primary.Height;
        if (!live) return StudioModes.ForRecording(mode, _encoders, sourceHeight);
        var services = new List<StreamService> { CurrentService() };
        services.AddRange(_s.StudioExtraStreams.Select(e => (StreamService)Math.Clamp(e.Service, 0, 3)));
        bool youTubeOnly = services.All(s => s == StreamService.YouTube);
        int max = services.Min(s => PlatformSpecs.For(s).MaxVideoKbps) / 1000;
        return StudioModes.ForLive(mode, _encoders, youTubeOnly, max);
    }

    /// <summary>Aplica o modo: codificador, resolução, quadros e taxa. Os ajustes finos continuam editáveis.</summary>
    private void ApplyMode(StudioMode mode, bool live)
    {
        if (_studio.IsRunning) { SetStatus("Pare a transmissão ou a gravação para trocar o modo.", "WarnBrush"); return; }
        var plan = Plan(mode, live);
        if (live) _s.StudioLiveMode = (int)mode; else _s.StudioRecordMode = (int)mode;
        if (plan is not null)
        {
            int index = _encoders.ToList().FindIndex(e => e.FfmpegName == plan.Encoder.FfmpegName);
            if (index >= 0) { _encoderIndex = index; _s.StudioEncoderName = plan.Encoder.FfmpegName; }
            _s.StudioEncoderQuality = (int)plan.Quality;
            int resolution = Array.FindIndex(Resolutions, r => r.Height == plan.Height);
            if (resolution < 0) resolution = Array.FindIndex(Resolutions, r => r.Height <= plan.Height);
            if (resolution >= 0) _s.StudioResolution = resolution;
            _s.StudioFps = plan.Fps;
            if (live)
            {
                _s.StudioBitrateMbps = 0;   // automático, dentro do limite
                var caps = CapOptions.Where(c => c > 0 && c <= plan.Mbps).ToList();
                _s.StudioStreamCapMbps = caps.Count > 0 ? caps.Max() : 0;
            }
            else _s.StudioBitrateMbps = BitrateOptions.Where(b => b > 0).OrderBy(b => Math.Abs(b - plan.Mbps)).FirstOrDefault();
            SetStatus($"Modo {plan.Title}: {plan.Summary}.", "OkBrush");
        }
        _s.Save();
        BuildVideo();
        BuildStream();
        UpdatePlan();
    }

    /// <summary>Esconde os ajustes finos (do item <paramref name="keep"/> em diante) atrás do botão Editar.</summary>
    private void FoldDetails(StackPanel tab, int keep, bool live)
    {
        if (keep >= tab.Children.Count) return;
        bool open = live ? _s.StudioLiveDetails : _s.StudioRecordDetails;
        var details = new StackPanel { Visibility = Vis(open) };
        var moved = tab.Children.Cast<UIElement>().Skip(keep).ToList();
        foreach (var e in moved) tab.Children.Remove(e);
        foreach (var e in moved) details.Children.Add(e);
        var toggle = SmallButton(open ? "Fechar ajustes" : "Editar ajustes do modo", (_, _) => { });
        toggle.HorizontalAlignment = HorizontalAlignment.Left;
        toggle.Margin = new Thickness(0, 2, 0, 8);
        toggle.Click += (_, _) =>
        {
            bool show = details.Visibility != Visibility.Visible;
            details.Visibility = Vis(show);
            toggle.Content = show ? "Fechar ajustes" : "Editar ajustes do modo";
            if (live) _s.StudioLiveDetails = show; else _s.StudioRecordDetails = show;
        };
        tab.Children.Add(toggle);
        tab.Children.Add(details);
    }

    // ---------------------------------------------------------------- várias plataformas

    private static StreamService ServiceOfCard(string card) => card switch
    {
        "youtube" => StreamService.YouTube, "twitch" => StreamService.Twitch, "facebook" => StreamService.Facebook, _ => StreamService.Custom,
    };

    private string MainCard()
    {
        var service = CurrentService();
        bool kick = service == StreamService.Custom && _s.StudioStreamPreset == 0 && _s.StudioCustomUrl.Contains("live-video.net", StringComparison.OrdinalIgnoreCase);
        return service switch
        {
            StreamService.YouTube => "youtube", StreamService.Twitch => "twitch", StreamService.Facebook => "facebook",
            _ => _s.StudioStreamPreset == 1 ? "instagram" : _s.StudioStreamPreset == 2 ? "tiktok" : kick ? "kick" : "custom",
        };
    }

    /// <summary>Clique num cartão de plataforma: liga como a mais nova da lista ou desliga (a principal só sai se houver outra).</summary>
    private void TogglePlatform(string card)
    {
        string main = MainCard();
        var extra = _s.StudioExtraStreams.FirstOrDefault(e => e.Card == card);
        if (card == main)
        {
            if (_s.StudioExtraStreams.Count == 0) { BuildStream(); return; }   // sempre fica uma plataforma
            var next = _s.StudioExtraStreams[0];
            _s.StudioExtraStreams.RemoveAt(0);
            string? nextKey = SecretStore.Unprotect(next.ProtectedKey);
            if (next.Url.Length > 0) _s.StudioCustomUrl = next.Url;
            ChoosePlatform(next.Card);
            _key.Password = nextKey ?? "";
            return;
        }
        if (extra is not null)
        {
            _s.StudioExtraStreams.Remove(extra);
        }
        else if (card is "instagram" or "tiktok")
        {
            if (_s.StudioExtraStreams.Count > 0) { SetStatus("Instagram e TikTok são lives verticais: com várias plataformas, ligue a vertical em Formatos e avançado.", "WarnBrush"); BuildStream(); return; }
            ChoosePlatform(card);
            return;
        }
        else if (main is "instagram" or "tiktok")
        {
            ChoosePlatform(card);   // da vertical para a horizontal: troca a principal
            return;
        }
        else
        {
            _s.StudioExtraStreams.Add(new StudioExtraStream { Card = card, Service = (int)ServiceOfCard(card), Url = card == "kick" ? KickUrl : card == "custom" ? "rtmp://" : "" });
        }
        _s.Save();
        BuildStream();
        UpdatePlan();
    }

    /// <summary>Blocos de chave: o da plataforma principal (a mesma chave de sempre) e um para cada outra marcada.</summary>
    private UIElement KeyBlocks()
    {
        EnsureKeyLoaded();
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = Math.Min(2, 1 + _s.StudioExtraStreams.Count), Margin = new Thickness(0, 6, -10, 6) };
        string main = MainCard();
        Detach(_key);
        Detach(_remember);
        var mainBlock = new StackPanel();
        mainBlock.Children.Add(new TextBlock { Text = $"{CardTitle(main)} · principal", FontSize = 13, FontWeight = FontWeights.SemiBold });
        mainBlock.Children.Add(Muted("Chave de transmissão", 11));
        _key.Width = double.NaN;
        mainBlock.Children.Add(_key);
        mainBlock.Children.Add(_remember);
        grid.Children.Add(KeyCard(mainBlock));
        foreach (var extra in _s.StudioExtraStreams)
        {
            var block = new StackPanel();
            block.Children.Add(new TextBlock { Text = CardTitle(extra.Card), FontSize = 13, FontWeight = FontWeights.SemiBold });
            if (extra.Card is "custom" or "kick")
            {
                block.Children.Add(Muted("Servidor", 11));
                var url = new TextBox { Text = extra.Url, Margin = new Thickness(0, 4, 0, 4) };
                var e1 = extra;
                url.TextChanged += (_, _) => { e1.Url = url.Text.Trim(); _s.Save(); };
                block.Children.Add(url);
            }
            block.Children.Add(Muted("Chave de transmissão", 11));
            var key = new PasswordBox { Margin = new Thickness(0, 4, 0, 0), Password = SecretStore.Unprotect(extra.ProtectedKey) ?? "" };
            var e2 = extra;
            key.PasswordChanged += (_, _) => { e2.ProtectedKey = key.Password.Length > 0 ? SecretStore.Protect(key.Password) : null; _s.Save(); };
            block.Children.Add(key);
            grid.Children.Add(KeyCard(block));
        }
        return grid;
    }

    private Border KeyCard(UIElement child) => new()
    {
        Child = child, Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 10, 8), CornerRadius = new CornerRadius(12),
        Background = Brush("SurfaceBrush"), BorderBrush = Brush("BorderBrush"), BorderThickness = new Thickness(1),
    };

    private static string CardTitle(string card) => card switch
    {
        "youtube" => "YouTube", "twitch" => "Twitch", "kick" => "Kick", "tiktok" => "TikTok Live", "facebook" => "Facebook", "instagram" => "Instagram", _ => "Outro servidor",
    };

    /// <summary>Destinos das outras plataformas para a live (sem chave = fica de fora, com aviso).</summary>
    private IReadOnlyList<StreamTarget> ExtraTargets()
    {
        var list = new List<StreamTarget>();
        foreach (var e in _s.StudioExtraStreams)
        {
            string key = SecretStore.Unprotect(e.ProtectedKey) ?? "";
            var service = ServiceOfCard(e.Card);
            if (key.Length == 0 && service != StreamService.Custom) continue;
            list.Add(new StreamTarget(service, service == StreamService.Custom ? e.Url : "", key));
        }
        return list;
    }
}
