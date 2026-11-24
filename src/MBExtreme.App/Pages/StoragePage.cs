using System.IO;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Progress;
using MBExtreme.Core.Storage;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>Espaço, saúde e otimização de SSD e HD.</summary>
public sealed class StoragePage : ToolPageBase
{
    public StoragePage() : base("Armazenamento",
        "Espaço usado, saúde dos discos e otimização com a ferramenta do Windows (TRIM em SSD, desfragmentação em HD). Temperatura e desgaste dependem do que o disco informa.")
    {
        AddButton("Atualizar", async (_, _) => await RunOnce(LoadAsync), false);
        AddButton("Ajustes de TRIM e hibernação", (_, _) => MainWindow.Current?.Go("tweaks-storage"), false);
        Loaded += async (_, _) => await RunOnce(LoadAsync);
    }

    private IReadOnlySet<char> _protected = new HashSet<char>();
    private OptimizeTask? _optimize;
    private bool? _trim;
    private static string SystemDrive => (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\');

    private async Task LoadAsync()
    {
        SetStatus("Lendo os discos...");
        Body.Children.Clear();
        IReadOnlyList<VolumeInfo> volumes;
        IReadOnlyList<DiskInfo> disks = Array.Empty<DiskInfo>();
        string? diskProblem = null;
        try
        {
            volumes = await Task.Run(StorageService.Volumes);
            var maintenance = await Task.Run(() => (Protected: DiskMaintenance.ProtectedDrives(), Optimize: DiskMaintenance.ScheduledOptimize(),
                Trim: DiskMaintenance.TrimEnabled()));
            _protected = maintenance.Protected;
            _optimize = maintenance.Optimize;
            _trim = maintenance.Trim;
            try { disks = await Task.Run(() => StorageService.WithSmart(StorageService.PhysicalDisks())); }
            catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException)
            {
                diskProblem = ex.Message;
            }
        }
        catch (IOException ex)
        {
            SetStatus("Não foi possível ler os volumes: " + ex.Message, "BadBrush");
            return;
        }

        SetStatus("");
        var history = disks.Count > 0 ? await Task.Run(() => new DiskHistory().Record(disks, DateTimeOffset.Now)) : Array.Empty<DiskSnapshot>();

        // ---- números no topo
        var grades = disks.Select(d => DiskHealth.Grade(d).Grade).ToList();
        var worst = grades.Count == 0 ? DiskGrade.Unknown : grades.Contains(DiskGrade.Bad) ? DiskGrade.Bad : grades.Contains(DiskGrade.Caution) ? DiskGrade.Caution
            : grades.All(g => g == DiskGrade.Unknown) ? DiskGrade.Unknown : DiskGrade.Good;
        var health = OptimizeKit.Stat("Saúde dos discos", DiskHealth.GradeText(worst), $"{disks.Count} disco(s) físico(s)", DiskHealth.GradeBrush(worst));
        health.Value.Foreground = Brush(DiskHealth.GradeBrush(worst));
        var trim = OptimizeKit.Stat("TRIM dos SSDs", _trim switch { true => "Ligado", false => "Desligado", null => "—" },
            _trim == false ? "ligue em Ajustes de armazenamento" : "mantém o SSD rápido", _trim == false ? "BadBrush" : "MutedBrush");
        var auto = OptimizeKit.Stat("Otimização automática", _optimize is null ? "—" : _optimize.Enabled ? "Ligada" : "Desligada",
            _optimize?.LastRun is { } last ? $"última: {last.ToLocalTime():dd/MM HH:mm}" : "TRIM no SSD, desfragmenta só HD", _optimize?.Enabled == false ? "WarnBrush" : "MutedBrush");
        long free = volumes.Sum(v => v.FreeBytes), total = volumes.Sum(v => v.TotalBytes);
        var space = OptimizeKit.Stat("Espaço livre", $"{free / 1e9:0} GB", $"de {total / 1e9:0} GB em {volumes.Count} unidade(s)");
        Body.Children.Add(OptimizeKit.StatRow(health.Card, trim.Card, auto.Card, space.Card));

        // ---- discos físicos (estilo CrystalDiskInfo)
        Body.Children.Add(Heading("Discos físicos"));
        if (diskProblem is not null) Body.Children.Add(Muted("Não foi possível ler a saúde dos discos: " + diskProblem));
        var diskTable = new CompactTable(new[]
        {
            new TableColumn("Modelo"), new TableColumn("Tipo", 90), new TableColumn("Capacidade", 90, HorizontalAlignment.Right),
            new TableColumn("Temp.", 64, HorizontalAlignment.Right), new TableColumn("Horas ligado", 96, HorizontalAlignment.Right),
            new TableColumn("Desgaste", 76, HorizontalAlignment.Right), new TableColumn("Setores ruins", 104, HorizontalAlignment.Right), new TableColumn("Saúde", 96),
        }, checks: false);
        diskTable.SetRows(disks.Select(d =>
        {
            var (grade, reason) = DiskHealth.Grade(d);
            string bad = d.Reallocated is null && d.Pending is null && d.Uncorrectable is null ? "—" : $"{d.Reallocated ?? 0} / {d.Pending ?? 0} / {d.Uncorrectable ?? 0}";
            string tip = $"{d.Name}\nSérie: {(d.Serial.Length > 0 ? d.Serial : "—")} · Firmware: {(d.Firmware.Length > 0 ? d.Firmware : "—")}" +
                         (d.PowerCycles is { } c ? $" · {c:N0} vezes ligado" : "") +
                         "\nSetores ruins: realocados / pendentes / incorrigíveis" + $"\n{DiskHealth.GradeText(grade)}: {reason}" +
                         (DiskHistory.Trend(history, d.Name) is { } trend ? "\nEvolução: " + trend : "");
            return new TableRow(null, CompactTable.Glyph("\uEDA2", DiskHealth.GradeBrush(grade)), new object[]
            {
                d.Name, DiskHealth.Kind(d), $"{d.SizeBytes / 1e9:0} GB",
                d.Temperature is { } t && t > 0 ? $"{t:0} °C" : "—",
                d.PowerOnHours is { } h ? $"{h:N0} h" : "—",
                d.WearPercent is { } w ? $"{w:0}%" : "—",
                bad,
                CompactTable.Badge(DiskHealth.GradeText(grade), DiskHealth.GradeBrush(grade)),
            }, ToolTip: tip);
        }).ToList());
        Body.Children.Add(diskTable);
        if (disks.Count == 0 && diskProblem is null) Body.Children.Add(Muted("Nenhum disco físico informado pelo Windows."));

        // ---- unidades (C:, D:...) com as ações de cada uma
        Body.Children.Add(Heading("Unidades"));
        var volumeTable = new CompactTable(new[]
        {
            new TableColumn("Unidade"), new TableColumn("Formato", 70), new TableColumn("Usado", 120, HorizontalAlignment.Right),
            new TableColumn("Livre", 90, HorizontalAlignment.Right), new TableColumn("Proteção", 110), new TableColumn("Ações", 330, HorizontalAlignment.Right),
        }, checks: false);
        volumeTable.SetRows(volumes.Select(v =>
        {
            bool protectedDrive = v.Name.Length > 0 && _protected.Contains(char.ToUpperInvariant(v.Name[0]));
            string name = v.Name;
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(Mini("Otimizar", async () => await OptimizeAsync(name), "TRIM no SSD, desfragmentação no HD (o Windows escolhe)"));
            actions.Children.Add(Mini("Verificar erros", async () => await CheckAsync(name), "Procura erros no sistema de arquivos sem desligar o PC (chkdsk /scan)"));
            actions.Children.Add(Mini("Velocidade", async () => await BenchAsync(name), "Mede leitura e gravação com a ferramenta do Windows"));
            return new TableRow(null, CompactTable.Glyph("\uE8B7"), new object[]
            {
                $"{v.Name} {v.Label}".Trim(), v.Format,
                CompactTable.Meter($"{v.UsedPercent:0}%", v.UsedPercent / 100, v.UsedPercent > 90 ? "BadBrush" : v.UsedPercent > 75 ? "WarnBrush" : "AccentBrush"),
                $"{v.FreeBytes / 1e9:0.0} GB",
                CompactTable.Badge(protectedDrive ? "Restauração ligada" : "Sem restauração",
                    protectedDrive ? "OkBrush" : v.Name.StartsWith(SystemDrive, StringComparison.OrdinalIgnoreCase) ? "WarnBrush" : "MutedBrush"),
                actions,
            });
        }).ToList());
        Body.Children.Add(volumeTable);
        Body.Children.Add(UiTree.Free(_bench));
    }

    private readonly StackPanel _bench = new() { Margin = new Thickness(0, 10, 0, 0) };

    private Button Mini(string text, Func<Task> click, string tip)
    {
        var b = new Button { Content = text, Style = (Style)FindResource("GhostButton"), Padding = new Thickness(9, 1, 9, 2), FontSize = 11.5, Margin = new Thickness(6, 0, 0, 0), ToolTip = tip };
        b.Click += async (_, _) => await click();
        return b;
    }

    /// <summary>Verificação de erros sem desligar o PC (chkdsk /scan): o Windows lê o sistema de arquivos e só avisa.</summary>
    private async Task CheckAsync(string drive)
    {
        if (!StorageService.IsValidDrive(drive)) return;
        ShowProgress("Verificando erros em " + drive, "Verificação");
        IProgress<ProgressEvent> progress = new Progress<ProgressEvent>(Progress.Report);
        progress.Report(new ProgressEvent("Verificação", 10, "O Windows está lendo o sistema de arquivos (alguns minutos)"));
        var (ok, tail) = await Task.Run(() => StorageService.ScanErrors(drive));
        progress.Report(new ProgressEvent("Verificação", 100, "Concluído"));
        Progress.Finish(ok ? "Nenhum erro encontrado" : "O Windows encontrou algo para corrigir", ok,
            ok ? "O sistema de arquivos está em ordem." : "Abra Reparo do Windows › CHKDSK para corrigir no próximo reinício.\n" + tail);
    }

    /// <summary>Teste de velocidade com o WinSAT: leitura e gravação sequencial e aleatória 4K.</summary>
    private async Task BenchAsync(string drive)
    {
        if (!StorageService.IsValidDrive(drive)) return;
        var tests = DiskBench.Tests(drive[0]);
        _bench.Children.Clear();
        _bench.Children.Add(Heading($"Velocidade de {drive.TrimEnd('\\')}"));
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = tests.Count, Margin = new Thickness(0, 0, -10, 0) };
        var cells = tests.Select(t => OptimizeKit.Stat(t.Title, "…", "medindo", "MutedBrush")).ToList();
        foreach (var c in cells) grid.Children.Add(c.Card);
        _bench.Children.Add(grid);
        SetStatus("Medindo a velocidade (cerca de 1 minuto; evite usar o disco agora)...");
        for (int i = 0; i < tests.Count; i++)
        {
            var args = tests[i].Args;
            double? mbps = await Task.Run(() => DiskBench.Measure(args));
            cells[i].Value.Text = mbps is { } m ? $"{m:0} MB/s" : "—";
            cells[i].Detail.Text = mbps is null ? "o Windows não mediu" : "pelo WinSAT do Windows";
        }
        SetStatus("Velocidade medida. Em SSD NVMe a leitura sequencial passa de 2000 MB/s; em SSD SATA, perto de 500; em HD, 100 a 200.", "OkBrush");
    }

    private async Task OptimizeAsync(string drive)
    {
        ShowProgress("Otimizando " + drive, "Otimização");
        IProgress<ProgressEvent> progress = new Progress<ProgressEvent>(Progress.Report);
        progress.Report(new ProgressEvent("Otimização", 10, "O Windows está otimizando a unidade (pode demorar em HD)"));
        try
        {
            var (ok, message) = await Task.Run(() => StorageService.Optimize(drive));
            progress.Report(new ProgressEvent("Otimização", 100, "Concluído"));
            Progress.Finish(ok ? "Unidade otimizada" : "Não foi possível otimizar", ok, message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Progress.Finish("Não foi possível otimizar", false, ex.Message);
        }
    }

    protected override async void OnProgressClosed() => await LoadAsync();
}
