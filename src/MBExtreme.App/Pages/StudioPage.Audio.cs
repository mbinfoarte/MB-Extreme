using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Recording;

namespace MBExtreme.UI.Pages;

/// <summary>Tratamento profissional do áudio: limitador em dB, porta de ruído, compressor e filtro de graves. Tudo vale na hora, até com a live no ar.</summary>
public sealed partial class StudioPage
{
    private readonly TextBlock _limiterNow = new() { FontSize = 11.5 };

    private AudioProcessing CurrentAudioProcessing() => new AudioProcessing(
        _s.StudioAudHighPass, _s.StudioAudGate, _s.StudioAudGateDb, _s.StudioAudComp, _s.StudioAudCompDb, _s.StudioAudCompRatio,
        _s.StudioAudMakeupDb, _s.StudioAudLimiter, _s.StudioAudCeilingDb).Clamped();

    private void ApplyAudioProcessing() => _studio.SetProcessing(CurrentAudioProcessing());

    private void LoadPreset(AudioProcessing p)
    {
        _s.StudioAudHighPass = p.HighPass; _s.StudioAudGate = p.Gate; _s.StudioAudGateDb = p.GateDb;
        _s.StudioAudComp = p.Compressor; _s.StudioAudCompDb = p.CompressorDb; _s.StudioAudCompRatio = p.CompressorRatio; _s.StudioAudMakeupDb = p.MakeupDb;
        _s.StudioAudLimiter = p.Limiter; _s.StudioAudCeilingDb = p.CeilingDb;
        ApplyAudioProcessing();
    }

    private (Slider Slider, UIElement Row) DbSlider(string label, double min, double max, double value, string unit, Action<double> changed)
    {
        var text = new TextBlock { Width = 62, TextAlignment = TextAlignment.Right, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), TickFrequency = 0.5, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        void Show() => text.Text = $"{slider.Value:0.#} {unit}";
        slider.ValueChanged += (_, _) => { Show(); changed(slider.Value); ApplyAudioProcessing(); };
        Show();
        var row = new DockPanel { Margin = new Thickness(22, 0, 0, 4) };
        DockPanel.SetDock(text, Dock.Right);
        row.Children.Add(text);
        row.Children.Add(new TextBlock { Text = label, Width = 120, FontSize = 12, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(slider);
        return (slider, row);
    }

    private UIElement BuildAudioPro()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Áudio profissional", FontSize = 14, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(Muted("Deixa o áudio limpo e sem estourar. Tudo vale na hora, mesmo com a live no ar: mexa e ouça.", 11.5));

        var presets = Chips(new[] { "Padrão (só limitador)", "Voz limpa (live)", "Podcast" }, -1, i =>
        {
            LoadPreset(i switch { 1 => AudioProcessing.CleanVoice, 2 => AudioProcessing.Podcast, _ => AudioProcessing.Safe });
            RebuildAudioPro();
        });
        panel.Children.Add(Field("Atalhos", presets, 90));

        CheckBox Toggle(string text, bool on, Action<bool> set, string? tip = null)
        {
            var box = new CheckBox { Content = text, IsChecked = on, Foreground = Brush("TextBrush"), Margin = new Thickness(0, 6, 0, 2), FontSize = 12.5, FontWeight = FontWeights.SemiBold };
            if (tip is not null) box.ToolTip = tip;
            box.Click += (_, _) => { set(box.IsChecked == true); ApplyAudioProcessing(); };
            return box;
        }

        // limitador
        panel.Children.Add(Toggle("Limitador de volume (teto em dB)", _s.StudioAudLimiter, v => _s.StudioAudLimiter = v, "Nada passa deste volume: evita estourar o áudio da gravação e da live, sem estalo."));
        panel.Children.Add(DbSlider("Teto", -12, 0, _s.StudioAudCeilingDb, "dB", v => _s.StudioAudCeilingDb = v).Row);
        _limiterNow.Foreground = Brush("MutedBrush");
        _limiterNow.Margin = new Thickness(22, 0, 0, 4);
        (_limiterNow.Parent as Panel)?.Children.Remove(_limiterNow);
        panel.Children.Add(_limiterNow);

        // microfone
        panel.Children.Add(Muted("Só no microfone:", 11));
        panel.Children.Add(Toggle("Filtro de graves (corta ronco e vento)", _s.StudioAudHighPass, v => _s.StudioAudHighPass = v));
        panel.Children.Add(Toggle("Porta de ruído (fecha o som entre as falas)", _s.StudioAudGate, v => _s.StudioAudGate = v, "Abaixa teclado, ventilador e chiado quando você não está falando."));
        panel.Children.Add(DbSlider("Abre acima de", -70, -20, _s.StudioAudGateDb, "dB", v => _s.StudioAudGateDb = v).Row);
        panel.Children.Add(Toggle("Compressor (nivela a voz)", _s.StudioAudComp, v => _s.StudioAudComp = v, "Segura os gritos e levanta o que ficou baixo: a voz fica constante e profissional."));
        panel.Children.Add(DbSlider("Começa em", -40, -6, _s.StudioAudCompDb, "dB", v => _s.StudioAudCompDb = v).Row);
        panel.Children.Add(DbSlider("Ganho extra", 0, 12, _s.StudioAudMakeupDb, "dB", v => _s.StudioAudMakeupDb = v).Row);
        double[] ratios = { 2, 3, 4, 6 };
        var ratio = Chips(ratios.Select(r => r + ":1").ToList(), Math.Max(0, Array.IndexOf(ratios, Math.Round(_s.StudioAudCompRatio))), i => { _s.StudioAudCompRatio = ratios[i]; ApplyAudioProcessing(); });
        ratio.Margin = new Thickness(22, 0, 0, 0);
        panel.Children.Add(ratio);

        var card = CardOf(panel);
        card.Padding = new Thickness(12, 10, 12, 8);
        return card;
    }

    private void RebuildAudioPro()
    {
        if (_audioPro is null) return;
        int at = _tabAudio.Children.IndexOf(_audioPro);
        if (at < 0) return;
        _tabAudio.Children.RemoveAt(at);
        _audioPro = BuildAudioPro();
        _tabAudio.Children.Insert(at, _audioPro);
    }

    private UIElement? _audioPro;

    /// <summary>Mostra quanto o limitador está abaixando o volume agora.</summary>
    private void RefreshLimiterNow()
    {
        if (!_studio.IsRunning) { _limiterNow.Text = "O medidor do limitador aparece com a gravação ou a live no ar."; return; }
        double db = _studio.LimiterReductionDb;
        _limiterNow.Text = db < 0.3 ? "Limitador: sem precisar atuar (volume dentro do teto)." : $"Limitador abaixando {db:0.#} dB agora: o som estava passando do teto.";
    }
}
