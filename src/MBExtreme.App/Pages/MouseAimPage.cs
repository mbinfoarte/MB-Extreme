using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using MBExtreme.Core.Gamepads;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Mira do mouse em jogos de tiro: distância por volta (cm/360), sensibilidade certa para o estilo, conversão entre
/// jogos, aceleração do Windows e teste da taxa de atualização do mouse. Não mexe no jogo nem finge ser controle.
/// </summary>
public sealed class MouseAimPage : ToolPageBase
{
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);
    [StructLayout(LayoutKind.Sequential)] private struct RawInputDevice { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }
    private const int WM_INPUT = 0x00FF;
    private const uint RIDEV_INPUTSINK = 0x100, RIDEV_REMOVE = 0x1;

    private readonly TextBox _dpi = Field("800");
    private readonly TextBox _sens = Field("5");
    private readonly ComboBox _game = new() { MinWidth = 260 };
    private readonly ComboBox _toGame = new() { MinWidth = 260 };
    private readonly TextBox _toDpi = Field("800");
    private readonly StackPanel _result = new();
    private readonly StackPanel _styles = new();
    private readonly TextBlock _convert = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _windows = new();
    private readonly TextBlock _poll = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _pollButton;

    public MouseAimPage() : base("Mira do mouse",
        "Deixa a mira do mouse igual em todo jogo de tiro: mostra quantos centímetros de mouse dão uma volta, sugere a sensibilidade para o seu estilo, converte de um jogo para outro e confere o Windows e o mouse. Não dá auxílio de mira: isso o jogo só dá para controle.")
    {
        foreach (var g in MouseAim.Games) { _game.Items.Add(g.Name); _toGame.Items.Add(g.Name); }
        _game.SelectedIndex = 0;
        _toGame.SelectedIndex = 2;

        // 1. o que você usa hoje
        Body.Children.Add(Heading("1. Sua mira hoje"));
        var now = new WrapPanel();
        now.Children.Add(Labeled("Jogo", _game));
        now.Children.Add(Labeled("DPI do mouse", _dpi));
        now.Children.Add(Labeled("Sensibilidade no jogo", _sens));
        Body.Children.Add(CardOf(new StackPanel { Children = { now, _result, Muted("Não sabe o DPI? Veja no programa do mouse (Logitech G Hub, Razer Synapse...) ou na caixa. 800 e 1600 são os mais comuns.", 11.5) } }));

        // 2. estilo
        Body.Children.Add(Heading("2. Sensibilidade sugerida para o seu estilo"));
        Body.Children.Add(CardOf(_styles));

        // 3. converter
        Body.Children.Add(Heading("3. Levar a mesma mira para outro jogo"));
        var conv = new WrapPanel();
        conv.Children.Add(Labeled("Para o jogo", _toGame));
        conv.Children.Add(Labeled("Com o DPI", _toDpi));
        Body.Children.Add(CardOf(new StackPanel { Children = { conv, _convert } }));

        // 4. COD
        Body.Children.Add(Heading("4. Dentro do Call of Duty"));
        var tips = new StackPanel();
        foreach (var t in MouseAim.CodTips) tips.Children.Add(Muted("• " + t, 12.5));
        Body.Children.Add(CardOf(tips));

        // 5. Windows
        Body.Children.Add(Heading("5. Windows"));
        Body.Children.Add(_windows);

        // 6. taxa do mouse
        Body.Children.Add(Heading("6. Taxa de atualização do mouse"));
        var pollCard = new StackPanel();
        pollCard.Children.Add(Muted("Clique em Medir e mexa o mouse em círculos rápidos por 3 segundos. 1000 Hz ou mais é o ideal para jogos de tiro; 125 Hz deixa a mira \"picotada\".", 12));
        _pollButton = SmallButton("Medir agora", async (_, _) => await MeasurePollingAsync(), true);
        _pollButton.HorizontalAlignment = HorizontalAlignment.Left;
        _pollButton.Margin = new Thickness(0, 10, 0, 0);
        pollCard.Children.Add(_pollButton);
        pollCard.Children.Add(_poll);
        Body.Children.Add(CardOf(pollCard));

        foreach (var box in new[] { _dpi, _sens, _toDpi }) box.TextChanged += (_, _) => Recalc();
        _game.SelectionChanged += (_, _) => Recalc();
        _toGame.SelectionChanged += (_, _) => Recalc();
        Loaded += (_, _) => { Recalc(); ShowWindows(); };
    }

    private static TextBox Field(string text) => new() { Text = text, Width = 120, Padding = new Thickness(8, 5, 8, 5), FontSize = 14 };

    private FrameworkElement Labeled(string label, FrameworkElement input)
    {
        var st = new StackPanel { Margin = new Thickness(0, 0, 16, 10) };
        st.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Brush("MutedBrush"), Margin = new Thickness(0, 0, 0, 4) });
        st.Children.Add(input);
        return st;
    }

    private static double Num(TextBox b) => double.TryParse(b.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
    private static string Fmt(double v, int decimals = 2) => v.ToString("F" + decimals, CultureInfo.GetCultureInfo("pt-BR"));

    private void Recalc()
    {
        if (_game.SelectedIndex < 0 || _toGame.SelectedIndex < 0) return;
        var game = MouseAim.Games[_game.SelectedIndex];
        int dpi = (int)Num(_dpi);
        double sens = Num(_sens);
        double cm = MouseAim.CmPer360(game.Yaw, sens, dpi);

        _result.Children.Clear();
        if (cm <= 0) { _result.Children.Add(Muted("Preencha o DPI e a sensibilidade.", 12.5)); }
        else
        {
            string feel = cm >= 40 ? "mira de precisão (lenta)" : cm >= 27 ? "equilibrada" : cm >= 18 ? "rápida" : "muito rápida: difícil acertar de longe";
            _result.Children.Add(new TextBlock { Text = $"{Fmt(cm, 1)} cm de mouse por volta completa · {feel}", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 2) });
            _result.Children.Add(Muted($"eDPI {Fmt(MouseAim.EDpi(sens, dpi), 0)} (DPI × sensibilidade) · {game.Hint}", 12));
        }

        _styles.Children.Clear();
        foreach (var style in MouseAim.Styles)
        {
            double s = MouseAim.SensitivityFor(game.Yaw, style.CmPer360, dpi);
            _styles.Children.Add(Row($"{style.Name} · {style.CmPer360:0} cm por volta", dpi > 0 ? $"Use sensibilidade {Fmt(s)} no {game.Name} com {dpi} DPI. {style.Hint}" : style.Hint));
        }
        _styles.Children.Add(Muted("Dica: mude a sensibilidade só no jogo e deixe o DPI fixo (800 ou 1600). Treine uma semana antes de trocar de novo.", 11.5));

        var to = MouseAim.Games[_toGame.SelectedIndex];
        int toDpi = (int)Num(_toDpi);
        double converted = MouseAim.Convert(game, sens, dpi, to, toDpi);
        _convert.Text = converted > 0 ? $"No {to.Name} com {toDpi} DPI use sensibilidade {Fmt(converted, 3)} — a mesma mira de agora. {to.Hint}" : "Preencha o DPI.";
    }

    // ---------------------------------------------------------------- Windows

    private void ShowWindows()
    {
        _windows.Children.Clear();
        var tweak = AppServices.Tweaks.FirstOrDefault(t => t.Id == "periph-mouse-accel");
        if (tweak is null) return;
        bool? off = null;
        try { off = tweak.IsApplied(); } catch { }
        var button = SmallButton(off == true ? "Desfazer" : "Desligar agora", (_, _) =>
        {
            var outcome = MBExtreme.Core.Tweaks.TweakRunner.Run(new[] { new MBExtreme.Core.Tweaks.TweakWork(tweak, off != true) }).Single();
            SetStatus(outcome.Success ? "Pronto. Vale para os programas abertos daqui em diante." : "Não foi possível alterar: " + outcome.Error, outcome.Success ? "OkBrush" : "BadBrush");
            ShowWindows();
        }, off != true);
        _windows.Children.Add(Row(off == true ? "✓ Aceleração do mouse desligada" : "Aceleração do mouse ligada",
            off == true ? "O cursor anda sempre a mesma distância para o mesmo movimento. Pode ser desfeito aqui ou na Central Desfazer."
                        : "\"Aprimorar precisão do ponteiro\" faz movimentos rápidos irem mais longe: a mira muda conforme a velocidade da mão. Desligue para jogos de tiro.",
            button, off == true ? "MutedBrush" : "WarnBrush"));
    }

    // ---------------------------------------------------------------- taxa de atualização (Raw Input)

    private async Task MeasurePollingAsync()
    {
        _pollButton.IsEnabled = false;
        var times = new List<double>(20000);
        var clock = Stopwatch.StartNew();
        var sink = new HwndSource(new HwndSourceParameters("MB taxa do mouse") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) });
        sink.AddHook((IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == WM_INPUT && times.Count < 20000) times.Add(clock.Elapsed.TotalMilliseconds);
            return IntPtr.Zero;
        });
        var dev = new[] { new RawInputDevice { UsagePage = 1, Usage = 2, Flags = RIDEV_INPUTSINK, Target = sink.Handle } };
        if (!RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf<RawInputDevice>()))
        {
            _poll.Text = "O Windows não deixou ler o mouse agora. Feche o modo Teclado e mouse → controle e tente de novo.";
            sink.Dispose();
            _pollButton.IsEnabled = true;
            return;
        }
        try
        {
            for (int i = 3; i > 0; i--) { _poll.Text = $"Mexa o mouse em círculos rápidos... {i}"; _poll.Foreground = Brush("TextBrush"); await Task.Delay(1000); }
        }
        finally
        {
            RegisterRawInputDevices(new[] { new RawInputDevice { UsagePage = 1, Usage = 2, Flags = RIDEV_REMOVE, Target = IntPtr.Zero } }, 1, (uint)Marshal.SizeOf<RawInputDevice>());
            sink.Dispose();
            _pollButton.IsEnabled = true;
            if (AppServices.Kbm.Running) AppServices.Kbm.Start(); // devolve o mouse ao modo controle, se estava ligado
        }

        var r = MouseAim.PollingRate(times);
        if (r is null) { _poll.Text = "Quase não chegou movimento. Clique de novo e mexa o mouse sem parar."; _poll.Foreground = Brush("WarnBrush"); return; }
        var (hz, measured) = r.Value;
        string verdict = hz >= 1000 ? "ótimo para jogos de tiro." : hz >= 500 ? "bom; se o programa do mouse permitir, suba para 1000 Hz." : "baixo: a mira fica picotada. Suba para 1000 Hz no programa do mouse.";
        _poll.Text = $"{hz} Hz (medido {Fmt(measured, 0)} Hz) — {verdict}";
        _poll.Foreground = Brush(hz >= 1000 ? "OkBrush" : hz >= 500 ? "TextBrush" : "WarnBrush");
    }
}
