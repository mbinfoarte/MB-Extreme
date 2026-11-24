using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Repair;
using MBExtreme.Core.Storage;
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
    private readonly WrapPanel _driveChips = new() { Margin = new Thickness(0, 4, 0, 6) };
    private readonly System.Windows.Controls.Primitives.UniformGrid _diskHost = TwoColumns();
    private readonly StackPanel _scanHost = new();

    private static System.Windows.Controls.Primitives.UniformGrid TwoColumns() =>
        new() { Columns = 2, Margin = new Thickness(0, 0, -8, 0), VerticalAlignment = VerticalAlignment.Top };

    /// <summary>Exame rápido (só lendo): mostra o que precisa ser feito e o reparo certo de cada problema, com Executar.</summary>
    private async Task ScanAsync()
    {
        if (s_running) return;
        _scanHost.Children.Clear();
        _scanHost.Children.Add(Heading("Escaneando o Windows"));
        var line = Muted("Começando...", 12);
        _scanHost.Children.Add(line);
        _scanHost.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 6, Margin = new Thickness(0, 6, 0, 8), Foreground = Brush("AccentBrush"), Background = Brush("Surface2Brush"), BorderThickness = new Thickness(0) });
        IReadOnlyList<RepairFinding> found;
        try { found = await RepairScan.RunAsync(new Progress<string>(t => line.Text = t), CancellationToken.None); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException)
        {
            _scanHost.Children.Clear();
            SetStatus("Não foi possível escanear: " + ex.Message, "BadBrush");
            return;
        }
        _scanHost.Children.Clear();
        int problems = found.Count(f => f.Problem);
        _scanHost.Children.Add(Heading(problems == 0 ? "Escaneamento: nada para reparar" : $"Escaneamento: {problems} coisa(s) para reparar"));
        var all = RepairCatalog.All().Concat(RepairCatalog.DiskTasks(RepairCatalog.SystemDrive, true, System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)).ToList();
        var table = new MBExtreme.UI.Controls.CompactTable(new[]
        {
            new MBExtreme.UI.Controls.TableColumn("Verificação", 210), new MBExtreme.UI.Controls.TableColumn("Resultado"),
            new MBExtreme.UI.Controls.TableColumn("Reparo indicado", 250), new MBExtreme.UI.Controls.TableColumn("", 100, HorizontalAlignment.Right),
        }, checks: false);
        table.SetRows(found.Select(f =>
        {
            var task = f.TaskId is { } id ? all.FirstOrDefault(t => t.Id == id) : null;
            object action = "";
            if (f.Problem && task is not null)
            {
                var run = new Button { Content = "Executar", Style = (Style)FindResource("PrimaryButton"), Padding = new Thickness(10, 1, 10, 2), FontSize = 11.5 };
                run.Click += async (_, _) => await RunAsync(task);
                _runButtons.Add(run);
                action = run;
            }
            return new MBExtreme.UI.Controls.TableRow(null, MBExtreme.UI.Controls.CompactTable.Glyph(f.Problem ? "\uE7BA" : "\uE73E", f.Problem ? "WarnBrush" : "OkBrush"),
                new object[] { f.Title, f.Detail, f.Problem ? task?.Title ?? "—" : "Nada a fazer", action });
        }).ToList());
        _scanHost.Children.Add(table);
        SetStatus(problems == 0 ? "O Windows está em ordem pelo exame rápido." : "Execute os reparos indicados na ordem da lista.", problems == 0 ? "OkBrush" : "WarnBrush");
    }
    private readonly List<Button> _diskRunButtons = new();
    private IReadOnlyList<(string Name, string Text, bool IsSystem)> _drives = Array.Empty<(string, string, bool)>();
    private string _driveName = RepairCatalog.SystemDrive;
    private bool _drivesLoaded;

    /// <summary>Unidade a escolher ao abrir a página (vem do Gerenciador de discos).</summary>
    internal static string? PendingDrive { get; set; }

    // o estado vale para o app todo: voltar à página durante um reparo não libera um segundo
    private static bool s_running;
    private readonly TextBlock _rebootNote = new()
    {
        Text = "O Windows está esperando reiniciar para terminar uma atualização. Reinicie antes de reparar: o DISM e o SFC podem falhar ou repetir o trabalho.",
        TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4),
    };
    private static CancellationTokenSource? s_cts;

    public RepairPage() : base("Reparo do Windows",
        "Roda no próprio app os mesmos comandos que você digitaria no terminal (SFC, DISM, CHKDSK e outros) e mostra a saída ao vivo. Se não souber por onde começar, use o reparo completo recomendado.")
    {
        var full = RepairCatalog.All().First(t => t.Id == "full");
        var scanButton = AddButton("\uD83D\uDD0D  Escanear o Windows", async (_, _) => await ScanAsync());
        _runButtons.Add(scanButton);
        var fullButton = AddButton("Reparo completo recomendado", async (_, _) => await RunAsync(full), false);
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
        Body.Children.Add(_scanHost);

        // reinício pendente: reparar antes de reiniciar costuma falhar ou repetir o trabalho
        Body.Children.Add(_rebootNote);
        _rebootNote.Foreground = Brush("WarnBrush");
        _rebootNote.Visibility = Visibility.Collapsed;

        var timeouts = new[] { 5, 15, 30, 60 };
        var s = AppServices.Settings;
        var limits = Choice(timeouts.Select(m => m + " min").ToList(), Math.Max(0, Array.IndexOf(timeouts, s.RepairTimeoutMinutes)), i =>
        {
            s.RepairTimeoutMinutes = timeouts[i];
            s.Save();
        });
        Body.Children.Add(Muted("Tempo limite dos comandos comuns (se travarem, param sozinhos). O DISM, o SFC e o CHKDSK nunca são cortados no meio: se demorarem, o programa só avisa.", 11.5));
        Body.Children.Add(limits);
        Body.Children.Add(Muted("Quer só verificar sem mudar nada? Use a análise profunda do Painel inteligente.", 11.5));

        Loaded += async (_, _) =>
        {
            bool pending = await Task.Run(MBExtreme.Core.Windows.RebootState.IsPending);
            _rebootNote.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
            SetRunning(s_running);
            await LoadDrivesAsync();
        };

        Body.Children.Add(Heading("Andamento"));
        Body.Children.Add(_current);
        Body.Children.Add(_bar);
        _current.Foreground = Brush("MutedBrush");
        _log.Background = Brush("SurfaceBrush");
        _log.Foreground = Brush("TextBrush");
        _log.BorderBrush = Brush("BorderBrush");
        Body.Children.Add(_log);
        _log.Text = "A saída dos comandos aparece aqui.";

        // grupos em duas colunas; dentro de cada grupo, primeiro o que tem ordem recomendada (1º, 2º...) e depois o resto
        foreach (var byGroup in RepairCatalog.All().GroupBy(t => t.Group))
        {
            Body.Children.Add(Heading(byGroup.Key));
            if (byGroup.Key == RepairCatalog.GroupDisk) { BuildDiskSelector(); continue; }   // as tarefas de disco dependem da unidade escolhida
            var grid = TwoColumns();
            foreach (var task in byGroup.OrderBy(t => RepairScan.Order(t.Id) is int o and > 0 ? o : 100)) grid.Children.Add(TaskCard(task));
            Body.Children.Add(grid);
        }

        Body.Children.Add(Muted($"Relatórios de cada reparo ficam em {RepairRunner.LogDirectory}. O SFC e o DISM também gravam seus próprios registros: {RepairInterpreter.CbsLog} e {RepairInterpreter.DismLog}."));
    }

    // ---------------------------------------------------------------- escolha do disco

    private void BuildDiskSelector()
    {
        Body.Children.Add(Muted("Escolha qual disco verificar ou reparar. Os comandos abaixo valem só para a unidade escolhida.", 12));
        Body.Children.Add(UiTree.Free(_driveChips));
        Body.Children.Add(UiTree.Free(_diskHost));
        PaintDriveChips();
        RebuildDiskTasks();
    }

    private async Task LoadDrivesAsync()
    {
        if (!_drivesLoaded)
        {
            try
            {
                var list = await Task.Run(() => DriveMap.List(true));
                _drives = list
                    .Select(d => (d.Name, $"{d.Letter}: {(string.IsNullOrWhiteSpace(d.Label) ? "" : d.Label + " · ")}{d.TotalBytes / 1_073_741_824.0:0} GB" + (d.IsSystem ? " · Windows" : d.IsExternal ? " · externo" : ""), d.IsSystem))
                    .ToList();
                _drivesLoaded = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException)
            {
                _drives = new[] { (RepairCatalog.SystemDrive, RepairCatalog.SystemDrive + " · Windows", true) };
            }
        }

        if (PendingDrive is { } pending && _drives.Any(d => d.Name.Equals(pending, StringComparison.OrdinalIgnoreCase)))
        {
            _driveName = _drives.First(d => d.Name.Equals(pending, StringComparison.OrdinalIgnoreCase)).Name;
            RebuildDiskTasks();
        }

        PendingDrive = null;
        PaintDriveChips();
    }

    private void PaintDriveChips()
    {
        _driveChips.Children.Clear();
        var drives = _drives.Count > 0 ? _drives : new[] { (RepairCatalog.SystemDrive, RepairCatalog.SystemDrive + " · Windows", true) };
        foreach (var (name, text, _) in drives)
        {
            string drive = name;
            var chip = new Button
            {
                Content = text,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(11, 4, 11, 4),
                FontSize = 12,
                Style = (Style)FindResource(drive.Equals(_driveName, StringComparison.OrdinalIgnoreCase) ? "PrimaryButton" : "GhostButton"),
            };
            chip.Click += (_, _) =>
            {
                if (s_running) return;
                _driveName = drive;
                PaintDriveChips();
                RebuildDiskTasks();
            };
            _driveChips.Children.Add(chip);
        }
    }

    private void RebuildDiskTasks()
    {
        foreach (var button in _diskRunButtons) _runButtons.Remove(button);
        _diskRunButtons.Clear();
        _diskHost.Children.Clear();

        bool isSystem = _drives.FirstOrDefault(d => d.Name.Equals(_driveName, StringComparison.OrdinalIgnoreCase)).IsSystem
            || _driveName.Equals(RepairCatalog.SystemDrive, StringComparison.OrdinalIgnoreCase);
        string language = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        foreach (var task in RepairCatalog.DiskTasks(_driveName, isSystem, language))
        {
            int before = _runButtons.Count;
            _diskHost.Children.Add(TaskCard(task));
            for (int i = before; i < _runButtons.Count; i++) _diskRunButtons.Add(_runButtons[i]);
        }

        foreach (var button in _diskRunButtons) button.IsEnabled = !s_running;
    }

    /// <summary>Linha fina: número da ordem recomendada, título, duração e Executar à direita (descrição e comandos no balão).</summary>
    private UIElement TaskCard(RepairTask task)
    {
        var dock = new DockPanel();
        int order = RepairScan.Order(task.Id);
        var badge = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(1.3), BorderBrush = Brush(order > 0 ? "AccentBrush" : "BorderBrush"),
            Child = new TextBlock
            {
                Text = order > 0 ? order + "º" : "\uE90F", FontFamily = order > 0 ? new System.Windows.Media.FontFamily("Segoe UI") : new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = order > 0 ? 10.5 : 10, FontWeight = FontWeights.Bold, Foreground = Brush(order > 0 ? "AccentBrush" : "MutedBrush"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        DockPanel.SetDock(badge, Dock.Left);
        dock.Children.Add(badge);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var run = new Button { Content = "Executar", Style = (Style)FindResource("PrimaryButton"), Padding = new Thickness(10, 1, 10, 2), FontSize = 11.5, Margin = new Thickness(6, 0, 0, 0) };
        run.Click += async (_, _) => await RunAsync(task);
        _runButtons.Add(run);
        var copy = new Button { Content = "\uE8C8", FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), Style = (Style)FindResource("GhostButton"), Padding = new Thickness(7, 2, 7, 2), FontSize = 11, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Copiar os comandos" };
        copy.Click += (_, _) => { if (TryCopy(task.ManualCommands)) SetStatus("Comandos copiados. Cole em um terminal aberto como administrador.", "OkBrush"); };
        buttons.Children.Add(copy);
        buttons.Children.Add(run);
        DockPanel.SetDock(buttons, Dock.Right);
        dock.Children.Add(buttons);

        var extra = new List<string> { task.Duration };
        if (task.NeedsInternet) extra.Add("internet");
        if (task.NeedsRestart) extra.Add("reinicia");
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = task.Title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = string.Join(" · ", extra), FontSize = 11, Foreground = Brush("MutedBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
        dock.Children.Add(text);
        return new Border
        {
            Child = dock, MinHeight = 44, Padding = new Thickness(10, 5, 8, 5), Margin = new Thickness(0, 0, 8, 6), CornerRadius = new CornerRadius(9),
            Background = Brush("SurfaceBrush"), BorderBrush = Brush(order > 0 ? "AccentBrush" : "BorderBrush"), BorderThickness = new Thickness(order > 0 ? 1 : 1),
            ToolTip = task.Description + "\nComando: " + task.ManualCommands.Replace(Environment.NewLine, "  |  "),
        };
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

            var result = await Task.Run(() => new RepairRunner { GenericTimeoutMinutes = AppServices.Settings.RepairTimeoutMinutes }.RunAsync(task, progress, token));
            MBExtreme.Core.Operations.OperationJournal.Shared.Record(
                new MBExtreme.Core.Operations.OperationInfo("repair." + task.Id, MBExtreme.Core.Operations.OperationKind.Repair, "Reparo: " + task.Title, NeedsAdmin: true),
                new MBExtreme.Core.Operations.OperationResult(result.Cancelled ? MBExtreme.Core.Operations.OperationState.Cancelled
                    : result.Success ? MBExtreme.Core.Operations.OperationState.Succeeded : MBExtreme.Core.Operations.OperationState.Failed, result.Summary), DateTimeOffset.Now);
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
