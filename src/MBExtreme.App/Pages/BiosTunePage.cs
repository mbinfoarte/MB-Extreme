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

    private void Render(IReadOnlyList<BiosStep> steps)
    {
        var hw = _hw!;
        Body.Children.Clear();

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
