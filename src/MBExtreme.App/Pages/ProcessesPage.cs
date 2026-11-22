using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MBExtreme.Core.Tools;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Processos abertos numa lista fina em colunas, agrupados por programa (as 20 abas do Chrome viram uma linha). Ajudantes de fundo
/// sem uso (atualizadores, widgets, sincronização) já vêm marcados; "Otimizar processos" fecha os marcados. O que é do Windows,
/// segurança, drivers, antitrapaça e lojas de jogos fica protegido.
/// </summary>
public sealed class ProcessesPage : ToolPageBase
{
    private sealed record Group(string Name, IReadOnlyList<ProcessEntry> Items, double Cpu, long Memory, bool HasWindow, string? Title);

    private readonly ProcessList _list = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly CompactTable _table = new(new[]
    {
        new TableColumn("Programa"), new TableColumn("Tipo", 120), new TableColumn("Processos", 80, HorizontalAlignment.Right),
        new TableColumn("Processador", 100, HorizontalAlignment.Right), new TableColumn("Memória", 100, HorizontalAlignment.Right), new TableColumn("Situação", 150),
    });
    private readonly (Border Card, TextBlock Value, TextBlock Detail) _count = OptimizeKit.Stat("Processos abertos agora");
    private readonly (Border Card, TextBlock Value, TextBlock Detail) _memory = OptimizeKit.Stat("Memória em uso por programas");
    private readonly (Border Card, TextBlock Value, TextBlock Detail) _helpers = OptimizeKit.Stat("Ajudantes de fundo sem uso");
    private readonly Dictionary<string, bool> _choice = new(StringComparer.OrdinalIgnoreCase);
    private readonly Button _optimize = CompactPick.Button("Otimizar processos", primary: true);
    private readonly TextBox _search;
    private List<Group> _groups = new();
    private int _filter, _sort = 4;
    private bool _descending = true, _busy;

    public ProcessesPage() : base("Processos abertos",
        "O que está rodando agora, agrupado por programa. Os ajudantes de fundo que não fazem falta já vêm marcados; os essenciais ficam protegidos.")
    {
        var (searchBox, input) = CompactPick.Search("Buscar processo...", 200);
        _search = input;
        _search.TextChanged += (_, _) => Render();
        _optimize.Click += async (_, _) => await OptimizeAsync();
        _table.SortRequested += (column, descending) => { _sort = column; _descending = descending; Render(); };

        Body.Children.Add(OptimizeKit.StatRow(_count.Card, _memory.Card, _helpers.Card));
        Body.Children.Add(OptimizeKit.Toolbar(
            new UIElement[] { searchBox, OptimizeKit.Filters(new[] { "Todos", "Aplicativos", "Segundo plano", "Pode fechar" }, 0, i => { _filter = i; Render(); }) },
            new UIElement[] { _optimize }));
        Body.Children.Add(_table);
        Body.Children.Add(OptimizeKit.SafetyNote("Nunca fecha o que é do Windows, antivírus, drivers de vídeo e áudio, antitrapaça, lojas de jogos, Discord, programas de captura nem o MB Extreme. " +
                                                 "Fechar um programa não desinstala nada: ele volta quando você abrir de novo."));

        _timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) => { _timer.Start(); await RefreshAsync(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    private async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var all = await Task.Run(_list.Sample);
            _groups = all.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => new Group(g.Key, g.ToList(), g.Sum(p => p.CpuPercent), g.Sum(p => p.WorkingSetBytes),
                    g.Any(p => !string.IsNullOrWhiteSpace(p.Title)), g.Select(p => p.Title).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))))
                .ToList();
            int helpers = _groups.Count(g => OptimizeAdvisor.ProcessOneClick(g.Name));
            _count.Value.Text = all.Count.ToString();
            _count.Detail.Text = $"{_groups.Count} programas diferentes";
            _memory.Value.Text = $"{all.Sum(p => p.WorkingSetBytes) / 1_073_741_824.0:0.0} GB";
            _memory.Detail.Text = $"maior: {_groups.OrderByDescending(g => g.Memory).FirstOrDefault()?.Name ?? "—"}";
            _helpers.Value.Text = helpers.ToString();
            _helpers.Detail.Text = helpers > 0 ? $"liberam ~{_groups.Where(g => OptimizeAdvisor.ProcessOneClick(g.Name)).Sum(g => g.Memory) / 1_048_576.0:0} MB" : "nada sobrando";
            _helpers.Detail.Foreground = Brush(helpers > 0 ? "WarnBrush" : "OkBrush");
            Render();
        }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException)
        {
            SetStatus("Não foi possível listar os processos: " + ex.Message, "WarnBrush");
        }
        finally { _busy = false; }
    }

    private void Render()
    {
        string q = _search.Text.Trim();
        IEnumerable<Group> rows = _groups.Where(g =>
            (q.Length == 0 || g.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || (g.Title?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false)) &&
            _filter switch
            {
                1 => g.HasWindow,
                2 => !g.HasWindow,
                3 => !OptimizeAdvisor.ProcessProtected(g.Name),
                _ => true,
            });
        Func<Group, object> key = _sort switch
        {
            1 => g => OptimizeAdvisor.ProcessKind(g.Name, g.HasWindow),
            2 => g => g.Items.Count,
            3 => g => g.Cpu,
            4 => g => g.Memory,
            5 => g => Situation(g).Text,
            _ => g => g.Name.ToLowerInvariant(),
        };
        rows = _descending ? rows.OrderByDescending(key) : rows.OrderBy(key);
        double maxMemory = Math.Max(1, _groups.Count == 0 ? 1 : _groups.Max(g => g.Memory));
        _table.SetRows(rows.Select(g => Row(g, maxMemory)).ToList());
        UpdateButton();
    }

    private static (string Text, string Brush) Situation(Group g) =>
        OptimizeAdvisor.ProcessProtected(g.Name) ? ("Protegido", "MutedBrush")
        : OptimizeAdvisor.ProcessOneClick(g.Name) ? ("Pode fechar", "OkBrush")
        : g.HasWindow ? ("Em uso (janela aberta)", "WarnBrush")
        : ("Confira antes", "MutedBrush");

    private TableRow Row(Group g, double maxMemory)
    {
        bool locked = OptimizeAdvisor.ProcessProtected(g.Name);
        bool on = !locked && (_choice.TryGetValue(g.Name, out var chosen) ? chosen : OptimizeAdvisor.ProcessOneClick(g.Name));
        var box = new CheckBox { IsChecked = on, IsEnabled = !locked };
        box.Click += (_, _) => { _choice[g.Name] = box.IsChecked == true; UpdateButton(); };
        var (text, brush) = Situation(g);
        string kind = OptimizeAdvisor.ProcessKind(g.Name, g.HasWindow);
        string glyph = kind switch { "Windows" => "", "Aplicativo" => "", _ => "" };
        return new TableRow(box, CompactTable.Glyph(glyph, locked ? "MutedBrush" : "AccentBrush"), new object[]
        {
            g.Title is { Length: > 0 } title ? $"{g.Name}  ·  {title}" : g.Name,
            kind,
            g.Items.Count.ToString(),
            CompactTable.Meter($"{g.Cpu:0.0}%", g.Cpu / 100, g.Cpu > 20 ? "WarnBrush" : "AccentBrush"),
            CompactTable.Meter($"{g.Memory / 1_048_576.0:0} MB", g.Memory / maxMemory, g.Memory > 1_073_741_824 ? "WarnBrush" : "AccentBrush"),
            CompactTable.Badge(text, brush),
        }, ToolTip: $"PID: {string.Join(", ", g.Items.Select(p => p.Id).Take(12))}");
    }

    private IEnumerable<Group> Chosen() => _groups.Where(g => !OptimizeAdvisor.ProcessProtected(g.Name) &&
        (_choice.TryGetValue(g.Name, out var c) ? c : OptimizeAdvisor.ProcessOneClick(g.Name)));

    private void UpdateButton()
    {
        var chosen = Chosen().ToList();
        _optimize.IsEnabled = chosen.Count > 0;
        _optimize.Content = chosen.Count > 0 ? $"Otimizar processos ({chosen.Count})" : "Nada marcado";
    }

    private async Task OptimizeAsync()
    {
        var chosen = Chosen().ToList();
        if (chosen.Count == 0) return;
        string list = string.Join("\n", chosen.Take(15).Select(g => $"• {g.Name} ({g.Memory / 1_048_576.0:0} MB)"));
        bool windows = chosen.Any(g => g.HasWindow);
        if (MessageBox.Show($"Fechar {chosen.Count} programa(s)?\n\n{list}" + (windows ? "\n\nAtenção: algum deles tem janela aberta; o que não foi salvo nele se perde." : ""),
                "Otimizar processos", MessageBoxButton.YesNo, windows ? MessageBoxImage.Warning : MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        long freed = chosen.Sum(g => g.Memory);
        int ok = 0, fail = 0;
        await Task.Run(() =>
        {
            foreach (var p in chosen.SelectMany(g => g.Items))
            {
                var (done, _) = ProcessList.Kill(p.Id, p.Name, p.StartTime);
                if (done) ok++; else fail++;
            }
        });
        foreach (var g in chosen) _choice.Remove(g.Name);
        SetStatus($"{ok} processo(s) fechados · ~{freed / 1_048_576.0:0} MB liberados." + (fail > 0 ? $" {fail} não fecharam (já tinham saído ou são protegidos)." : ""), fail == 0 ? "OkBrush" : "WarnBrush");
        await RefreshAsync();
    }
}
