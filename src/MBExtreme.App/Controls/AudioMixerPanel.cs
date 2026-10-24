using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MBExtreme.Core.Recording;
using MBExtreme.Core.Settings;

namespace MBExtreme.UI.Controls;

/// <summary>Mixer de áudio no estilo do OBS: medidor e volume do som do PC e do microfone, e uma linha por programa que está tocando (jogo, Discord, música...).</summary>
internal sealed class AudioMixerPanel : StackPanel
{
    private sealed class Row
    {
        public required ProgressBar Meter { get; init; }
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
    private readonly AppAudioMixer _mixer = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly StackPanel _apps = new();
    private readonly Dictionary<string, Row> _appRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBlock _empty = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly Row _system;
    private readonly Row _mic;
    private int _tick;

    public AudioMixerPanel(AppSettings settings, StudioController studio, Func<string?> outputId, Func<string?> micId, Func<IEnumerable<string>> games)
    {
        _s = settings;
        _studio = studio;
        _outputId = outputId;
        _micId = micId;
        _games = games;

        Children.Add(Title("Mixer de áudio"));
        Children.Add(Note("Os medidores mostram o som em tempo real. O volume do som do PC e do microfone vale na hora, até com a transmissão no ar."));
        _system = MakeRow("Som do PC", 150, _s.StudioSystemVolume * 100, v => SetMain(false, v));
        _mic = MakeRow("Microfone", 150, _s.StudioMicVolume * 100, v => SetMain(true, v));
        Children.Add(_system.Container);
        Children.Add(_mic.Container);

        Children.Add(Title("Programas tocando agora"));
        var hint = Note("Jogo, Discord, música e navegador são reconhecidos sozinhos. Aqui o volume de cada programa é o do Windows: o que você baixa aqui baixa também no seu fone. (O volume só para a transmissão exige capturar cada programa separado, previsto para uma próxima versão.)");
        Children.Add(hint);
        Children.Add(_apps);
        _empty.Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("MutedBrush");
        Children.Add(_empty);

        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    private static TextBlock Title(string text) => new() { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 2) };

    private static TextBlock Note(string text) => new()
    {
        Text = text, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4),
        Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("MutedBrush"),
    };

    private Row MakeRow(string label, double max, double value, Action<double> changed, string? tag = null)
    {
        var meter = new ProgressBar { Minimum = 0, Maximum = 1, Height = 8, Margin = new Thickness(0, 4, 0, 4) };
        var slider = new Slider { Minimum = 0, Maximum = max, Value = Math.Min(max, value), SmallChange = 5, LargeChange = 10 };
        var percent = new TextBlock { Width = 46, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        var mute = new Button { Content = "Mudo", Padding = new Thickness(9, 3, 9, 3), FontSize = 11.5, Margin = new Thickness(8, 0, 0, 0), Style = (Style)Application.Current.FindResource("GhostButton"), VerticalAlignment = VerticalAlignment.Center };

        var middle = new StackPanel();
        middle.Children.Add(meter);
        middle.Children.Add(slider);

        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (!string.IsNullOrEmpty(tag)) name.Children.Add(new TextBlock { Text = tag, FontSize = 10.5, Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("AccentBrush") });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(name);
        Grid.SetColumn(middle, 1);
        grid.Children.Add(middle);
        Grid.SetColumn(percent, 2);
        grid.Children.Add(percent);
        Grid.SetColumn(mute, 3);
        grid.Children.Add(mute);

        var container = new Border
        {
            Style = (Style)Application.Current.FindResource("Card"), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 4), Child = grid,
        };
        var row = new Row { Meter = meter, Volume = slider, Percent = percent, Mute = mute, Container = container, Before = value > 0 ? value : 100 };
        percent.Text = $"{value:0}%";

        slider.ValueChanged += (_, e) =>
        {
            if (row.Updating) return;
            row.Muted = e.NewValue <= 0.5;
            if (!row.Muted) row.Before = e.NewValue;
            percent.Text = $"{e.NewValue:0}%";
            changed(e.NewValue);
            Paint(row);
        };
        mute.Click += (_, _) =>
        {
            row.Muted = !row.Muted;
            row.Updating = true;
            slider.Value = row.Muted ? 0 : Math.Max(row.Before, 5);
            row.Updating = false;
            percent.Text = $"{slider.Value:0}%";
            changed(slider.Value);
            Paint(row);
        };
        Paint(row);
        return row;
    }

    private static void Paint(Row row)
    {
        row.Mute.Content = row.Muted ? "Mudo ✓" : "Mudo";
        row.Mute.Style = (Style)Application.Current.FindResource(row.Muted ? "PrimaryButton" : "GhostButton");
        row.Mute.Padding = new Thickness(9, 3, 9, 3);
        row.Mute.FontSize = 11.5;
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
        _system.Meter.Value = _mixer.OutputPeak(_outputId());
        _mic.Meter.Value = _mixer.MicrophonePeak(_micId());
        // uma lista de programas nova por segundo; os medidores andam a cada tique
        IReadOnlyList<AppAudioSession> sessions;
        try { sessions = _mixer.Snapshot(_games()); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { sessions = Array.Empty<AppAudioSession>(); }
        if (++_tick % 7 == 1 || sessions.Count != _appRows.Count) Sync(sessions);

        foreach (var s in sessions)
        {
            if (!_appRows.TryGetValue(s.ProcessName, out var row)) continue;
            row.Meter.Value = s.Peak;
            if (!row.Volume.IsMouseCaptureWithin)
            {
                row.Updating = true;
                row.Volume.Value = s.Volume * 100;
                row.Updating = false;
                row.Percent.Text = $"{s.Volume * 100:0}%";
                row.Muted = s.Muted;
                Paint(row);
            }
        }
    }

    /// <summary>Cria e tira linhas conforme os programas começam e param de tocar.</summary>
    private void Sync(IReadOnlyList<AppAudioSession> sessions)
    {
        var current = sessions.ToDictionary(s => s.ProcessName, StringComparer.OrdinalIgnoreCase);
        foreach (string gone in _appRows.Keys.Where(k => !current.ContainsKey(k)).ToList())
        {
            _apps.Children.Remove(_appRows[gone].Container);
            _appRows.Remove(gone);
        }

        foreach (var s in sessions)
        {
            if (_appRows.ContainsKey(s.ProcessName)) continue;
            string process = s.ProcessName;
            var row = MakeRow(s.Display, 100, s.Volume * 100, v => _mixer.SetVolume(process, (float)(v / 100)), s.KindText);
            row.Mute.Click += (_, _) => _mixer.SetMuted(process, row.Muted);
            row.Muted = s.Muted;
            Paint(row);
            _appRows[process] = row;
            _apps.Children.Add(row.Container);
        }

        // jogos e programas de voz sempre no alto, na mesma ordem do Snapshot
        for (int i = 0; i < sessions.Count; i++)
        {
            var container = _appRows[sessions[i].ProcessName].Container;
            if (_apps.Children.IndexOf(container) != i)
            {
                _apps.Children.Remove(container);
                _apps.Children.Insert(Math.Min(i, _apps.Children.Count), container);
            }
        }

        _empty.Text = sessions.Count == 0 ? "Nenhum programa está tocando som agora. Abra o jogo, o Discord ou a música e eles aparecem aqui." : "";
        _empty.Visibility = sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
