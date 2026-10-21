using System.IO;
using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Hardware;

namespace MBExtreme.UI.Pages;

/// <summary>Receita de BIOS personalizada (memória, Resizable BAR, PBO/Curve Optimizer). O MB Extreme só orienta; quem altera é você, na BIOS.</summary>
public sealed class RecipePage : ToolPageBase
{
    private HardwareInventory? _hw;

    public RecipePage() : base("Receita de BIOS",
        "Passo a passo para o SEU hardware: perfil de memória (XMP/EXPO), Resizable BAR e PBO/Curve Optimizer em Ryzen. O Windows não consegue aplicar isso sozinho e valores errados travam o PC, por isso o app orienta e você ajusta na BIOS.")
    {
        AddButton("Copiar receita", (_, _) =>
        {
            if (_hw is null) return;
            Clipboard.SetText(BiosGuide.RecipeToText(_hw));
            SetStatus("Receita copiada.", "OkBrush");
        }, false);
        AddButton("Salvar receita", (_, _) => Save(), false);
        AddButton("Teste de estresse", (_, _) => MainWindow.Current?.Go("stress"), false);
        Loaded += async (_, _) => { if (Body.Children.Count == 0) await LoadAsync(); };
    }

    private async Task LoadAsync()
    {
        SetStatus("Lendo o hardware...");
        try
        {
            _hw = await AppServices.Hardware();
        }
        catch (Exception ex)
        {
            SetStatus("Não foi possível ler o hardware: " + ex.Message, "BadBrush");
            return;
        }

        SetStatus("");
        Body.Children.Clear();
        Body.Children.Add(Muted($"{_hw.Cpu.Name} · {_hw.Board.Manufacturer} {_hw.Board.Product}", 13));
        foreach (var section in BiosGuide.BuildRecipe(_hw))
        {
            Body.Children.Add(Heading(section.Title));
            var steps = new StackPanel();
            int i = 1;
            foreach (string step in section.Steps)
                steps.Children.Add(new TextBlock { Text = $"{i++}.  {step}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) });
            Body.Children.Add(CardOf(steps));
        }
    }

    private void Save()
    {
        if (_hw is null) return;
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MB Extreme");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"receita-bios-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(file, BiosGuide.RecipeToText(_hw));
            SetStatus("Salva em " + file, "OkBrush");
        }
        catch (IOException ex)
        {
            SetStatus("Não foi possível salvar: " + ex.Message, "BadBrush");
        }
    }
}
