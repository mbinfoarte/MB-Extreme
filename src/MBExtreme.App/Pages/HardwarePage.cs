using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Hardware;

namespace MBExtreme.UI.Pages;

/// <summary>Visão geral do hardware e dos avisos: o que está bom, o que dá para melhorar e onde.</summary>
public sealed class HardwarePage : ToolPageBase
{
    public HardwarePage() : base("Meu hardware",
        "O MB Extreme lê as peças do seu PC e aponta o que pode ser melhorado: memória em velocidade errada, canal único, Windows desatualizado, BIOS antiga e mais. Só leitura; nada é alterado.")
    {
        AddButton("Ler de novo", async (_, _) => await LoadAsync(true));
        AddButton("Receita de BIOS", (_, _) => MainWindow.Current?.Go("recipe"), false);
        AddButton("BIOS e atualizações", (_, _) => MainWindow.Current?.Go("bios"), false);
        Loaded += async (_, _) => { if (Body.Children.Count == 0) await LoadAsync(false); };
    }

    private static string LevelBrush(CheckLevel level) => level switch
    {
        CheckLevel.Ok => "OkBrush",
        CheckLevel.Info => "MutedBrush",
        CheckLevel.Attention => "WarnBrush",
        _ => "BadBrush",
    };

    private static string LevelMark(CheckLevel level) => level switch
    {
        CheckLevel.Ok => "✓  ",
        CheckLevel.Info => "ℹ  ",
        CheckLevel.Attention => "⚠  ",
        _ => "✖  ",
    };

    private async Task LoadAsync(bool refresh)
    {
        SetStatus("Lendo o hardware...");
        Body.Children.Clear();
        HardwareInventory hw;
        try
        {
            hw = await AppServices.Hardware(refresh);
        }
        catch (Exception ex)
        {
            SetStatus("Não foi possível ler o hardware: " + ex.Message, "BadBrush");
            return;
        }

        SetStatus("");
        var checks = HardwareAdvisor.Evaluate(hw, DateTime.Now);
        int issues = checks.Count(c => c.Level is CheckLevel.Attention or CheckLevel.Problem);
        Body.Children.Add(Heading(issues == 0 ? "Verificações: tudo certo" : $"Verificações: {issues} ponto(s) para melhorar"));

        foreach (var c in checks.OrderByDescending(c => c.Level))
        {
            string detail = c.Detail + (c.Action is null ? "" : "\n→ " + c.Action);
            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = LevelMark(c.Level) + c.Title,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush(c.Level == CheckLevel.Info ? "TextBrush" : LevelBrush(c.Level)),
                TextWrapping = TextWrapping.Wrap,
            });
            text.Children.Add(Muted(detail));
            Body.Children.Add(CardOf(text));
        }

        Body.Children.Add(Heading("Resumo das peças"));
        Body.Children.Add(Row("Processador", $"{hw.Cpu.Name}\n{hw.Cpu.Cores} núcleos · {hw.Cpu.Threads} threads" +
            (hw.Cpu.MaxMhz > 0 ? $" · até {hw.Cpu.MaxMhz / 1000.0:0.0} GHz" : "") +
            (hw.Cpu.L3Kb > 0 ? $" · cache L3 {hw.Cpu.L3Kb / 1024} MB" : "") +
            (hw.Cpu.Microcode is { } m ? $" · microcódigo 0x{m:X}" : "")));
        Body.Children.Add(Row("Placa-mãe", $"{hw.Board.Manufacturer} {hw.Board.Product}\nBIOS {hw.Bios.Version}" +
            (hw.Bios.ReleaseDate is { } d ? $" de {d:dd/MM/yyyy}" : "")));
        foreach (var gpu in hw.Gpus)
            Body.Children.Add(Row("Placa de vídeo", $"{gpu.Name}" + (gpu.VramBytes > 0 ? $" · {gpu.VramBytes / 1024.0 / 1024 / 1024:0.#} GB" : "") + $"\nDriver {gpu.DriverVersion}"));
        foreach (var mem in hw.Memory)
            Body.Children.Add(Row("Memória " + mem.Slot,
                $"{mem.CapacityBytes / 1024.0 / 1024 / 1024:0.#} GB {mem.Type} · em uso {mem.ConfiguredMhz} MHz · suporta {mem.RatedMhz} MHz\n{mem.Manufacturer} {mem.PartNumber}".Trim()));
        Body.Children.Add(Row("Windows", $"{hw.Windows.Edition} {hw.Windows.DisplayVersion} (compilação {hw.Windows.Build})" +
            (hw.IsLaptop ? "\nTipo: notebook" : "\nTipo: desktop") + $" · tela a {hw.RefreshRateHz} Hz"));
    }
}
