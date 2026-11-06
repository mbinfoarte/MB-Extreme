using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Latency;
using MBExtreme.Core.Tweaks;
using MBExtreme.Core.Windows;

namespace MBExtreme.UI.Pages;

/// <summary>Diagnóstico de latência: encontra o driver que causa micro-travadas medindo DPC e ISR do sistema.</summary>
public sealed class LatencyPage : ToolPageBase
{
    private static readonly int[] Seconds = { 30, 60, 120, 300 };
    private int _secondsIndex = 1;
    private readonly Button _start;
    private readonly TextBlock _live = new() { Margin = new Thickness(0, 10, 0, 0) };
    private readonly StackPanel _results = new();
    private readonly StackPanel _settings = new();
    private readonly GameSettingsService _gameSettings = new(AppServices.TweakStore);

    private static readonly string[] LatencyTweaks =
    {
        "power-min-cpu", "power-core-parking", "power-pcie-aspm", "power-usb-suspend",
        "game-dvr", "game-mmcss", "perf-priority-separation", "net-throttling", "periph-mouse-accel",
    };

    private sealed record TweakState(Tweak Tweak, bool? Applied);
    private sealed record SettingsSnapshot(string? PlanName, bool GameMode, bool? Hags, IReadOnlyList<TweakState> Tweaks, bool? VbsOff);

    public LatencyPage() : base("Diagnóstico de latência",
        "Micro-travadas, áudio estourando e quedas de quadro quase sempre vêm de um driver que ocupa a CPU por tempo demais (rotinas DPC/ISR). Esta página mede o sistema por alguns minutos e mostra qual driver é o pior. Só leitura: nada é alterado. Enquanto mede, use o PC normalmente ou jogue.")
    {
        _start = AddButton("Iniciar medição", async (_, _) => await RunAsync());
        AddButton("Atualizar configurações", (_, _) => _ = LoadSettingsAsync(), false);
        Body.Children.Add(Heading("Configurações que afetam a latência"));
        Body.Children.Add(Muted("Estado atual de cada ajuste do Windows que influencia a latência. Os que o MB Extreme altera guardam o valor original e podem ser desfeitos aqui mesmo."));
        Body.Children.Add(_settings);
        Body.Children.Add(Heading("Medição"));
        Body.Children.Add(Heading("Duração"));
        Body.Children.Add(Choice(Seconds.Select(s => s < 60 ? s + " s" : s / 60 + " min").ToList(), _secondsIndex, i => _secondsIndex = i));
        Body.Children.Add(_live);
        Body.Children.Add(_results);

        double? timer = LatencyMonitor.TimerResolutionMilliseconds();
        if (timer is { } t)
            Body.Children.Add(Row("Resolução do temporizador do Windows",
                $"{t:0.0##} ms agora. Programas e jogos podem pedir uma resolução mais fina (até 0,5 ms) e isso aumenta o consumo de energia. O MB Extreme não altera este valor: testes e a documentação da Microsoft não mostram ganho comprovado em mexer nisso."));

        Loaded += (_, _) => _ = LoadSettingsAsync();
    }

    private int _generation;

    private async Task LoadSettingsAsync()
    {
        int generation = ++_generation;
        _settings.Children.Clear();
        _settings.Children.Add(Muted("Lendo as configurações..."));
        try
        {
            var snapshot = await Task.Run(ReadSettings);
            if (generation == _generation) ShowSettings(snapshot);
        }
        catch (Exception ex)
        {
            _settings.Children.Clear();
            _settings.Children.Add(Row("Não foi possível ler as configurações", ex.Message, null, "BadBrush"));
        }
    }

    private SettingsSnapshot ReadSettings()
    {
        string? plan = null;
        try { plan = _gameSettings.PowerPlans().FirstOrDefault(p => p.Active)?.Name; } catch { }

        var states = new List<TweakState>();
        foreach (string id in LatencyTweaks)
        {
            var tweak = AppServices.Tweaks.FirstOrDefault(t => t.Id == id);
            if (tweak is null) continue;
            bool? applied = null;
            try { applied = tweak.IsApplied(); } catch { }
            states.Add(new TweakState(tweak, applied));
        }

        bool? vbsOff = null;
        var vbs = AppServices.Tweaks.FirstOrDefault(t => t.Id == "perf-vbs-off");
        if (vbs is not null) { try { vbsOff = vbs.IsApplied(); } catch { } }

        return new SettingsSnapshot(plan, _gameSettings.GameModeEnabled(), _gameSettings.HagsEnabled(), states, vbsOff);
    }

    private void ShowSettings(SettingsSnapshot s)
    {
        _settings.Children.Clear();

        _settings.Children.Add(Row($"Plano de energia: {s.PlanName ?? "não identificado"}",
            "Planos econômicos reduzem o clock da CPU e adiam a resposta a cada ação. Para jogar, use Alto desempenho ou Desempenho máximo.",
            SmallButton("Abrir Jogos e energia", (_, _) => MainWindow.Current?.Go("gamesettings"))));

        _settings.Children.Add(Row($"Modo de Jogo: {(s.GameMode ? "ligado" : "desligado")}",
            "Faz o Windows priorizar o jogo e adiar tarefas em segundo plano. Recomendado: ligado.",
            SmallButton(s.GameMode ? "Desligar" : "Ligar", (_, _) => ApplySetting(() => _gameSettings.SetGameMode(!s.GameMode), "gamesettings.gamemode", s.GameMode ? "Desligar o Modo de Jogo" : "Ligar o Modo de Jogo")),
            s.GameMode ? "OkBrush" : "WarnBrush"));

        string hags = s.Hags switch { true => "ligado", false => "desligado", null => "padrão do Windows" };
        _settings.Children.Add(Row($"Agendamento de GPU acelerado (HAGS): {hags}",
            "Pode reduzir a latência de quadro em GPUs recentes (com Frame Generation exige ligado) e piorar em outras. Exige reiniciar o PC. Teste e compare com o Antes e depois.",
            SmallButton(s.Hags == true ? "Desligar" : "Ligar", (_, _) => ApplySetting(() => _gameSettings.SetHags(s.Hags != true), "gamesettings.hags", s.Hags == true ? "Desligar o agendamento de GPU (HAGS)" : "Ligar o agendamento de GPU (HAGS)"))));

        foreach (var state in s.Tweaks)
        {
            var tweak = state.Tweak;
            bool on = state.Applied == true;
            string label = state.Applied switch { true => "aplicado", false => "não aplicado", null => "indisponível" };
            string detail = tweak.Description + (string.IsNullOrWhiteSpace(tweak.Effect) ? "" : "\n" + tweak.Effect);
            UIElement? button = state.Applied is null ? null
                : SmallButton(on ? "Desfazer" : "Aplicar", (_, _) => ApplyTweak(tweak, apply: !on));
            _settings.Children.Add(Row($"{tweak.Title}: {label}", detail, button, on ? "OkBrush" : "MutedBrush"));
        }

        if (s.VbsOff is not null)
            _settings.Children.Add(Row($"Integridade de memória (VBS): {(s.VbsOff == true ? "desligada" : "ligada")}",
                "Pode custar de 5% a 10% de desempenho em jogos, mas protege contra malware em nível de kernel. Por ser risco alto, a alteração fica em Ajustes de desempenho.",
                SmallButton("Ver ajuste", (_, _) => MainWindow.Current?.Go("tweaks-perf")), "WarnBrush"));
    }

    private void ApplySetting(Action change, string id, string title)
    {
        const string hint = "Para desfazer: \"Restaurar originais\" em Jogos e energia.";
        try
        {
            change();
            MBExtreme.Core.Operations.OperationJournal.Shared.RecordSetting(id, title, true, hint);
            SetStatus("Configuração alterada. Alguns ajustes só valem depois de reiniciar.", "OkBrush");
        }
        catch (Exception ex)
        {
            MBExtreme.Core.Operations.OperationJournal.Shared.RecordSetting(id, title, false, hint, ex.Message);
            SetStatus("Não foi possível alterar: " + ex.Message, "BadBrush");
        }
        _ = LoadSettingsAsync();
    }

    /// <summary>Pelo mesmo caminho das páginas de ajustes: entra no diário e na Central Desfazer.</summary>
    private void ApplyTweak(MBExtreme.Core.Tweaks.Tweak tweak, bool apply)
    {
        var outcome = MBExtreme.Core.Tweaks.TweakRunner.Run(new[] { new MBExtreme.Core.Tweaks.TweakWork(tweak, apply) }).Single();
        SetStatus(outcome.Success ? "Configuração alterada. Alguns ajustes só valem depois de reiniciar." : "Não foi possível alterar: " + outcome.Error,
            outcome.Success ? "OkBrush" : "BadBrush");
        _ = LoadSettingsAsync();
    }

    private async Task RunAsync()
    {
        _start.IsEnabled = false;
        _results.Children.Clear();
        SetStatus("Medindo... clique em Parar na janela de progresso ou aguarde o fim.");
        ShowProgress("Diagnóstico de latência", "Medição");
        var token = Cts!.Token;
        var duration = TimeSpan.FromSeconds(Seconds[_secondsIndex]);
        try
        {
            void OnTick(TimeSpan elapsed, long count)
            {
                var message = new MBExtreme.Core.Progress.ProgressEvent("Medição", 100 * elapsed.TotalSeconds / duration.TotalSeconds,
                    $"{elapsed:mm\\:ss} · {count:N0} eventos medidos");
                Dispatcher.BeginInvoke(new Action(() => Progress.Report(message)));
            }

            var (top, events) = await Task.Run(() =>
            {
                var list = LatencyMonitor.Run(duration, OnTick, token, out long total);
                return (list, total);
            });

            Progress.Finish("Medição concluída", true, $"{events:N0} eventos analisados.");
            Show(top);
        }
        catch (OperationCanceledException)
        {
            Progress.Finish("Interrompido.", false);
        }
        catch (Exception ex)
        {
            Progress.Finish("Não foi possível medir", false,
                "O Windows só permite uma medição de kernel por vez (feche outros medidores, como o LatencyMon) e ela exige administrador. " + ex.Message);
        }
        finally
        {
            _start.IsEnabled = true;
        }
    }

    private void Show(IReadOnlyList<DriverLatency> top)
    {
        _results.Children.Clear();
        if (top.Count == 0)
        {
            _results.Children.Add(Muted("Nenhum evento registrado."));
            return;
        }

        var worst = top[0];
        string verdict = DpcIsrAggregator.Verdict(worst.MaxMicroseconds);
        SetStatus($"Pior pico: {worst.Module} com {worst.MaxMicroseconds:0} µs ({verdict}). Referência: abaixo de 1000 µs é bom.",
            verdict is "ótimo" or "bom" ? "OkBrush" : "WarnBrush");

        _results.Children.Add(Heading("Drivers com os maiores picos"));
        foreach (var d in top)
        {
            string v = DpcIsrAggregator.Verdict(d.MaxMicroseconds);
            string brush = v switch { "ótimo" => "OkBrush", "bom" => "OkBrush", "atenção" => "WarnBrush", _ => "BadBrush" };
            string detail = $"{d.Kind} · pico {d.MaxMicroseconds:0} µs · média {d.AverageMicroseconds:0.0} µs · {d.Count:N0} chamadas · {d.TotalMilliseconds:0.0} ms no total";
            if (v is "atenção" or "ruim") detail += "\n→ " + DpcIsrAggregator.Advice(d.Module);
            _results.Children.Add(Row($"{d.Module} ({v})", detail, null, brush));
        }
    }
}
