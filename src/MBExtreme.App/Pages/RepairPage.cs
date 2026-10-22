using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Repair;
using MBExtreme.Core.Windows;

namespace MBExtreme.UI.Pages;

/// <summary>Reparo do Windows: roda SFC, DISM, CHKDSK e outras correções mostrando a saída ao vivo.</summary>
public sealed class RepairPage : ToolPageBase
{
    private const int MaxLogLength = 400_000;

    private readonly TextBox _log = new()
    {
        IsReadOnly = true,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 12,
        Height = 280,
        TextWrapping = TextWrapping.NoWrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        Margin = new Thickness(0, 8, 0, 0),
    };

    private readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 100, Height = 8, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _current = new() { Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly CheckBox _restorePoint;
    private readonly List<Button> _runButtons = new();
    private Button? _cancel;
    // o estado vale para o app todo: voltar à página durante um reparo não libera um segundo
    private static bool s_running;
    private static CancellationTokenSource? s_cts;

    public RepairPage() : base("Reparo do Windows",
        "Roda no próprio app os mesmos comandos que você digitaria no terminal (SFC, DISM, CHKDSK e outros) e mostra a saída ao vivo. Se não souber por onde começar, use o reparo completo recomendado.")
    {
        var full = RepairCatalog.All().First(t => t.Id == "full");
        var fullButton = AddButton("Reparo completo recomendado", async (_, _) => await RunAsync(full));
        _runButtons.Add(fullButton);
        _cancel = AddButton("Cancelar", (_, _) => s_cts?.Cancel(), false);
        _cancel.Visibility = Visibility.Collapsed;
        AddButton("Abrir terminal", (_, _) => OpenTerminal(), false);
        AddButton("Abrir relatórios salvos", (_, _) => OpenLogs(), false);

        _restorePoint = new CheckBox
        {
            Content = "Criar um ponto de restauração antes dos reparos que alteram o sistema (recomendado)",
            IsChecked = true,
            Foreground = Brush("TextBrush"),
            Margin = new Thickness(0, 4, 0, 0),
        };
        Body.Children.Add(_restorePoint);
        Loaded += (_, _) => SetRunning(s_running);

        Body.Children.Add(Heading("Andamento"));
        Body.Children.Add(_current);
        Body.Children.Add(_bar);
        _current.Foreground = Brush("MutedBrush");
        _log.Background = Brush("SurfaceBrush");
        _log.Foreground = Brush("TextBrush");
        _log.BorderBrush = Brush("BorderBrush");
        Body.Children.Add(_log);
        _log.Text = "A saída dos comandos aparece aqui.";

        string? group = null;
        foreach (var task in RepairCatalog.All())
        {
            if (task.Group != group)
            {
                group = task.Group;
                Body.Children.Add(Heading(group));
            }
            Body.Children.Add(TaskCard(task));
        }

        Body.Children.Add(Muted($"Relatórios de cada reparo ficam em {RepairRunner.LogDirectory}. O SFC e o DISM também gravam seus próprios registros: {RepairInterpreter.CbsLog} e {RepairInterpreter.DismLog}."));
    }

    private UIElement TaskCard(RepairTask task)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var run = SmallButton("Executar", async (_, _) => await RunAsync(task), true);
        _runButtons.Add(run);
        buttons.Children.Add(run);
        buttons.Children.Add(SmallButton("Copiar comandos", (_, _) =>
        {
            if (TryCopy(task.ManualCommands)) SetStatus("Comandos copiados. Cole em um terminal aberto como administrador.", "OkBrush");
        }));

        var extra = new List<string> { "Duração: " + task.Duration };
        if (task.NeedsInternet) extra.Add("precisa de internet");
        if (task.NeedsRestart) extra.Add("exige reiniciar o PC");
        string detail = task.Description + "\n" + string.Join(" · ", extra) + "\nComando: " + task.ManualCommands.Replace(Environment.NewLine, "  |  ");
        return Row(task.Title, detail, buttons);
    }

    private void Append(string text)
    {
        if (_log.Text.Length > MaxLogLength) _log.Text = _log.Text[^(MaxLogLength / 2)..];
        _log.AppendText(text + Environment.NewLine);
        _log.ScrollToEnd();
    }

    private void SetRunning(bool running)
    {
        s_running = running;
        foreach (var button in _runButtons) button.IsEnabled = !running;
        if (_cancel is not null) _cancel.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task RunAsync(RepairTask task)
    {
        if (s_running) return;

        if (task.NeedsRestart || task.Caution is not null || task.Id is "icons")
        {
            string warn = task.Id == "icons"
                ? "A barra de tarefas e a área de trabalho vão piscar por alguns segundos."
                : "Depois deste reparo será preciso reiniciar o PC.";
            if (task.Caution is not null) warn += "\n\n" + task.Caution;
            if (MessageBox.Show($"{task.Title}\n\n{warn}\n\nContinuar?", "Reparo do Windows",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        }

        SetRunning(true);
        _log.Clear();
        _bar.Value = 0;
        SetStatus($"Executando: {task.Title}", "MutedBrush");
        s_cts = new CancellationTokenSource();
        var token = s_cts.Token;

        try
        {
            if (task.AdvisesRestorePoint && _restorePoint.IsChecked == true)
            {
                _current.Text = "Criando ponto de restauração...";
                Append("▶ Criando ponto de restauração");
                var (ok, error) = await Task.Run(() => RestorePointService.TryCreate("MB Extreme - " + task.Title));
                Append(ok ? "  ✔ Ponto de restauração criado." : "  ⚠ Não foi possível criar o ponto de restauração: " + error + " O reparo continua.");
            }

            var progress = new Progress<RepairUpdate>(update =>
            {
                _bar.Value = update.OverallPercent;
                _current.Text = update.Message;
                if (update.IsLog) Append(update.Message);
            });

            var result = await Task.Run(() => new RepairRunner().RunAsync(task, progress, token));
            _bar.Value = result.Cancelled ? _bar.Value : 100;
            _current.Text = result.Summary;
            SetStatus(result.Summary, result.Cancelled ? "WarnBrush" : result.Success ? "OkBrush" : "BadBrush");

            if (!result.Success && !result.Cancelled)
            {
                foreach (var step in result.Steps.Where(s => !s.Ok))
                    Append($"⚠ {step.Title}: {step.Verdict}");
            }
        }
        catch (Exception ex)
        {
            SetStatus("Não foi possível executar o reparo: " + ex.Message, "BadBrush");
            Append("✖ " + ex.Message);
        }
        finally
        {
            SetRunning(false);
        }
    }

    private void OpenTerminal()
    {
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", "/k title MB Extreme - Terminal") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SetStatus("Não foi possível abrir o terminal: " + ex.Message, "BadBrush");
        }
    }

    private void OpenLogs()
    {
        try
        {
            System.IO.Directory.CreateDirectory(RepairRunner.LogDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{RepairRunner.LogDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.IOException)
        {
            SetStatus("Não foi possível abrir a pasta: " + ex.Message, "BadBrush");
        }
    }
}
