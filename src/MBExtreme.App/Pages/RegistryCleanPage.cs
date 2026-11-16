using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Apps;
using MBExtreme.Core.Operations;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Limpeza de registro segura: seis grupos só com o que aponta para algo que não existe mais. À esquerda os grupos (marcar o grupo
/// inteiro), à direita os itens do grupo escolhido com o motivo. Antes de apagar, cópia (.reg) de tudo; volta pela Central Desfazer.
/// </summary>
public sealed class RegistryCleanPage : ToolPageBase
{
    private readonly Grid _layout = new();
    private readonly StackPanel _groups = new();
    private readonly StackPanel _detail = new();
    private readonly Dictionary<RegistryIssue, CheckBox> _boxes = new();
    private readonly Button _clean = CompactPick.Button("Limpar", primary: true);
    private readonly TextBlock _count = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    private IReadOnlyList<RegistryIssue> _issues = Array.Empty<RegistryIssue>();
    private RegistryIssueKind _selected = RegistryIssueKind.GhostProgram;
    private bool _busy;

    public RegistryCleanPage() : base("Limpeza de registro", "Só o que está comprovadamente quebrado: itens que apontam para programas e arquivos que não existem mais.")
    {
        AddButton("↻ Examinar de novo", async (_, _) => await ScanAsync(), false);

        _layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
        _layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        _layout.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel();
        left.Children.Add(_groups);
        var why = new StackPanel();
        why.Children.Add(new TextBlock { Text = "Por que não \"1.000 erros\"?", FontSize = 12.5, FontWeight = FontWeights.SemiBold });
        why.Children.Add(Muted("Limpadores que apagam tudo quase não deixam o PC mais rápido e às vezes quebram o Windows. O MB só mexe no que aponta para algo que não existe mais.", 11.5));
        var whyCard = CardOf(why);
        whyCard.Margin = new Thickness(0, 6, 0, 0);
        left.Children.Add(whyCard);
        _layout.Children.Add(left);
        var right = CardOf(_detail);
        right.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(right, 2);
        _layout.Children.Add(right);
        Body.Children.Add(_layout);

        var foot = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        _clean.MinHeight = 36;
        _clean.Click += async (_, _) => await CleanAsync();
        DockPanel.SetDock(_clean, Dock.Right);
        DockPanel.SetDock(_count, Dock.Right);
        foot.Children.Add(_clean);
        _count.Foreground = Brush("MutedBrush");
        foot.Children.Add(_count);
        foot.Children.Add(Muted("🛡  Antes de limpar, o MB salva uma cópia (.reg) de tudo. A Central Desfazer volta cada chave do jeito que estava.", 11.5));
        Body.Children.Add(foot);

        Loaded += async (_, _) => { if (_issues.Count == 0 && !_busy) await ScanAsync(); };
    }

    private async Task ScanAsync()
    {
        if (_busy) return;
        _busy = true;
        _groups.Children.Clear();
        _detail.Children.Clear();
        _detail.Children.Add(Muted("Examinando o registro...", 12));
        _clean.IsEnabled = false;
        try
        {
            _issues = OperatingSystem.IsWindows() ? await Task.Run(() => RegistryCleaner.Scan()) : Array.Empty<RegistryIssue>();
            _boxes.Clear();
            foreach (var issue in _issues)
            {
                var box = new CheckBox { IsChecked = issue.Recommended };
                box.Checked += (_, _) => { Count(); RenderGroups(); };
                box.Unchecked += (_, _) => { Count(); RenderGroups(); };
                _boxes[issue] = box;
            }
            if (_issues.Count > 0 && !_issues.Any(i => i.Kind == _selected)) _selected = _issues[0].Kind;
            SetStatus(_issues.Count == 0 ? "Nada quebrado no registro." : $"{_issues.Count} item(ns) em {_issues.Select(i => i.Kind).Distinct().Count()} grupo(s).", _issues.Count == 0 ? "OkBrush" : "MutedBrush");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or IOException)
        {
            SetStatus("Não deu para examinar tudo: " + ex.Message, "WarnBrush");
        }
        finally { _busy = false; }
        RenderGroups();
        RenderDetail();
        Count();
    }

    private void RenderGroups()
    {
        _groups.Children.Clear();
        foreach (RegistryIssueKind kind in Enum.GetValues<RegistryIssueKind>())
        {
            var k = kind;
            var items = _issues.Where(i => i.Kind == kind).ToList();
            int on = items.Count(i => _boxes[i].IsChecked == true);
            var box = new CheckBox { IsChecked = items.Count > 0 && on == items.Count ? true : on == 0 ? false : null, IsEnabled = items.Count > 0, IsThreeState = false };
            box.Click += (_, _) =>
            {
                bool value = box.IsChecked == true;
                foreach (var i in items) _boxes[i].IsChecked = value;
                _selected = k;
                RenderDetail();
            };
            var tag = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            tag.Children.Add(new TextBlock { Text = items.Count.ToString(), FontSize = 14, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right, Foreground = items.Count > 0 ? Brush("AccentBrush") : Brush("MutedBrush") });
            tag.Children.Add(new TextBlock { Text = "itens", FontSize = 10, Foreground = Brush("MutedBrush"), HorizontalAlignment = HorizontalAlignment.Right });
            var icon = new TextBlock { Text = Glyph(kind), FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 15, Foreground = Brush("AccentBrush"), TextAlignment = TextAlignment.Center };
            var card = CompactPick.Card(box, icon, RegistryCleaner.Title(kind), RegistryCleaner.Hint(kind), tag);
            card.Height = 52;
            card.Margin = new Thickness(0, 0, 0, 6);
            if (kind == _selected) card.BorderBrush = Brush("AccentBrush");
            card.MouseLeftButtonUp += (_, e) => { _selected = k; RenderGroups(); RenderDetail(); e.Handled = true; };
            _groups.Children.Add(card);
        }
    }

    private void RenderDetail()
    {
        _detail.Children.Clear();
        var items = _issues.Where(i => i.Kind == _selected).ToList();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var all = CompactPick.Button("Marcar todos");
        all.Click += (_, _) => { foreach (var i in items) _boxes[i].IsChecked = true; };
        DockPanel.SetDock(all, Dock.Right);
        head.Children.Add(all);
        head.Children.Add(new TextBlock { Text = RegistryCleaner.Title(_selected), FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(new TextBlock { Text = $"  · {items.Count} itens", FontSize = 12, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center });
        _detail.Children.Add(head);
        if (items.Count == 0) { _detail.Children.Add(Muted("Nada quebrado neste grupo.", 12)); return; }
        foreach (var issue in items.Take(300))
        {
            var box = _boxes[issue];
            var tag = CompactPick.Tag(issue.Why, issue.Recommended ? "MutedBrush" : "WarnBrush");
            tag.MaxWidth = 220;
            tag.TextTrimming = TextTrimming.CharacterEllipsis;
            var icon = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 13, Foreground = Brush("MutedBrush"), TextAlignment = TextAlignment.Center };
            var card = CompactPick.Card(box, icon, issue.Title, issue.KeyPath + (issue.ValueName is { Length: > 0 } v ? " › " + v : ""), tag);
            card.ToolTip = issue.KeyPath + (issue.ValueName is { } n ? "\nValor: " + n : "") + "\n" + issue.Why;
            _detail.Children.Add(card);
        }
        if (items.Count > 300) _detail.Children.Add(Muted($"e mais {items.Count - 300}", 11));
    }

    private void Count()
    {
        int on = _boxes.Count(b => b.Value.IsChecked == true);
        _count.Text = $"{on} marcados";
        _clean.Content = on > 0 ? $"Limpar {on} itens" : "Limpar";
        _clean.IsEnabled = on > 0 && !_busy;
    }

    private async Task CleanAsync()
    {
        var chosen = _boxes.Where(b => b.Value.IsChecked == true).Select(b => b.Key).ToList();
        if (chosen.Count == 0 || !OperatingSystem.IsWindows()) return;
        if (MessageBox.Show($"Limpar {chosen.Count} item(ns) do registro?\n\nAntes, o MB salva uma cópia de cada chave. Para voltar, use a Central Desfazer.",
                "Limpeza de registro", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _busy = true;
        _clean.IsEnabled = false;
        SetStatus("Salvando a cópia e limpando...");
        var (done, failed, backup) = await Task.Run(() => RegistryCleaner.Clean(chosen, "limpeza de registro"));
        if (done > 0)
            OperationJournal.Shared.Record(new OperationInfo("registry.clean", OperationKind.Registry, $"Limpeza de registro: {done} item(ns)", TargetId: backup, Reversible: true),
                OperationResult.Ok("Cópia guardada antes de apagar."), DateTimeOffset.Now);
        _busy = false;
        SetStatus($"{done} item(ns) limpos." + (failed > 0 ? $" {failed} não deu (abra o MB como administrador)." : " Cópia guardada; volta pela Central Desfazer."), failed == 0 ? "OkBrush" : "WarnBrush");
        await ScanAsync();
    }

    private static string Glyph(RegistryIssueKind kind) => kind switch
    {
        RegistryIssueKind.GhostProgram => "",
        RegistryIssueKind.BrokenStartup => "",
        RegistryIssueKind.AppPath => "",
        RegistryIssueKind.FileType => "",
        RegistryIssueKind.EmptyKey => "",
        _ => "",
    };
}
