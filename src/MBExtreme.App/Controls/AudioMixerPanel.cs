using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MBExtreme.Core.Recording;
using MBExtreme.Core.Settings;

namespace MBExtreme.UI.Controls;

/// <summary>Mixer de áudio em colunas curtas, como numa mesa de som: som do PC, microfone e um canal por programa que está tocando (jogo, Discord, música...).</summary>
internal sealed class AudioMixerPanel : StackPanel
{
    private const double StripHeight = 120;
    private static readonly SolidColorBrush Yellow = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)));
    private static readonly SolidColorBrush Hot = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x3D)));

    private sealed class Strip
    {
        public required Border Level { get; init; }
        public required Slider Volume { get; init; }
        public required TextBlock Percent { get; init; }
        public required Button Mute { get; init; }
        public required Border Container { get; init; }
        public bool Muted { get; set; }
        public double Before { get; set; } = 100;
        public bool Updating { get; set; }
    }

    private readonly AppSettings _s;
    private readonly StudioController _studio;
    private readonly Func<string?> _outputId;
    private readonly Func<string?> _micId;
    private readonly Func<IEnumerable<string>> _games;
    private readonly Action? _openFilters;
    private readonly AppAudioMixer _mixer = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly WrapPanel _strips = new();
    private readonly Dictionary<string, Strip> _appStrips = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBlock _empty = new() { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
    private readonly Strip _system;
    private readonly Strip _mic;
    private int _tick;

    public AudioMixerPanel(AppSettings settings, StudioController studio, Func<string?> outputId, Func<string?> micId, Func<IEnumerable<string>> games, Action? openFilters = null)
    {
        _s = settings;
        _studio = studio;
        _outputId = outputId;
        _micId = micId;
        _games = games;
        _openFilters = openFilters;

        _system = MakeStrip("Som do PC", null, 150, _s.StudioSystemVolume * 100, v => SetMain(false, v), true);
        _mic = MakeStrip("Microfone", null, 150, _s.StudioMicVolume * 100, v => SetMain(true, v), true);
        _strips.Children.Add(_system.Container);
        _strips.Children.Add(_mic.Container);
        Children.Add(_strips);

        _empty.Foreground = (Brush)Application.Current.FindResource("MutedBrush");
        Children.Add(_empty);

        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    private static SolidColorBrush Freeze(SolidColorBrush brush) { brush.Freeze(); return brush; }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    private Strip MakeStrip(string label, string? tag, double max, double value, Action<double> changed, bool filters)
    {
        var level = new Border { Background = Yellow, Height = 0, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(3) };
        var meter = new Border { Width = 8, Height = StripHeight, CornerRadius = new CornerRadius(3), Background = Res("BorderBrush"), Child = level, Margin = new Thickness(0, 0, 6, 0) };
        var slider = new Slider
        {
            Orientation = Orientation.Vertical, Height = StripHeight, Minimum = 0, Maximum = max, Value = Math.Min(max, value),
            SmallChange = 5, LargeChange = 10, ToolTip = "Volume de " + label,
        };
        var percent = new TextBlock { FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Res("MutedBrush"), Margin = new Thickness(0, 4, 0, 4) };
        var mute = new Button { Padding = new Thickness(6, 2, 6, 2), FontSize = 11, Margin = new Thickness(0, 0, 0, 4), HorizontalAlignment = HorizontalAlignment.Stretch };

        var name = new TextBlock
        {
            Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = label, Margin = new Thickness(0, 0, 0, 2),
        };
        var column = new StackPanel();
        column.Children.Add(name);
        column.Children.Add(new TextBlock { Text = tag ?? " ", FontSize = 10.5, TextAlignment = TextAlignment.Center, Foreground = Yellow, Margin = new Thickness(0, 0, 0, 4) });
        var bars = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        bars.Children.Add(meter);
        bars.Children.Add(slider);
        column.Children.Add(bars);
        column.Children.Add(percent);
        column.Children.Add(mute);
        if (filters && _openFilters is not null)
        {
            var filter = new Button
            {
                Content = "Filtro", Padding = new Thickness(6, 2, 6, 2), FontSize = 11, Style = (Style)Application.Current.FindResource("GhostButton"),
                ToolTip = "Limitador, porta de ruído, compressor e filtro de graves. Muda na hora, sem parar.",
            };
            filter.Click += (_, _) => _openFilters();
            column.Children.Add(filter);
        }

        var container = new Border
        {
            Style = (Style)Application.Current.FindResource("Card"), Width = 92, Padding = new Thickness(6, 8, 6, 8),
            Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(10), Child = column,
        };
        var strip = new Strip { Level = level, Volume = slider, Percent = percent, Mute = mute, Container = container, Before = value > 0 ? value : 100 };
        percent.Text = $"{value:0}%";

        slider.ValueChanged += (_, e) =>
        {
            if (strip.Updating) return;
            strip.Muted = e.NewValue <= 0.5;
            if (!strip.Muted) strip.Before = e.NewValue;
            percent.Text = $"{e.NewValue:0}%";
            changed(e.NewValue);
            Paint(strip);
        };
        mute.Click += (_, _) =>
        {
            strip.Muted = !strip.Muted;
            strip.Updating = true;
            slider.Value = strip.Muted ? 0 : Math.Max(strip.Before, 5);
            strip.Updating = false;
            percent.Text = $"{slider.Value:0}%";
            changed(slider.Value);
            Paint(strip);
        };
        Paint(strip);
        return strip;
    }

    private static void Paint(Strip strip)
    {
        strip.Mute.Content = strip.Muted ? "Mudo" : "Som";
        strip.Mute.Style = (Style)Application.Current.FindResource(strip.Muted ? "PrimaryButton" : "GhostButton");
        strip.Mute.Padding = new Thickness(6, 2, 6, 2);
        strip.Mute.FontSize = 11;
        strip.Mute.ToolTip = strip.Muted ? "Está mudo: clique para voltar o som." : "Clique para deixar mudo.";
    }

    private static void ShowLevel(Strip strip, double peak)
    {
        double value = Math.Clamp(peak, 0, 1);
        strip.Level.Height = StripHeight * value;
        strip.Level.Background = value > 0.85 ? Hot : Yellow;
    }

    private void SetMain(bool microphone, double percent)
    {
        float volume = (float)(percent / 100);
        if (microphone) _s.StudioMicVolume = volume; else _s.StudioSystemVolume = volume;
        if (_studio.IsRunning) _studio.SetVolume(microphone, volume);
    }

    private void Tick()
    {
        if (!IsVisible) return;
        ShowLevel(_system, _mixer.OutputPeak(_outputId()));
        ShowLevel(_mic, _mixer.MicrophonePeak(_micId()));
        // uma lista de programas nova por segundo; os medidores andam a cada tique
        IReadOnlyList<AppAudioSession> sessions;
        try { sessions = _mixer.Snapshot(_games()); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { sessions = Array.Empty<AppAudioSession>(); }
        if (++_tick % 7 == 1 || sessions.Count != _appStrips.Count) Sync(sessions);

        foreach (var s in sessions)
        {
            if (!_appStrips.TryGetValue(s.ProcessName, out var strip)) continue;
            ShowLevel(strip, s.Peak);
            if (!strip.Volume.IsMouseCaptureWithin)
            {
                strip.Updating = true;
                strip.Volume.Value = s.Volume * 100;
                strip.Updating = false;
                strip.Percent.Text = $"{s.Volume * 100:0}%";
                strip.Muted = s.Muted;
                Paint(strip);
            }
        }
    }

    /// <summary>Cria e tira colunas conforme os programas começam e param de tocar. As duas primeiras (som do PC e microfone) ficam fixas.</summary>
    private void Sync(IReadOnlyList<AppAudioSession> sessions)
    {
        var current = sessions.ToDictionary(s => s.ProcessName, StringComparer.OrdinalIgnoreCase);
        foreach (string gone in _appStrips.Keys.Where(k => !current.ContainsKey(k)).ToList())
        {
            _strips.Children.Remove(_appStrips[gone].Container);
            _appStrips.Remove(gone);
        }

        foreach (var s in sessions)
        {
            if (_appStrips.ContainsKey(s.ProcessName)) continue;
            string process = s.ProcessName;
            var strip = MakeStrip(s.Display, s.KindText, 100, s.Volume * 100, v => _mixer.SetVolume(process, (float)(v / 100)), false);
            strip.Mute.Click += (_, _) => _mixer.SetMuted(process, strip.Muted);
            strip.Muted = s.Muted;
            Paint(strip);
            _appStrips[process] = strip;
            _strips.Children.Add(strip.Container);
        }

        // jogos e programas de voz logo depois do microfone, na mesma ordem do Snapshot
        for (int i = 0; i < sessions.Count; i++)
        {
            var container = _appStrips[sessions[i].ProcessName].Container;
            int want = i + 2;
            if (_strips.Children.IndexOf(container) != want)
            {
                _strips.Children.Remove(container);
                _strips.Children.Insert(Math.Min(want, _strips.Children.Count), container);
            }
        }

        _empty.Text = sessions.Count == 0
            ? "Os programas que tocam som (jogo, Discord, música) aparecem aqui como colunas. O volume deles é o do Windows."
            : "Colunas de programas: o volume é o do Windows (baixar aqui baixa no seu fone também).";
    }
}
