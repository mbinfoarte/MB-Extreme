using System.IO;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Startup;
using MBExtreme.Core.Tools;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Programas que abrem junto com o Windows, em grade (como Programas › Instalar) e em ordem alfabética: marcado = abre com o Windows.
/// Mostra o tempo que o PC levou para ligar e tem o "Otimizar inicialização", que desliga num clique o que é seguro (volta pela Central Desfazer).
/// </summary>
public sealed class StartupPage : ToolPageBase
{
    private readonly System.Windows.Controls.Primitives.UniformGrid _grid = CompactPick.Grid(5, 250);
    private readonly (Border Card, TextBlock Value, TextBlock Detail) _boot = OptimizeKit.Stat("Tempo para ligar o PC");
    private readonly (Border Card, TextBlock Value, TextBlock Detail) _on = OptimizeKit.Stat("Abrem com o Windows");
    private readonly (Border Card, TextBlock Value, TextBlock Detail) _safe = OptimizeKit.Stat("Podem esperar você abrir");
    private readonly TextBox _search;
    private readonly Button _optimize = CompactPick.Button("Otimizar inicialização", primary: true);
    private List<(StartupEntry Entry, string WhatIs, StartupAdvice Advice)> _items = new();
    private int _filter;

    public StartupPage() : base("Programas da inicialização",
        "Marcado = abre junto com o Windows. Desmarcar não apaga nada: o programa só deixa de abrir sozinho e você pode marcar de novo quando quiser.")
    {
        var (searchBox, input) = CompactPick.Search("Buscar programa...", 220);
        _search = input;
        _search.TextChanged += (_, _) => Fill();
        _optimize.Click += (_, _) => OptimizeAll();
        var refresh = CompactPick.Button("Atualizar");
        refresh.Click += (_, _) => Reload();

        Body.Children.Add(OptimizeKit.StatRow(_boot.Card, _on.Card, _safe.Card));
        Body.Children.Add(OptimizeKit.Toolbar(
            new UIElement[] { searchBox, OptimizeKit.Filters(new[] { "Todos", "Ligados", "Desligados", "Pode desligar" }, 0, i => { _filter = i; Fill(); }) },
            new UIElement[] { refresh, _optimize }));
        Body.Children.Add(_grid);
        Body.Children.Add(OptimizeKit.SafetyNote("O \"Otimizar inicialização\" nunca desliga antivírus, áudio, touchpad, Bluetooth, programa da placa de vídeo, periféricos nem o MB Extreme. " +
                                                 "Tudo volta pela Central Desfazer ou marcando de novo aqui."));
        Loaded += (_, _) => Reload();
    }

    private async void Reload()
    {
        IReadOnlyList<StartupEntry> entries;
        try { entries = await Task.Run(StartupService.List); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            SetStatus("Não foi possível ler a lista: " + ex.Message, "BadBrush");
            return;
        }
        _items = entries.Select(e => { var d = StartupAdvisor.Describe(e.Name, e.Command); return (e, d.WhatIs, d.Advice); })
                        .OrderBy(x => x.e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        Fill();
        UpdateNumbers();
        await LoadBootAsync();
    }

    private void UpdateNumbers()
    {
        int on = _items.Count(i => i.Entry.Enabled);
        int safe = _items.Count(i => i.Entry.Enabled && OptimizeAdvisor.StartupOneClick(i.Advice));
        _on.Value.Text = on.ToString();
        _on.Detail.Text = $"de {_items.Count} programas da lista";
        _safe.Value.Text = safe.ToString();
        _safe.Detail.Text = safe > 0 ? "o \"Otimizar\" desliga esses" : "nada para desligar";
        _safe.Detail.Foreground = Brush(safe > 0 ? "WarnBrush" : "OkBrush");
        _optimize.IsEnabled = safe > 0;
        _optimize.Content = safe > 0 ? $"Otimizar inicialização ({safe})" : "Inicialização otimizada";
    }

    private void Fill()
    {
        string q = _search.Text.Trim();
        _grid.Children.Clear();
        foreach (var item in _items)
        {
            if (q.Length > 0 && !item.Entry.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)) continue;
            bool show = _filter switch
            {
                1 => item.Entry.Enabled,
                2 => !item.Entry.Enabled,
                3 => item.Entry.Enabled && OptimizeAdvisor.StartupOneClick(item.Advice),
                _ => true,
            };
            if (show) _grid.Children.Add(Card(item));
        }
        if (_grid.Children.Count == 0)
            _grid.Children.Add(Muted(_items.Count == 0 ? "Nenhum programa na inicialização." : "Nada com esse filtro."));
    }

    private Border Card((StartupEntry Entry, string WhatIs, StartupAdvice Advice) item)
    {
        var entry = item.Entry;
        var box = new CheckBox { IsChecked = entry.Enabled, ToolTip = "Abre com o Windows" };
        box.Click += (_, _) => Apply(entry, box.IsChecked == true, box);
        string brush = item.Advice switch
        {
            StartupAdvice.CanDisable => "OkBrush", StartupAdvice.DisableIfUnused => "WarnBrush", StartupAdvice.KeepOn => "BadBrush", _ => "MutedBrush",
        };
        string impact = OptimizeAdvisor.StartupImpact(entry.Name, entry.Command);
        var tag = CompactPick.Tag(StartupAdvisor.AdviceText(item.Advice), brush);
        var card = CompactPick.Card(box, CompactPick.ProgramIcon(OptimizeKit.ExeOf(entry.Command), entry.Name), entry.Name, $"Impacto {impact.ToLowerInvariant()} · {item.WhatIs}", tag);
        card.ToolTip = $"{item.WhatIs}\n{entry.SourceText} · {entry.Command}";
        return card;
    }

    private bool Apply(StartupEntry entry, bool enabled, CheckBox? box)
    {
        try
        {
            StartupService.SetEnabled(entry, enabled);
            MBExtreme.Core.Operations.OperationJournal.Shared.RecordSetting("startup." + entry.Name, (enabled ? "Abrir na inicialização: " : "Tirar da inicialização: ") + entry.Name, true,
                "Para desfazer: marque ou desmarque de novo em Programas da inicialização.");
            int index = _items.FindIndex(i => i.Entry == entry);
            if (index >= 0) _items[index] = (entry with { Enabled = enabled }, _items[index].WhatIs, _items[index].Advice);
            SetStatus(enabled ? $"{entry.Name}: vai abrir com o Windows." : $"{entry.Name}: não abre mais com o Windows.", "OkBrush");
            UpdateNumbers();
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException or IOException)
        {
            if (box is not null) box.IsChecked = !enabled;
            SetStatus($"Não foi possível alterar {entry.Name}: {ex.Message}", "BadBrush");
            return false;
        }
    }

    /// <summary>Um clique: desliga tudo que é seguro (o que o MB conhece e pode esperar a pessoa abrir).</summary>
    private void OptimizeAll()
    {
        var targets = _items.Where(i => i.Entry.Enabled && OptimizeAdvisor.StartupOneClick(i.Advice)).ToList();
        if (targets.Count == 0) return;
        string list = string.Join("\n", targets.Take(15).Select(t => "• " + t.Entry.Name)) + (targets.Count > 15 ? $"\n• e mais {targets.Count - 15}" : "");
        if (MessageBox.Show($"Tirar {targets.Count} programa(s) da inicialização?\n\n{list}\n\nEles continuam instalados e abrem normalmente quando você clicar neles.",
                "Otimizar inicialização", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        int ok = targets.Count(t => Apply(t.Entry, false, null));
        Fill();
        SetStatus($"{ok} programa(s) tirados da inicialização. Reinicie o PC e veja o tempo para ligar de novo aqui.", "OkBrush");
    }

    private async Task LoadBootAsync()
    {
        try
        {
            var boots = await Task.Run(StartupAdvisor.ReadBootTimes);
            if (boots.Count == 0)
            {
                _boot.Value.Text = "—";
                _boot.Detail.Text = "o Windows ainda não mediu: reinicie o PC e volte aqui";
                return;
            }
            var last = boots[0];
            _boot.Value.Text = $"{last.Boot.TotalSeconds:0.0} s";
            _boot.Detail.Text = $"ligou em {last.At:dd/MM HH:mm}" + (last.MainPath is { } m ? $" · área de trabalho em {m.TotalSeconds:0.0} s" : "")
                                + (boots.Count > 1 ? $" · antes: {boots[1].Boot.TotalSeconds:0} s" : "");
            _boot.Detail.Foreground = Brush(last.Boot.TotalSeconds <= 30 ? "OkBrush" : last.Boot.TotalSeconds <= 60 ? "WarnBrush" : "BadBrush");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _boot.Value.Text = "—";
            _boot.Detail.Text = "não deu para ler: " + ex.Message;
        }
    }
}
