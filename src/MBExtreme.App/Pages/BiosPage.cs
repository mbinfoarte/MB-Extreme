using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Hardware;

namespace MBExtreme.UI.Pages;

/// <summary>Versão da BIOS, microcódigo e como procurar atualização com segurança. O MB Extreme nunca atualiza a BIOS sozinho.</summary>
public sealed class BiosPage : ToolPageBase
{
    public BiosPage() : base("BIOS e atualizações",
        "Mostra a BIOS instalada e abre a busca no site oficial da fabricante da sua placa ou do seu equipamento. Não existe uma API pública das fabricantes para saber a versão mais nova, por isso a conferência final é feita no site delas.")
    {
        Loaded += async (_, _) => { if (Body.Children.Count == 0) await RunOnce(LoadAsync); };
    }

    private async Task LoadAsync()
    {
        SetStatus("Lendo a BIOS...");
        HardwareInventory hw;
        try
        {
            hw = await AppServices.Hardware();
        }
        catch (Exception ex)
        {
            SetStatus("Não foi possível ler a BIOS: " + ex.Message, "BadBrush");
            return;
        }

        SetStatus("");
        Body.Children.Clear();
        int months = hw.Bios.ReleaseDate is { } d ? (int)((DateTime.Now - d).TotalDays / 30.4) : -1;
        var info = new StackPanel();
        info.Children.Add(new TextBlock { Text = $"{hw.Board.Manufacturer} {hw.Board.Product}", FontSize = 16, FontWeight = FontWeights.SemiBold });
        info.Children.Add(Muted($"BIOS {hw.Bios.Vendor} versão {hw.Bios.Version}" +
            (hw.Bios.ReleaseDate is { } date ? $" · {date:dd/MM/yyyy}" + (months >= 0 ? $" ({months} meses)" : "") : "")));
        if (hw.Cpu.Microcode is { } micro) info.Children.Add(Muted($"Microcódigo da CPU: 0x{micro:X}"));
        if (!string.IsNullOrWhiteSpace(hw.System.Model)) info.Children.Add(Muted($"Equipamento: {hw.System.Manufacturer} {hw.System.Model}"));
        Body.Children.Add(CardOf(info));

        var link = BiosGuide.SupportSearch(hw);
        Body.Children.Add(Row("Procurar BIOS mais nova",
            "Abre uma busca restrita ao site oficial" + (link.Vendor.Length > 0 ? $" da {link.Vendor}" : "") + " com o modelo exato." +
            (link.Tool.Length > 0 ? $"\nA fabricante também tem o aplicativo {link.Tool}, que pode avisar de novas versões." : ""),
            SmallButton("Abrir", (_, _) => Open(link.Url), true)));

        if (HardwareAdvisor.IsIntel13Or14GenDesktop(hw.Cpu))
        {
            string state = hw.Cpu.Microcode is { } m && m >= HardwareAdvisor.IntelMicrocodeMinimum
                ? "Seu microcódigo já inclui a correção da Intel (0x12B)."
                : "Sua BIOS pode estar sem a correção da Intel (microcódigo 0x12B ou mais novo). Atualize.";
            Body.Children.Add(Row("Intel 13ª/14ª geração", state + " Use também o perfil \"Intel Default Settings\" na BIOS.", null,
                hw.Cpu.Microcode is { } mm && mm >= HardwareAdvisor.IntelMicrocodeMinimum ? "OkBrush" : "WarnBrush"));
        }

        Body.Children.Add(Heading("Antes de atualizar a BIOS"));
        foreach (string step in BiosGuide.UpdateChecklist())
            Body.Children.Add(CardOf(new TextBlock { Text = "•  " + step, TextWrapping = TextWrapping.Wrap }));
    }
}
