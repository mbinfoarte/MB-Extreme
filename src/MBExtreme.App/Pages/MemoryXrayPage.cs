using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MBExtreme.Core.Memory;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Raio-X da memória (Otimizar › Limpeza › Memória): mostra quem ocupa a RAM de verdade, separado em programas abertos, ajudantes
/// de segundo plano, serviços (cada um, mesmo dentro do svchost) e apps da Store. O seguro já vem marcado; "Liberar marcados"
/// fecha, para serviços (Manual), bloqueia apps em segundo plano e tira da inicialização, com cópia antes para a Central Desfazer.
/// </summary>
public sealed class MemoryXrayPage : ToolPageBase
{
    private const double GB = 1_073_741_824.0, MB = 1_048_576.0;

    private readonly CompactTable _table = new(new[]
    {
        new TableColumn("Nome"), new TableColumn("Tipo", 120), new TableColumn("Memória", 130, HorizontalAlignment.Right),
        new TableColumn("O que o Liberar faz", 210), new TableColumn("Situação", 190),
    });
    private readonly TextBlock _usage = new() { FontSize = 26, FontWeight = FontWeights.Bold };
    private readonly TextBlock _level = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressBar _bar = new() { Height = 12, Maximum = 100, Margin = new Thickness(0, 8, 0, 8), BorderThickness = new Thickness(0) };
    private readonly TextBlock _groups = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _result = new() { FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button _free = CompactPick.Button("Liberar marcados", primary: true);
    private readonly Button _cache = CompactPick.Button("Só esvaziar o cache");
    private readonly Button _refresh = CompactPick.Button("Atualizar");
    private readonly Dictionary<string, bool> _choice = new();
    private readonly WrapPanel _allow = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBox _allowBox = new() { Width = 220, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 8, 0) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(4) };
    private XrayReport _report = new(Array.Empty<XrayItem>());
    private int _filter;
    private int _sort = 2;
    private bool _descending = true, _busy;

    public MemoryXrayPage() : base("Memória",
        "Quem está ocupando a memória RAM de verdade: programas, segundo plano, serviços e apps da Store. O seguro já vem marcado; o que é do Windows fica protegido.")
    {
        _free.Click += async (_, _) => await FreeAsync();
        _cache.Click += async (_, _) => await CacheAsync();
        _refresh.Click += async (_, _) => await ScanAsync();
        _table.SortRequested += (c, d) => { _sort = c; _descending = d; Render(); };

        var head = new StackPanel();
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(_usage);
        top.Children.Add(_level);
        head.Children.Add(top);
        _bar.Background = Brush("Surface2Brush");
        head.Children.Add(_bar);
        head.Children.Add(_groups);
        head.Children.Add(_result);
        Body.Children.Add(CardOf(head));

        Body.Children.Add(OptimizeKit.Toolbar(
            new UIElement[] { OptimizeKit.Filters(new[] { "Tudo", "Pode liberar", "Segundo plano", "Serviços", "Apps da Store", "Programas abertos", "Protegidos" }, 0, i => { _filter = i; Render(); }) },
            new UIElement[] { _refresh, _cache, _free }));
        Body.Children.Add(_table);
        Body.Children.Add(OptimizeKit.SafetyNote(
            "Nunca mexe no que é do Windows, antivírus, drivers de vídeo e áudio, rede, antitrapaça, lojas de jogos, Discord nem no MB Extreme. Fechar um programa não " +
            "desinstala nada. Serviço em Manual, app sem segundo plano e programa fora da inicialização voltam pela Central Desfazer."));

        var allowCard = new StackPanel();
        allowCard.Children.Add(new TextBlock { Text = "Nunca mexer nestes", FontSize = 14, FontWeight = FontWeights.SemiBold });
        allowCard.Children.Add(Muted("Nome do programa (como aparece na lista) ou do serviço. Eles ficam protegidos aqui, na limpeza automática e na otimização em 1 clique.", 11.5));
        var addRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        _allowBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) AddAllow(); };
        var add = CompactPick.Button("Proteger");
        add.Click += (_, _) => AddAllow();
        addRow.Children.Add(_allowBox);
        addRow.Children.Add(add);
        allowCard.Children.Add(addRow);
        allowCard.Children.Add(_allow);
        Body.Children.Add(CardOf(allowCard));

        _timer.Tick += (_, _) => UpdateUsage();
        Loaded += async (_, _) => { _timer.Start(); RenderAllow(); await ScanAsync(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    // ---------------------------------------------------------------- leitura

    private async Task ScanAsync()
    {
        if (_busy || !OperatingSystem.IsWindows()) return;
        _busy = true;
        _refresh.IsEnabled = false;
        SetStatus("Lendo processos, serviços e apps da Store...");
        try
        {
            var allow = AppServices.Settings.MemoryAllowlist.Concat(AppServices.Settings.GameProcesses).ToArray();
            _report = await Task.Run(() => MemoryXrayProbe.Scan(allow));
            SetStatus("");
            UpdateUsage();
            Render();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            SetStatus("Não foi possível ler a memória: " + ex.Message, "WarnBrush");
        }
        finally { _busy = false; _refresh.IsEnabled = true; }
    }

    private void UpdateUsage()
    {
        var s = MemoryService.GetStatus();
        if (s.TotalBytes <= 0) return;
        double used = 100 - s.FreePercent;
        _usage.Text = $"RAM {s.UsedBytes / GB:0.0} de {s.TotalBytes / GB:0} GB ({used:0}%)";
        (string text, string brush) = used >= 90 ? ("CRÍTICO", "BadBrush") : used >= 75 ? ("ALTO", "WarnBrush") : ("OK", "OkBrush");
        _level.Text = text;
        _level.Foreground = Brush(brush);
        _bar.Value = used;
        _bar.Foreground = Brush(brush);
        _groups.Text = $"Programas abertos {_report.Bytes(XrayKind.App) / GB:0.0} GB · Segundo plano {_report.Bytes(XrayKind.Background) / GB:0.0} GB · " +
                       $"Serviços {_report.Bytes(XrayKind.Service) / GB:0.0} GB · Apps da Store {_report.Bytes(XrayKind.StoreApp) / GB:0.0} GB · " +
                       $"Windows e protegidos {_report.Bytes(XrayKind.Protected) / GB:0.0} GB";
    }

    // ---------------------------------------------------------------- lista

    private bool IsChosen(XrayItem i) => !i.Protected && (_choice.TryGetValue(i.Key, out var c) ? c : i.Preselected);

    private void Render()
    {
        IEnumerable<XrayItem> rows = _report.Items.Where(i => _filter switch
        {
            1 => !i.Protected,
            2 => i.Kind == XrayKind.Background,
            3 => i.Kind == XrayKind.Service,
            4 => i.Kind == XrayKind.StoreApp,
            5 => i.Kind == XrayKind.App,
            6 => i.Protected,
            _ => i.Bytes >= 5 * MB || !i.Protected,
        });
        Func<XrayItem, object> key = _sort switch
        {
            1 => i => KindText(i),
            2 => i => i.Bytes,
            3 => i => ActionText(i),
            4 => i => i.Reason,
            _ => i => i.Name.ToLowerInvariant(),
        };
        rows = _descending ? rows.OrderByDescending(key) : rows.OrderBy(key);
        double max = Math.Max(1, _report.Items.Count == 0 ? 1 : _report.Items.Max(i => i.Bytes));
        _table.SetRows(rows.Take(250).Select(i => Row(i, max)).ToList());
        UpdateButton();
    }

    private TableRow Row(XrayItem i, double max)
    {
        var box = new CheckBox { IsChecked = IsChosen(i), IsEnabled = !i.Protected };
        box.Click += (_, _) => { _choice[i.Key] = box.IsChecked == true; UpdateButton(); };
        string glyph = i.Kind switch { XrayKind.Service => "", XrayKind.StoreApp => "", XrayKind.App => "", XrayKind.Background => "", _ => "" };
        string brush = i.Protected ? "MutedBrush" : i.Preselected ? "OkBrush" : i.Kind == XrayKind.App ? "WarnBrush" : "MutedBrush";
        return new TableRow(box, CompactTable.Glyph(glyph, i.Protected ? "MutedBrush" : "AccentBrush"), new object[]
        {
            i.Detail.Length > 0 ? $"{i.Name}  ·  {i.Detail}" : i.Name,
            KindText(i),
            CompactTable.Meter(i.Bytes >= GB ? $"{i.Bytes / GB:0.0} GB" : $"{i.Bytes / MB:0} MB", i.Bytes / max, i.Bytes > GB ? "WarnBrush" : "AccentBrush"),
            ActionText(i),
            CompactTable.Badge(i.Reason, brush),
        }, ToolTip: i.Processes.Count > 0 ? "PID: " + string.Join(", ", i.Processes.Select(p => p.Pid).Take(12)) : i.ServiceName ?? i.PackageFamily);
    }

    private static string KindText(XrayItem i) => i.Kind switch
    {
        XrayKind.App => "Programa aberto",
        XrayKind.Background => "Segundo plano",
        XrayKind.Service => "Serviço",
        XrayKind.StoreApp => "App da Store",
        _ => "Protegido",
    };

    private static string ActionText(XrayItem i) => i.Action switch
    {
        XrayAction.Close => "Fechar agora",
        XrayAction.CloseAndStartup => "Fechar e tirar da inicialização",
        XrayAction.StopService => "Parar agora e deixar em Manual",
        XrayAction.BlockBackground => "Bloquear em segundo plano",
        _ => "—",
    };

    private List<XrayItem> Chosen() => _report.Items.Where(IsChosen).ToList();

    private void UpdateButton()
    {
        var chosen = Chosen();
        long bytes = chosen.Sum(i => i.Bytes);
        _free.IsEnabled = chosen.Count > 0 && !_busy;
        _free.Content = chosen.Count > 0 ? $"Liberar marcados ({chosen.Count} · ~{bytes / GB:0.0} GB)" : "Nada marcado";
    }

    // ---------------------------------------------------------------- ações

    private async Task FreeAsync()
    {
        var chosen = Chosen();
        if (chosen.Count == 0 || !OperatingSystem.IsWindows()) return;
        string list = string.Join("\n", chosen.OrderByDescending(i => i.Bytes).Take(14).Select(i => $"• {i.Name}: {ActionText(i).ToLowerInvariant()} ({i.Bytes / MB:0} MB)"));
        bool windows = chosen.Any(i => i.Kind == XrayKind.App || i.Processes.Any(p => p.HasWindow));
        if (MessageBox.Show($"Liberar {chosen.Count} item(ns)?\n\n{list}" + (chosen.Count > 14 ? $"\n... e mais {chosen.Count - 14}" : "") +
                (windows ? "\n\nAtenção: algum tem janela aberta. O que não foi salvo nele se perde." : "") +
                "\n\nServiços, segundo plano e inicialização voltam pela Central Desfazer.",
                "Raio-X da memória", MessageBoxButton.YesNo, windows ? MessageBoxImage.Warning : MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _busy = true;
        _free.IsEnabled = false;
        double before = 100 - MemoryService.GetStatus().FreePercent;
        var progress = new Progress<string>(n => SetStatus("Liberando: " + n));
        try
        {
            var allow = AppServices.Settings.MemoryAllowlist.ToArray();
            var result = await Task.Run(() =>
            {
                var r = MemoryXrayProbe.Free(chosen, persistent: true, progress);
                MemoryService.Clean(allow);   // e esvazia o cache que sobrou
                return r;
            });
            var after = MemoryService.GetStatus();
            double now = 100 - after.FreePercent;
            var parts = new List<string>();
            if (result.Closed > 0) parts.Add($"{result.Closed} processo(s) fechados");
            if (result.ServicesStopped > 0) parts.Add($"{result.ServicesStopped} serviço(s) parados");
            if (result.AppsBlocked > 0) parts.Add($"{result.AppsBlocked} app(s) sem segundo plano");
            if (result.StartupOff > 0) parts.Add($"{result.StartupOff} fora da inicialização");
            _result.Text = $"Antes {before:0}% → agora {now:0}% · {Math.Max(0, (before - now) / 100 * after.TotalBytes) / GB:0.0} GB liberados" +
                           (parts.Count > 0 ? " · " + string.Join(", ", parts) : "");
            _result.Foreground = Brush(now < before ? "OkBrush" : "WarnBrush");
            SetStatus(result.Failed > 0 ? $"{result.Failed} item(ns) não deram: já tinham fechado ou precisam do MB aberto como administrador." : "", result.Failed > 0 ? "WarnBrush" : "MutedBrush");
            foreach (var i in chosen) _choice.Remove(i.Key);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            SetStatus("Não foi possível liberar: " + ex.Message, "BadBrush");
        }
        finally { _busy = false; }
        await ScanAsync();
    }

    private async Task CacheAsync()
    {
        if (_busy || !OperatingSystem.IsWindows()) return;
        _busy = true;
        double before = 100 - MemoryService.GetStatus().FreePercent;
        SetStatus("Esvaziando o cache...");
        try
        {
            var allow = AppServices.Settings.MemoryAllowlist.ToArray();
            var r = await Task.Run(() => MemoryService.Clean(allow));
            double now = 100 - MemoryService.GetStatus().FreePercent;
            _result.Text = $"Cache: antes {before:0}% → agora {now:0}% · {r.FreedBytes / MB:0} MB" +
                           (now >= 85 ? ". Ainda alto: o que ocupa são os programas e serviços da lista. Marque e use \"Liberar marcados\"." : "");
            _result.Foreground = Brush(now >= 85 ? "WarnBrush" : "OkBrush");
            SetStatus("");
        }
        finally { _busy = false; }
        UpdateUsage();
    }

    // ---------------------------------------------------------------- nunca mexer

    private void AddAllow()
    {
        string name = _allowBox.Text.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.Length == 0) return;
        var list = AppServices.Settings.MemoryAllowlist;
        if (!list.Contains(name, StringComparer.OrdinalIgnoreCase)) { list.Add(name); AppServices.Settings.Save(); }
        _allowBox.Clear();
        RenderAllow();
        _ = ScanAsync();
    }

    private void RenderAllow()
    {
        _allow.Children.Clear();
        foreach (string name in AppServices.Settings.MemoryAllowlist.OrderBy(n => n, StringComparer.Create(new System.Globalization.CultureInfo("pt-BR"), true)))
        {
            var chip = CompactPick.Button(name + "  ✕");
            chip.Margin = new Thickness(0, 0, 6, 6);
            chip.ToolTip = "Tirar da proteção";
            string captured = name;
            chip.Click += (_, _) =>
            {
                AppServices.Settings.MemoryAllowlist.RemoveAll(n => n.Equals(captured, StringComparison.OrdinalIgnoreCase));
                AppServices.Settings.Save();
                RenderAllow();
                _ = ScanAsync();
            };
            _allow.Children.Add(chip);
        }
        if (_allow.Children.Count == 0) _allow.Children.Add(Muted("Nenhum por enquanto.", 11.5));
    }
}
