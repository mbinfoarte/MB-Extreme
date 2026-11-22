using System.IO;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Tools;
using MBExtreme.UI.Controls;
using Microsoft.Win32;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Serviços numa lista fina em colunas: os de outros fabricantes que abrem com o Windows e os do Windows que quase ninguém usa.
/// "Otimizar serviços" passa os marcados para Manual (não abrem mais sozinhos, mas o Windows ainda os inicia quando um programa
/// precisar); "Voltar para automático" desfaz. Os seguros já vêm marcados.
/// </summary>
public sealed class ServicesPage : ToolPageBase
{
    private const string ServicesPath = @"SYSTEM\CurrentControlSet\Services";

    private sealed record ServiceEntry(string Name, string Display, string Path, int Start, bool Windows, string WhatIs, OptimizeAdvice Advice);

    private readonly CompactTable _table = new(new[]
    {
        new TableColumn("Serviço"), new TableColumn("Origem", 110), new TableColumn("Abre", 150), new TableColumn("Recomendação", 140),
    });
    private readonly (Border Card, TextBlock Value, TextBlock Detail) _auto = OptimizeKit.Stat("Abrem com o Windows");
    private readonly (Border Card, TextBlock Value, TextBlock Detail) _safe = OptimizeKit.Stat("Podem ficar em Manual");
    private readonly Dictionary<string, bool> _choice = new(StringComparer.OrdinalIgnoreCase);
    private readonly Button _optimize = CompactPick.Button("Otimizar serviços", primary: true);
    private readonly Button _restore = CompactPick.Button("Voltar para automático");
    private readonly TextBox _search;
    private List<ServiceEntry> _services = new();
    private int _filter, _sort;
    private bool _descending;

    public ServicesPage() : base("Serviços",
        "Serviços que abrem com o Windows. Em Manual eles param de abrir junto com o PC, mas ainda iniciam quando um programa precisar. Os seguros já vêm marcados.")
    {
        var (searchBox, input) = CompactPick.Search("Buscar serviço...", 200);
        _search = input;
        _search.TextChanged += (_, _) => Render();
        _optimize.Click += (_, _) => Change(manual: true);
        _restore.Click += (_, _) => Change(manual: false);
        var refresh = CompactPick.Button("Atualizar");
        refresh.Click += (_, _) => Load();
        _table.SortRequested += (column, descending) => { _sort = column; _descending = descending; Render(); };

        Body.Children.Add(OptimizeKit.StatRow(_auto.Card, _safe.Card));
        Body.Children.Add(OptimizeKit.Toolbar(
            new UIElement[] { searchBox, OptimizeKit.Filters(new[] { "Todos", "Automático", "Manual", "Do Windows", "De programas" }, 0, i => { _filter = i; Render(); }) },
            new UIElement[] { refresh, _restore, _optimize }));
        Body.Children.Add(_table);
        Body.Children.Add(OptimizeKit.SafetyNote("Antivírus, drivers de vídeo e áudio, rede, Bluetooth, impressora, lojas de jogos, antitrapaça e acesso remoto ficam como estão. " +
                                                 "Cada mudança vai para a Central Desfazer."));
        Loaded += (_, _) => Load();
    }

    private async void Load()
    {
        _services = await Task.Run(Read);
        int auto = _services.Count(s => s.Start == 2);
        int safe = _services.Count(s => OptimizeAdvisor.ServiceOneClick(s.Advice, s.Start == 2));
        _auto.Value.Text = auto.ToString();
        _auto.Detail.Text = $"de {_services.Count} serviços na lista";
        _safe.Value.Text = safe.ToString();
        _safe.Detail.Text = safe > 0 ? "o \"Otimizar\" muda esses" : "nada para mudar";
        _safe.Detail.Foreground = Brush(safe > 0 ? "WarnBrush" : "OkBrush");
        Render();
    }

    private void Render()
    {
        string q = _search.Text.Trim();
        IEnumerable<ServiceEntry> rows = _services.Where(s =>
            (q.Length == 0 || s.Display.Contains(q, StringComparison.CurrentCultureIgnoreCase) || s.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) &&
            _filter switch { 1 => s.Start == 2, 2 => s.Start != 2, 3 => s.Windows, 4 => !s.Windows, _ => true });
        Func<ServiceEntry, object> key = _sort switch
        {
            1 => s => s.Windows ? 0 : 1,
            2 => s => s.Start,
            3 => s => (int)s.Advice,
            _ => s => s.Display.ToLowerInvariant(),
        };
        rows = _descending ? rows.OrderByDescending(key) : rows.OrderBy(key);
        _table.SetRows(rows.Select(Row).ToList());
        UpdateButtons();
    }

    private bool Chosen(ServiceEntry s) =>
        s.Advice != OptimizeAdvice.Keep && (_choice.TryGetValue(s.Name, out var c) ? c : OptimizeAdvisor.ServiceOneClick(s.Advice, s.Start == 2));

    private TableRow Row(ServiceEntry s)
    {
        bool locked = s.Advice == OptimizeAdvice.Keep;
        var box = new CheckBox { IsChecked = Chosen(s), IsEnabled = !locked };
        box.Click += (_, _) => { _choice[s.Name] = box.IsChecked == true; UpdateButtons(); };
        string start = s.Start switch { 2 => "Automático (com o Windows)", 3 => "Manual (quando precisar)", 4 => "Desativado", _ => "—" };
        return new TableRow(box, CompactTable.Glyph(s.Windows ? "" : "", locked ? "MutedBrush" : "AccentBrush"), new object[]
        {
            s.Display,
            s.Windows ? "Windows" : "Programa",
            CompactTable.Badge(start, s.Start == 2 ? "TextBrush" : "MutedBrush"),
            CompactTable.Badge(OptimizeAdvisor.AdviceText(s.Advice), OptimizeAdvisor.AdviceBrush(s.Advice)),
        }, ToolTip: $"{s.WhatIs}\nNome interno: {s.Name}\n{s.Path}");
    }

    private void UpdateButtons()
    {
        var chosen = _services.Where(Chosen).ToList();
        int toManual = chosen.Count(s => s.Start == 2);
        int toAuto = chosen.Count(s => s.Start == 3);
        _optimize.IsEnabled = toManual > 0;
        _optimize.Content = toManual > 0 ? $"Otimizar serviços ({toManual})" : "Nada para otimizar";
        _restore.IsEnabled = toAuto > 0;
        _restore.Content = toAuto > 0 ? $"Voltar para automático ({toAuto})" : "Voltar para automático";
    }

    private void Change(bool manual)
    {
        var targets = _services.Where(s => Chosen(s) && s.Start == (manual ? 2 : 3)).ToList();
        if (targets.Count == 0) return;
        string list = string.Join("\n", targets.Take(15).Select(s => "• " + s.Display));
        if (MessageBox.Show($"{(manual ? "Passar para Manual" : "Voltar para Automático")} {targets.Count} serviço(s)?\n\n{list}",
                "Serviços", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        int ok = 0, fail = 0;
        foreach (var s in targets)
        {
            if (SetStart(s.Name, manual ? 3 : 2))
            {
                ok++;
                _choice.Remove(s.Name);
                MBExtreme.Core.Operations.OperationJournal.Shared.RecordSetting("service." + s.Name, (manual ? "Serviço em Manual: " : "Serviço em Automático: ") + s.Display, true,
                    "Para desfazer: Otimizar › Processos e inicialização › Serviços.");
            }
            else fail++;
        }
        SetStatus($"{ok} serviço(s) alterados." + (fail > 0 ? $" {fail} são protegidos pelo Windows e ficaram como estavam." : "") + (manual ? " Vale a partir do próximo reinício." : ""),
            fail == 0 ? "OkBrush" : "WarnBrush");
        Load();
    }

    private static List<ServiceEntry> Read()
    {
        var list = new List<ServiceEntry>();
        string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(ServicesPath);
            if (root is null) return list;
            var known = OptimizeAdvisor.Services.Where(k => k.Windows).ToDictionary(k => k.Name, StringComparer.OrdinalIgnoreCase);
            foreach (string name in root.GetSubKeyNames())
            {
                try
                {
                    using var key = root.OpenSubKey(name);
                    if (key?.GetValue("Type") is not int type || (type & 0x30) == 0) continue;   // só serviços de programas (não drivers)
                    if (key.GetValue("Start") is not int start || start is not (2 or 3 or 4)) continue;
                    string path = Environment.ExpandEnvironmentVariables(key.GetValue("ImagePath") as string ?? "").Trim();
                    string display = key.GetValue("DisplayName") as string ?? name;
                    if (display.StartsWith('@')) display = name;
                    if (known.TryGetValue(name, out var k))
                    {
                        list.Add(new ServiceEntry(name, k.Title, path, start, true, k.WhatIs, k.Advice));
                        continue;
                    }
                    if (start == 4) continue;
                    if (path.Length == 0 || path.Contains(windowsDir, StringComparison.OrdinalIgnoreCase) ||
                        path.Contains("\\Windows\\", StringComparison.OrdinalIgnoreCase) || path.Contains("svchost", StringComparison.OrdinalIgnoreCase)) continue;
                    if (path.Contains("Windows Defender", StringComparison.OrdinalIgnoreCase)) continue;
                    var other = OptimizeAdvisor.Services.FirstOrDefault(x => !x.Windows && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    list.Add(new ServiceEntry(name, other?.Title ?? display, path, start, false, other?.WhatIs ?? "Serviço instalado por um programa.",
                        OptimizeAdvisor.ServiceAdvice(name, display)));
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        return list;
    }

    private static bool SetStart(string name, int start)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(ServicesPath + "\\" + name, writable: true);
            if (key is null) return false;
            key.SetValue("Start", start, RegistryValueKind.DWord);
            if (start == 3 && key.GetValue("DelayedAutostart") is not null) key.SetValue("DelayedAutostart", 0, RegistryValueKind.DWord);
            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }
}
