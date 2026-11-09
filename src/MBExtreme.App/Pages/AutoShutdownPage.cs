using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MBExtreme.Core.Tools;

namespace MBExtreme.UI.Pages;

/// <summary>Desligamento automático: programa o desligamento ou o reinício do PC.</summary>
public sealed class AutoShutdownPage : ToolPageBase
{
    private static readonly int[] Minutes = { 15, 30, 60, 120, 180, 300 };
    private static DateTime? s_when;
    private static bool s_restart;
    private static bool s_loaded;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TextBlock _countdown = new() { FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) };
    private int _mode;
    private int _time = 2;

    public AutoShutdownPage() : base("Desligamento automático",
        "Deixe o PC desligar ou reiniciar sozinho depois de um tempo, por exemplo ao terminar um download, uma renderização ou um teste longo. Você pode cancelar a qualquer momento.")
    {
        AddButton("Programar", (_, _) => Schedule());
        AddButton("Cancelar programação", (_, _) => Cancel(), false);

        Body.Children.Add(Heading("O que fazer"));
        Body.Children.Add(Choice(new[] { "Desligar", "Reiniciar" }, _mode, i => _mode = i));
        Body.Children.Add(Heading("Daqui a quanto tempo"));
        Body.Children.Add(Choice(Minutes.Select(m => m < 60 ? m + " min" : m % 60 == 0 ? m / 60 + " h" : $"{m / 60} h {m % 60} min").ToList(), _time, i => _time = i));
        Body.Children.Add(Heading("Programado"));
        Body.Children.Add(CardOf(_countdown));
        Body.Children.Add(Muted("Antes de desligar, o Windows avisa e dá a chance de salvar. Programas com documentos abertos podem impedir o desligamento."));

        _timer.Tick += (_, _) => ShowCountdown();
        Loaded += (_, _) =>
        {
            if (!s_loaded)
            {
                s_loaded = true;
                // lembra do que foi programado antes de fechar o MB Extreme (o desligamento continua valendo no Windows)
                long ticks = AppServices.Settings.ScheduledShutdownUtcTicks;
                if (ticks > 0 && new DateTime(ticks, DateTimeKind.Utc).ToLocalTime() > DateTime.Now)
                {
                    s_when = new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
                    s_restart = AppServices.Settings.ScheduledShutdownRestart;
                }
            }

            _timer.Start();
            ShowCountdown();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    private void ShowCountdown()
    {
        if (s_when is not { } when || when <= DateTime.Now)
        {
            if (s_when is not null) Remember(null, false);
            s_when = null;
            _countdown.Text = "Nada programado.";
            _countdown.Foreground = Brush("MutedBrush");
            return;
        }

        var left = when - DateTime.Now;
        _countdown.Text = $"{(s_restart ? "Reinicia" : "Desliga")} às {when:HH:mm} (faltam {(int)left.TotalHours:00}:{left.Minutes:00}:{left.Seconds:00})";
        _countdown.Foreground = Brush("TextBrush");
    }

    private void Schedule()
    {
        int seconds = Minutes[_time] * 60;
        try
        {
            if (s_when is not null) Run(PowerSchedule.CancelArguments); // troca só a programação feita por aqui
            var (ok, message) = Run(PowerSchedule.ShutdownArguments(_mode == 1, seconds));
            if (!ok) { SetStatus("Não foi possível programar: " + message, "BadBrush"); return; }
            s_when = DateTime.Now.AddSeconds(seconds);
            s_restart = _mode == 1;
            Remember(s_when, s_restart);
            SetStatus("Programado.", "OkBrush");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SetStatus("Não foi possível programar: " + ex.Message, "BadBrush");
        }

        ShowCountdown();
    }

    private void Cancel()
    {
        try
        {
            if (s_when is null)
            {
                SetStatus("O MB Extreme não programou nada. Desligamentos agendados por outros programas ou pelo Windows não são tocados.", "MutedBrush");
                return;
            }

            var (ok, message) = Run(PowerSchedule.CancelArguments);
            s_when = null;
            Remember(null, false);
            SetStatus(ok ? "Programação cancelada." : "Nada estava programado." + (string.IsNullOrWhiteSpace(message) ? "" : " " + message), ok ? "OkBrush" : "MutedBrush");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SetStatus("Não foi possível cancelar: " + ex.Message, "BadBrush");
        }

        ShowCountdown();
    }

    private static void Remember(DateTime? when, bool restart)
    {
        AppServices.Settings.ScheduledShutdownUtcTicks = when?.ToUniversalTime().Ticks ?? 0;
        AppServices.Settings.ScheduledShutdownRestart = restart;
        AppServices.Settings.Save();
    }

    private static (bool Ok, string Message) Run(IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo("shutdown.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string a in arguments) info.ArgumentList.Add(a);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Não foi possível iniciar o shutdown.exe.");
        string text = (process.StandardError.ReadToEnd() + process.StandardOutput.ReadToEnd()).Trim();
        process.WaitForExit();
        return (process.ExitCode == 0, text);
    }
}
