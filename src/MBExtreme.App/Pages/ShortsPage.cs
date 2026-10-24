using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Recording;

namespace MBExtreme.UI.Pages;

/// <summary>Vídeos curtos: pega uma gravação longa e gera vários vídeos verticais (Shorts, Reels, TikTok), cortando na mão ou nos momentos mais fortes.</summary>
public sealed class ShortsPage : ToolPageBase
{
    private sealed class Cut
    {
        public bool On = true;
        public string Name = "";
        public double Start, End;
        public TextBlock? State;
    }

    private static readonly int[] Counts = { 3, 5, 8, 12 };
    private static readonly int[] Splits = { 30, 45, 60, 90 };

    private readonly MBExtreme.Core.Settings.AppSettings _s = AppServices.Settings;
    private readonly StackPanel _videoCard = new();
    private readonly StackPanel _cutsPanel = new();
    private readonly StackPanel _formatPanel = new();
    private readonly TextBlock _info = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly List<Cut> _cuts = new();
    private Button? _generate;
    private string? _ffmpeg;
    private string? _video;
    private double _duration;
    private (int W, int H)? _size;
    private bool _hasAudio, _busy;
    private int _suggestCount = 5;

    public ShortsPage() : base("Vídeos curtos (Shorts e Reels)", "Escolha uma gravação longa e gere vários vídeos verticais prontos para o YouTube Shorts, Instagram Reels e TikTok.")
    {
        AddButton("Escolher vídeo...", (_, _) => PickVideo());
        AddButton("Abrir a pasta dos curtos", (_, _) => OpenFolder(), false);

        Body.Children.Add(CardOf(_videoCard));
        Body.Children.Add(Heading("Cortes"));
        Body.Children.Add(CardOf(_cutsPanel));
        Body.Children.Add(Heading("Formato do vídeo vertical"));
        Body.Children.Add(CardOf(_formatPanel));
        _generate = new Button { Content = "Gerar os vídeos curtos", Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(0, 4, 0, 8) };
        _generate.Click += async (_, _) => await GenerateAsync();
        var cancel = new Button { Content = "Cancelar", Style = (Style)FindResource("GhostButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(12, 4, 0, 8) };
        cancel.Click += (_, _) => Cts?.Cancel();
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(_generate);
        actions.Children.Add(cancel);
        Body.Children.Add(actions);
        Body.Children.Add(Muted("Saem em MP4 com o áudio original, na pasta Vídeos\\MB Extreme\\Curtos. A legenda automática ainda não entra nos cortes (ela funciona ao gravar ou transmitir, na aba Vertical do estúdio).", 11));

        BuildVideoCard();
        BuildCuts();
        BuildFormat();
        Loaded += async (_, _) => await Task.Run(() => _ffmpeg ??= FfmpegRecorder.Locate()).ContinueWith(_ => Dispatcher.Invoke(BuildVideoCard));
    }

    private static string OutputRoot => Path.Combine(StudioController.Folder, "Curtos");

    private WrapPanel Chips(IReadOnlyList<string> options, int selected, Action<int> onChange)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        var buttons = new List<Button>();
        void Paint(int index)
        {
            for (int i = 0; i < buttons.Count; i++) { buttons[i].Style = (Style)FindResource(i == index ? "PrimaryButton" : "GhostButton"); buttons[i].Padding = new Thickness(10, 3, 10, 3); buttons[i].FontSize = 12; }
        }
        for (int i = 0; i < options.Count; i++)
        {
            int index = i;
            var button = new Button { Content = options[i], Margin = new Thickness(0, 0, 5, 5) };
            button.Click += (_, _) => { Paint(index); onChange(index); };
            buttons.Add(button);
            panel.Children.Add(button);
        }
        Paint(selected);
        return panel;
    }

    private static Grid Field(string label, UIElement content, UIElement? labelElement = null, double width = 150)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var text = new TextBlock { Text = label, FontSize = 12, Margin = new Thickness(0, 5, 8, 0), Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
        grid.Children.Add(text);
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
        return grid;
    }

    // ---------------------------------------------------------------- vídeo

    private void BuildVideoCard()
    {
        _videoCard.Children.Clear();
        _info.Foreground = Brush("MutedBrush");
        if (_ffmpeg is null && Dispatcher.CheckAccess() && !_busy) _info.Text = "Procurando o FFmpeg...";
        _videoCard.Children.Add(new TextBlock { Text = _video is null ? "Nenhum vídeo escolhido" : Path.GetFileName(_video), FontWeight = FontWeights.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap });
        _info.Text = _video is null
            ? (_ffmpeg is null ? "Precisa do FFmpeg (instale na página Gravação e transmissão, aba Avançado)." : "Clique em \"Escolher vídeo...\" ou use uma das gravações recentes abaixo.")
            : $"{TimeText.Format(_duration)} · {(_size is { } sz ? $"{sz.W}×{sz.H}" : "tamanho desconhecido")} · {(_hasAudio ? "com áudio" : "sem áudio")}";
        _videoCard.Children.Add(_info);

        var recent = RecentRecordings();
        if (recent.Count > 0)
        {
            _videoCard.Children.Add(new TextBlock { Text = "Gravações recentes:", FontSize = 11, Margin = new Thickness(0, 8, 0, 2), Foreground = Brush("MutedBrush") });
            var row = new WrapPanel();
            foreach (var file in recent)
            {
                var button = SmallButton(Path.GetFileName(file), async (_, _) => await LoadVideoAsync(file));
                button.Margin = new Thickness(0, 0, 6, 4);
                button.Padding = new Thickness(8, 3, 8, 3);
                button.FontSize = 11.5;
                row.Children.Add(button);
            }
            _videoCard.Children.Add(row);
        }
    }

    private static List<string> RecentRecordings()
    {
        try
        {
            var dir = new DirectoryInfo(StudioController.Folder);
            if (!dir.Exists) return new List<string>();
            return dir.EnumerateFiles().Where(f => f.Extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase) || f.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTime).Take(6).Select(f => f.FullName).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new List<string>(); }
    }

    private async void PickVideo()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Vídeos (*.mkv;*.mp4;*.mov;*.webm;*.avi)|*.mkv;*.mp4;*.mov;*.webm;*.avi", Title = "Escolha o vídeo longo", InitialDirectory = Directory.Exists(StudioController.Folder) ? StudioController.Folder : null };
        if (dialog.ShowDialog() == true) await LoadVideoAsync(dialog.FileName);
    }

    private async Task LoadVideoAsync(string file)
    {
        _ffmpeg ??= await Task.Run(FfmpegRecorder.Locate);
        if (_ffmpeg is null) { SetStatus("Instale o FFmpeg primeiro (página Gravação e transmissão, aba Avançado).", "WarnBrush"); return; }
        if (ShortsCommand.Validate(new ShortsRequest(file, new ShortSegment(0, 10, "x"), TestLayout, new EncoderOption(VideoCodec.H264, EncoderKind.Software, "libx264"), "x.mp4", true)) is { } bad && bad.StartsWith("O vídeo escolhido"))
        {
            SetStatus(bad + " Renomeie o arquivo ou copie para outra pasta.", "WarnBrush");
            return;
        }

        SetStatus("Lendo o vídeo...");
        string ffmpeg = _ffmpeg;
        var (_, _, err) = await Task.Run(() => Run(ffmpeg, ShortsCommand.ProbeArgs(file), CancellationToken.None));
        if (MediaProbe.ParseDuration(err) is not { } duration)
        {
            SetStatus("Não foi possível ler esse vídeo.", "BadBrush");
            return;
        }
        _video = file;
        _duration = duration;
        _size = MediaProbe.ParseVideoSize(err);
        _hasAudio = MediaProbe.HasAudio(err);
        _cuts.Clear();
        SetStatus("Vídeo carregado. Sugira os cortes ou adicione na mão.", "OkBrush");
        BuildVideoCard();
        BuildCuts();
    }

    private static readonly VerticalLayout TestLayout = new(1080, 1920, false, 50, new VerticalPane(PaneKind.Screen, PaneFit.Fit), new VerticalPane(PaneKind.Screen), false);

    // ---------------------------------------------------------------- cortes

    private void BuildCuts()
    {
        _cutsPanel.Children.Clear();
        var tools = new WrapPanel();
        var suggest = SmallButton("Sugerir os momentos mais fortes", async (_, _) => await SuggestAsync());
        suggest.Margin = new Thickness(0, 0, 8, 4);
        suggest.IsEnabled = _video is not null && _hasAudio && !_busy;
        tools.Children.Add(suggest);
        tools.Children.Add(Chips(Counts.Select(c => c + " cortes").ToList(), Math.Max(0, Array.IndexOf(Counts, _suggestCount)), i => _suggestCount = Counts[i]));
        _cutsPanel.Children.Add(tools);

        var split = new WrapPanel();
        split.Children.Add(new TextBlock { Text = "Ou dividir o vídeo inteiro em partes de:", FontSize = 12, Margin = new Thickness(0, 6, 8, 0), Foreground = Brush("MutedBrush") });
        foreach (int seconds in Splits)
        {
            var b = SmallButton(seconds + " s", (_, _) => SplitEvery(seconds));
            b.Margin = new Thickness(0, 0, 6, 4);
            b.IsEnabled = _video is not null && !_busy;
            split.Children.Add(b);
        }
        var add = SmallButton("+ Corte na mão", (_, _) => AddManual());
        add.Margin = new Thickness(8, 0, 6, 4);
        add.IsEnabled = _video is not null && !_busy;
        split.Children.Add(add);
        var clear = SmallButton("Limpar", (_, _) => { _cuts.Clear(); BuildCuts(); });
        clear.Margin = new Thickness(0, 0, 6, 4);
        split.Children.Add(clear);
        _cutsPanel.Children.Add(split);
        _cutsPanel.Children.Add(new TextBlock { Text = "Os momentos fortes são os pontos onde o som do vídeo sobe (gritos, comemorações, lances); eles ajudam, mas confira cada corte. Tempos no formato 1:23 (minuto:segundo). Cada corte pode ter de 3 a 180 segundos.", FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 2, 0, 6) });

        if (_cuts.Count == 0)
            _cutsPanel.Children.Add(new TextBlock { Text = _video is null ? "Escolha um vídeo para começar." : "Nenhum corte ainda.", Foreground = Brush("MutedBrush") });

        foreach (var cut in _cuts.ToList())
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            for (int i = 0; i < 6; i++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            var on = new CheckBox { IsChecked = cut.On, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            on.Click += (_, _) => cut.On = on.IsChecked == true;
            row.Children.Add(on);
            var name = new TextBox { Text = cut.Name, Margin = new Thickness(0, 0, 8, 0), MinWidth = 160 };
            name.TextChanged += (_, _) => cut.Name = name.Text;
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            var start = new TextBox { Text = TimeText.Format(cut.Start), Width = 70, Margin = new Thickness(0, 0, 4, 0) };
            var end = new TextBox { Text = TimeText.Format(cut.End), Width = 70, Margin = new Thickness(0, 0, 8, 0) };
            var state = new TextBlock { Text = DurationText(cut), VerticalAlignment = VerticalAlignment.Center, Width = 120, FontSize = 11.5, Foreground = Brush("MutedBrush") };
            cut.State = state;
            start.LostFocus += (_, _) => { if (TimeText.Parse(start.Text) is { } v) cut.Start = Math.Clamp(v, 0, _duration); start.Text = TimeText.Format(cut.Start); state.Text = DurationText(cut); };
            end.LostFocus += (_, _) => { if (TimeText.Parse(end.Text) is { } v) cut.End = Math.Clamp(v, 0, _duration); end.Text = TimeText.Format(cut.End); state.Text = DurationText(cut); };
            Grid.SetColumn(start, 2); Grid.SetColumn(end, 3); Grid.SetColumn(state, 4);
            row.Children.Add(start); row.Children.Add(end); row.Children.Add(state);
            var remove = SmallButton("Tirar", (_, _) => { _cuts.Remove(cut); BuildCuts(); });
            remove.Margin = new Thickness(0);
            remove.Padding = new Thickness(8, 3, 8, 3);
            Grid.SetColumn(remove, 5);
            row.Children.Add(remove);
            _cutsPanel.Children.Add(row);
        }
    }

    private static string DurationText(Cut cut)
    {
        var segment = new ShortSegment(cut.Start, cut.End, cut.Name);
        return segment.IsValid ? $"{segment.Duration:0.#} s" : $"{segment.Duration:0.#} s (3 a 180 s)";
    }

    private void SplitEvery(int seconds)
    {
        _cuts.Clear();
        foreach (var s in ShortsPlanner.FixedSplit(_duration, seconds)) _cuts.Add(new Cut { Name = s.Name, Start = s.Start, End = s.End });
        BuildCuts();
    }

    private void AddManual()
    {
        double start = _cuts.Count > 0 ? Math.Min(_cuts.Max(c => c.End), Math.Max(0, _duration - 30)) : 0;
        _cuts.Add(new Cut { Name = $"Corte {_cuts.Count + 1}", Start = start, End = Math.Min(_duration, start + 30) });
        BuildCuts();
    }

    private async Task SuggestAsync()
    {
        if (_video is null || _ffmpeg is null || _busy) return;
        _busy = true;
        BuildCuts();
        SetStatus("Ouvindo o vídeo para achar os momentos mais fortes... (pode levar alguns minutos em vídeos longos)");
        Cts = new CancellationTokenSource();
        string ffmpeg = _ffmpeg, video = _video;
        try
        {
            var (code, output, _) = await Task.Run(() => Run(ffmpeg, ShortsCommand.LoudnessArgs(video), Cts.Token));
            var series = MediaProbe.ParseLoudness(output);
            if (code != 0 && series.Count == 0) { SetStatus("Não foi possível analisar o áudio desse vídeo.", "BadBrush"); return; }
            var found = ShortsPlanner.FromLoudness(series, _duration, _suggestCount);
            _cuts.Clear();
            foreach (var s in found) _cuts.Add(new Cut { Name = s.Name, Start = s.Start, End = s.End });
            SetStatus(found.Count == 0 ? "Não achei momentos que se destacam no som. Divida por tempo ou corte na mão." : $"{found.Count} momento(s) sugerido(s). Ajuste os tempos se quiser.", found.Count == 0 ? "WarnBrush" : "OkBrush");
        }
        catch (OperationCanceledException) { SetStatus("Análise cancelada."); }
        finally
        {
            _busy = false;
            BuildCuts();
        }
    }

    // ---------------------------------------------------------------- formato

    private void BuildFormat()
    {
        _formatPanel.Children.Clear();
        var formats = VerticalFormat.All;
        int fi = Math.Clamp(_s.ShortsFormat, 0, formats.Count - 1);
        _formatPanel.Children.Add(Field("Tamanho", Chips(formats.Select(f => f.Name).ToList(), fi, i => _s.ShortsFormat = i)));
        _formatPanel.Children.Add(Field("Montagem", Chips(new[] { "Inteira, com fundo desfocado", "Preencher (corta as bordas)", "Dividida: jogo em cima e recorte embaixo" }, Math.Clamp(_s.ShortsMode, 0, 2), i => { _s.ShortsMode = i; BuildFormat(); })));
        if (_s.ShortsMode == 1)
            _formatPanel.Children.Add(Field("Parte que fica", Chips(new[] { "Esquerda", "Centro", "Direita" }, _s.ShortsFocus < 25 ? 0 : _s.ShortsFocus > 75 ? 2 : 1, i => _s.ShortsFocus = new[] { 0, 50, 100 }[i])));
        if (_s.ShortsMode == 2)
        {
            _formatPanel.Children.Add(Field("Recorte de baixo", Chips(new[] { "Canto superior esquerdo", "Canto superior direito", "Canto inferior esquerdo", "Canto inferior direito" }, Math.Clamp(_s.ShortsCorner, 0, 3), i => _s.ShortsCorner = i)));
            _formatPanel.Children.Add(Field("", new TextBlock { Text = "Mostra o vídeo inteiro em cima e, embaixo, o canto escolhido aproximado (por exemplo, a câmera do apresentador ou o minimapa).", FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Brush("MutedBrush") }));
        }
        _formatPanel.Children.Add(Field("Quadros por segundo", Chips(new[] { "30 fps", "60 fps" }, _s.ShortsFps >= 60 ? 1 : 0, i => _s.ShortsFps = i == 1 ? 60 : 30)));
        int[] rates = { 8, 12, 20 };
        _formatPanel.Children.Add(Field("Qualidade (taxa)", Chips(rates.Select(r => r + " Mbps").ToList(), Math.Max(0, Array.IndexOf(rates, _s.ShortsMbps)), i => _s.ShortsMbps = rates[i])));
    }

    private VerticalLayout CurrentLayout()
    {
        var f = VerticalFormat.ByIndex(_s.ShortsFormat);
        switch (_s.ShortsMode)
        {
            case 1:
                return new VerticalLayout(f.Width, f.Height, false, 50, new VerticalPane(PaneKind.Screen, PaneFit.Fill, _s.ShortsFocus), new VerticalPane(PaneKind.Screen), false);
            case 2:
                int corner = Math.Clamp(_s.ShortsCorner, 0, 3);
                int fx = corner % 2 == 0 ? 0 : 100, fy = corner < 2 ? 0 : 100;
                return new VerticalLayout(f.Width, f.Height, true, 58, new VerticalPane(PaneKind.Screen, PaneFit.Fit), new VerticalPane(PaneKind.Screen, PaneFit.Fill, fx, fy, 250), true);
            default:
                return new VerticalLayout(f.Width, f.Height, false, 50, new VerticalPane(PaneKind.Screen, PaneFit.Fit), new VerticalPane(PaneKind.Screen), false);
        }
    }

    // ---------------------------------------------------------------- gerar

    private EncoderOption PickEncoder()
    {
        var cached = AppServices.CachedEncoders;
        return cached?.FirstOrDefault(e => e.Codec == VideoCodec.H264) ?? new EncoderOption(VideoCodec.H264, EncoderKind.Software, "libx264");
    }

    private async Task GenerateAsync()
    {
        if (_busy) return;
        if (_video is null || _ffmpeg is null) { SetStatus("Escolha um vídeo primeiro.", "WarnBrush"); return; }
        var chosen = _cuts.Where(c => c.On).ToList();
        if (chosen.Count == 0) { SetStatus("Marque pelo menos um corte.", "WarnBrush"); return; }

        var layout = CurrentLayout();
        var encoder = PickEncoder();
        string folder = Path.Combine(OutputRoot, ShortsCommand.SafeName(Path.GetFileNameWithoutExtension(_video)));
        try { Directory.CreateDirectory(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetStatus("Não foi possível criar a pasta de saída: " + ex.Message, "BadBrush"); return; }

        var plan = new List<(Cut Cut, ShortsRequest Request)>();
        int n = 1;
        foreach (var cut in chosen)
        {
            string output = Path.Combine(folder, $"{n++:00}-{ShortsCommand.SafeName(cut.Name)}.mp4");
            var request = new ShortsRequest(_video, new ShortSegment(cut.Start, cut.End, cut.Name), layout, encoder, output, _hasAudio, _s.ShortsFps, _s.ShortsMbps);
            if (ShortsCommand.Validate(request) is { } problem) { SetStatus(problem, "WarnBrush"); return; }
            plan.Add((cut, request));
        }

        _busy = true;
        _generate!.IsEnabled = false;
        Cts = new CancellationTokenSource();
        string ffmpeg = _ffmpeg;
        int done = 0, failed = 0;
        try
        {
            foreach (var (cut, request) in plan)
            {
                if (Cts.IsCancellationRequested) break;
                SetStatus($"Gerando {done + failed + 1} de {plan.Count}: {cut.Name}...");
                if (cut.State is not null) cut.State.Text = "gerando...";
                var args = ShortsCommand.Build(request);
                var result = await Task.Run(() => Run(ffmpeg, args, Cts.Token, line =>
                {
                    if (line.StartsWith("out_time_us=", StringComparison.Ordinal) && long.TryParse(line[12..], out long us) && request.Segment.Duration > 0)
                    {
                        int percent = (int)Math.Clamp(us / 1_000_000.0 / request.Segment.Duration * 100, 0, 100);
                        Dispatcher.BeginInvoke(() => { if (cut.State is not null) cut.State.Text = percent + "%"; });
                    }
                }));
                if (result.ExitCode == 0 && File.Exists(request.OutputFile))
                {
                    done++;
                    if (cut.State is not null) cut.State.Text = "pronto";
                }
                else
                {
                    failed++;
                    if (cut.State is not null) cut.State.Text = "erro";
                    try { if (File.Exists(request.OutputFile)) File.Delete(request.OutputFile); } catch (IOException) { }
                }
            }

            if (Cts.IsCancellationRequested) SetStatus($"Cancelado. {done} vídeo(s) pronto(s) em {folder}.", "WarnBrush");
            else if (failed == 0) SetStatus($"{done} vídeo(s) curto(s) gerado(s) em {folder}.", "OkBrush");
            else SetStatus($"{done} gerado(s) e {failed} com erro. Se a placa de vídeo não aceitou, tente de novo (o app usa o codificador da placa; sem ele usa o processador).", "WarnBrush");
            if (done > 0) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (OperationCanceledException) { SetStatus($"Cancelado. {done} vídeo(s) pronto(s).", "WarnBrush"); }
        finally
        {
            _busy = false;
            _generate.IsEnabled = true;
            BuildCuts();
        }
    }

    private static void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(OutputRoot);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{OutputRoot}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
    }

    /// <summary>Roda o FFmpeg com os argumentos e devolve código de saída, texto de saída e de erro. <paramref name="onLine"/> recebe cada linha da saída (progresso).</summary>
    private static (int ExitCode, string Output, string Error) Run(string ffmpeg, IReadOnlyList<string> args, CancellationToken token, Action<string>? onLine = null)
    {
        var info = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var process = new Process { StartInfo = info };
        var output = new System.Text.StringBuilder();
        var error = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is null) return; if (onLine is null) output.AppendLine(e.Data); else onLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) error.AppendLine(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { } });
        process.WaitForExit();
        token.ThrowIfCancellationRequested();
        return (process.ExitCode, output.ToString(), error.ToString());
    }
}
