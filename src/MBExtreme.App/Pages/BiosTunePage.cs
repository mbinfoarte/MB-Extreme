using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Hardware;
using MBExtreme.UI.Controls;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Otimização de BIOS: plano em ordem montado pelo hardware (BIOS, EXPO/XMP, Resizable BAR, PBO/Curve Optimizer ou estabilidade Intel,
/// limite de temperatura e overclock de memória), com o que o MB conferiu no PC, teste depois de cada passo e histórico.
/// </summary>
public sealed class BiosTunePage : ToolPageBase
{
    private HardwareInventory? _hw;

    public BiosTunePage() : base("Otimização de BIOS",
        "O MB lê o seu hardware e monta o plano de BIOS para ter mais desempenho e menos temperatura. Você ajusta na BIOS; o MB confere e testa.")
    {
        AddButton("Testar agora (estresse)", (_, _) => MainWindow.Current?.Go("stress"), true);
        AddButton("Copiar plano", (_, _) => { if (_hw is not null && TryCopy(BiosGuide.RecipeToText(_hw))) SetStatus("Plano copiado.", "OkBrush"); }, false);
        AddButton("Conferir de novo", async (_, _) => { _hw = null; await LoadAsync(true); }, false);
        Loaded += async (_, _) => { if (_hw is null) await LoadAsync(); };
    }

    private async Task LoadAsync(bool refresh = false)
    {
        SetStatus("Lendo o hardware...");
        try { _hw = await AppServices.Hardware(refresh); }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException or System.Management.ManagementException or UnauthorizedAccessException)
        {
            SetStatus("Não foi possível ler o hardware: " + ex.Message, "BadBrush");
            return;
        }
        double? maxTemp = null;
        try { var s = await Task.Run(async () => (await AppServices.Sensors).Read()); maxTemp = s.CpuTemperature; }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException) { }
        SetStatus("");
        Render(BiosPlan.Build(_hw, maxTemp));
    }

    private string _topic = "plan";
    private IReadOnlyList<BiosStep> _steps = Array.Empty<BiosStep>();

    /// <summary>Menu da BIOS no visual das abas do módulo: Plano, Undervolt, Overclock, TPM, Resizable BAR, XMP, Fans e Water cooler.</summary>
    private WrapPanel TopicBar()
    {
        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        string group = "bios-" + Guid.NewGuid().ToString("N");
        foreach (var (key, title, glyph) in BiosTopics.Menu)
        {
            var button = new RadioButton
            {
                Content = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold }, Tag = glyph, GroupName = group,
                Style = (Style)FindResource("ToolTab"), IsChecked = key == _topic,
            };
            string k = key;
            button.Checked += (_, _) => { if (_topic == k) return; _topic = k; Render(_steps); };
            bar.Children.Add(button);
        }
        return bar;
    }

    private void RenderTopic(BiosTopic topic)
    {
        var head = new DockPanel();
        var badge = GlassFx.Badge(topic.Glyph, Color.FromRgb(0xFF, 0xC4, 0x00), 42, breathe: false);
        badge.Margin = new Thickness(0, 0, 14, 0);
        DockPanel.SetDock(badge, Dock.Left);
        head.Children.Add(badge);
        var status = new Border
        {
            CornerRadius = new CornerRadius(999), Padding = new Thickness(10, 3, 10, 4), Background = Brush("Surface2Brush"), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = topic.Status, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brush(topic.StatusBrush) },
        };
        DockPanel.SetDock(status, Dock.Right);
        head.Children.Add(status);
        var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(new TextBlock { Text = topic.Title, FontSize = 18, FontWeight = FontWeights.Bold });
        t.Children.Add(Muted(topic.WhatIs, 12.5));
        head.Children.Add(t);
        var hc = CardOf(head);
        hc.BorderBrush = Brush("AccentBrush");
        Body.Children.Add(hc);

        var facts = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -10, 0) };
        facts.Children.Add(OptimizeKit.Stat("Ganho", topic.Gain.Length > 0 ? "" : "—", topic.Gain, "OkBrush").Card);
        facts.Children.Add(OptimizeKit.Stat("Risco", "", topic.Risk, "WarnBrush").Card);
        facts.Children.Add(OptimizeKit.Stat("Onde fica na BIOS", "", topic.WhereInBios, "TextBrush").Card);
        foreach (var card in facts.Children.OfType<Border>())
            if (card.Child is StackPanel sp && sp.Children.Count > 1 && sp.Children[1] is TextBlock v && v.Text.Length == 0) v.Visibility = Visibility.Collapsed;
        Body.Children.Add(facts);

        var cols = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var steps = new StackPanel();
        steps.Children.Add(new TextBlock { Text = "Passo a passo", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        int n = 1;
        foreach (string step in topic.Steps)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
            var num = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderBrush = Brush("AccentBrush"), BorderThickness = new Thickness(1.3), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = (n++).ToString(), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brush("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            DockPanel.SetDock(num, Dock.Left);
            row.Children.Add(num);
            row.Children.Add(new TextBlock { Text = step, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            steps.Children.Add(row);
        }
        cols.Children.Add(CardOf(steps));

        var side = new StackPanel();
        if (topic.Settings.Count > 0)
        {
            side.Children.Add(new TextBlock { Text = "Configuração para o seu PC", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            var table = new CompactTable(new[] { new TableColumn("Opção na BIOS"), new TableColumn("Valor", 210) }, checks: false, icons: false);
            table.SetRows(topic.Settings.Select(x => new TableRow(null, null, new object[] { x.Setting, new TextBlock { Text = x.Value, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brush("AccentBrush"), TextWrapping = TextWrapping.Wrap } })).ToList());
            side.Children.Add(table);
        }
        var test = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        test.Children.Add(new TextBlock { Text = "Como conferir", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        test.Children.Add(Muted(topic.Test, 12));
        if (topic.Page is { } page)
        {
            var go = SmallButton(page switch { "stress" => "Abrir o Teste de estresse", "win11" => "Conferir TPM e Secure Boot", "fans" => "Abrir Coolers e fans", _ => "Abrir" }, (_, _) => MainWindow.Current?.Go(page), true);
            go.Margin = new Thickness(0, 8, 0, 0);
            go.HorizontalAlignment = HorizontalAlignment.Left;
            test.Children.Add(go);
        }
        side.Children.Add(CardOf(test));
        side.Children.Add(CardOf(Muted("O MB nunca mexe na BIOS sozinho. Se o PC não ligar depois de um ajuste, limpe o CMOS (jumper ou bateria da placa) e use valores menores. Overclock pode afetar a garantia.", 11.5)));
        Grid.SetColumn(side, 2);
        cols.Children.Add(side);
        Body.Children.Add(cols);
    }

    private void Render(IReadOnlyList<BiosStep> steps)
    {
        var hw = _hw!;
        _steps = steps;
        Body.Children.Clear();
        Body.Children.Add(TopicBar());
        if (_topic != "plan" && BiosTopics.Build(hw).FirstOrDefault(x => x.Key == _topic) is { } topic)
        {
            RenderTopic(topic);
            return;
        }

        // cabeçalho com o PC e quantos passos dão ganho
        var head = new DockPanel();
        var badge = GlassFx.Badge("", Color.FromRgb(0xFF, 0xC4, 0x00), 46, breathe: true);
        badge.Margin = new Thickness(0, 0, 14, 0);
        DockPanel.SetDock(badge, Dock.Left);
        head.Children.Add(badge);
        var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        string mem = hw.Memory.Count > 0 ? $"{hw.Memory.Count}×{hw.Memory[0].CapacityBytes / 1024 / 1024 / 1024} GB {hw.Memory[0].Type}" : "";
        t.Children.Add(new TextBlock { Text = string.Join(" · ", new[] { hw.Cpu.Name, $"{hw.Board.Manufacturer} {hw.Board.Product}".Trim(), mem, hw.Gpus.FirstOrDefault()?.Name ?? "" }.Where(x => x.Length > 0)), FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        int pending = BiosPlan.Pending(steps);
        t.Children.Add(Muted(pending == 0 ? "Pelo que dá para ver no Windows, a BIOS já está bem ajustada." : $"Plano para o seu PC: {pending} passo(s) com ganho, {steps.Count(s => s.State == BiosStepState.Done)} já feito(s).", 12));
        head.Children.Add(t);
        var hc = CardOf(head);
        hc.BorderBrush = Brush("AccentBrush");
        Body.Children.Add(hc);

        var cols = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var plan = new StackPanel();
        plan.Children.Add(new TextBlock { Text = "Seu plano, na ordem", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        int n = 1;
        foreach (var step in steps) plan.Children.Add(StepRow(n++, step));
        cols.Children.Add(CardOf(plan));

        var side = new StackPanel();
        var how = new StackPanel();
        how.Children.Add(new TextBlock { Text = "Como funciona", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        string[] flow = { "Faça um passo na BIOS (DEL ou F2 ao ligar)", "Volte ao Windows e abra esta tela", "O MB confere o que dá (memória, Resizable BAR)", "Teste de estresse por 10 minutos", "Estável? Marque Feito e siga. Travou? Volte um passo" };
        for (int i = 0; i < flow.Length; i++) how.Children.Add(new TextBlock { Text = $"{i + 1}.  {flow[i]}", FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 5) });
        side.Children.Add(CardOf(how));
        var log = new StackPanel();
        log.Children.Add(new TextBlock { Text = "Seu histórico", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        var entries = AppServices.Settings.BiosLog;
        if (entries.Count == 0) log.Children.Add(Muted("Quando você marcar um passo como feito, ele aparece aqui com a data.", 11.5));
        foreach (string e in entries.AsEnumerable().Reverse().Take(10)) log.Children.Add(new TextBlock { Text = e, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
        side.Children.Add(CardOf(log));
        side.Children.Add(CardOf(Muted("O MB nunca mexe na BIOS sozinho. Se o PC não ligar depois de um ajuste: limpe o CMOS (jumper ou bateria da placa) e use valores menores. Overclock pode afetar a garantia.", 11.5)));
        Grid.SetColumn(side, 2);
        cols.Children.Add(side);
        Body.Children.Add(cols);
    }

    private UIElement StepRow(int number, BiosStep step)
    {
        string brush = step.State switch
        {
            BiosStepState.Done => "OkBrush", BiosStepState.Todo => "BadBrush", BiosStepState.Suggested => "AccentBrush", _ => "MutedBrush",
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14), BorderBrush = Brush(brush), BorderThickness = new Thickness(1.5), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = step.State == BiosStepState.Done ? "✓" : number.ToString(), FontWeight = FontWeights.Bold, Foreground = Brush(brush), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        var text = new StackPanel();
        var title = new WrapPanel();
        title.Children.Add(new TextBlock { Text = step.Title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0) });
        title.Children.Add(new TextBlock { Text = "● " + step.Status, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Brush(brush), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        title.Children.Add(new TextBlock { Text = step.Level, FontSize = 10.5, Foreground = Brush("MutedBrush"), VerticalAlignment = VerticalAlignment.Center });
        text.Children.Add(title);
        text.Children.Add(Muted(step.Detail, 11.5));
        var howPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
        foreach (string h in step.HowTo) howPanel.Children.Add(Muted("•  " + h, 11.5));
        text.Children.Add(howPanel);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var buttons = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 0, 0) };
        if (step.Key == "bios" && _hw is not null)
        {
            var link = BiosGuide.SupportSearch(_hw);
            buttons.Children.Add(Small("Página oficial", () => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link.Url) { UseShellExecute = true }); } catch (System.ComponentModel.Win32Exception) { } }));
        }
        if (step.HowTo.Count > 0)
            buttons.Children.Add(Small("Passo a passo", () => howPanel.Visibility = howPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible));
        if (step.State != BiosStepState.Done)
            buttons.Children.Add(Small("Feito", () =>
            {
                AppServices.Settings.BiosLog.Add($"{DateTime.Now:dd/MM HH:mm} · {step.Title} ({step.Status})");
                AppServices.Settings.Save();
                SetStatus($"Anotado: {step.Title}. Agora rode o teste de estresse por 10 minutos para conferir.", "OkBrush");
                if (_hw is not null) Render(BiosPlan.Build(_hw));
            }));
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
        return new Border { Background = Brush("Surface2Brush"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 8), Child = grid };
    }

    private Button Small(string text, Action click)
    {
        var b = SmallButton(text, (_, _) => click());
        b.Margin = new Thickness(0, 0, 0, 6);
        b.Padding = new Thickness(10, 4, 10, 4);
        b.FontSize = 11.5;
        return b;
    }
}
