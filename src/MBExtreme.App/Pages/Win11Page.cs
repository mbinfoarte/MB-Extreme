using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Windows;

namespace MBExtreme.UI.Pages;

/// <summary>Windows 11: confere os requisitos oficiais, um por um, e diz o que fazer quando algum falta. Só lê.</summary>
public sealed class Win11Page : ToolPageBase
{
    private bool _loaded;

    public Win11Page() : base("Windows 11",
        "Descubra se este PC pode receber o Windows 11 e, se não puder, o que impede e o que dá para fazer. Só leitura: nada é alterado. Mudanças na BIOS você faz por lá.")
    {
        AddButton("Verificar de novo", async (_, _) => await LoadAsync());
        AddButton("Abrir a lista oficial de processadores", (_, _) => Open("https://learn.microsoft.com/windows-hardware/design/minimum/windows-processor-requirements"), false);
        Loaded += async (_, _) => { if (!_loaded) await LoadAsync(); };
    }

    private async Task LoadAsync()
    {
        _loaded = true;
        Body.Children.Clear();
        SetStatus("Conferindo os requisitos (a placa de vídeo leva alguns segundos)...");
        int w = (int)SystemParameters.PrimaryScreenWidth, h = (int)SystemParameters.PrimaryScreenHeight;
        var dpi = VisualTreeHelperDpi();
        Win11Inputs inputs;
        try { inputs = await Task.Run(() => Win11Check.Read((int)(w * dpi), (int)(h * dpi), CancellationToken.None)); }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.IO.IOException)
        {
            SetStatus("Não foi possível ler tudo: " + ex.Message, "BadBrush");
            return;
        }
        var list = Win11Check.Evaluate(inputs);
        int ok = list.Count(r => r.State == ReqState.Ok);
        int fail = list.Count(r => r.State == ReqState.Fail);
        bool on11 = inputs.CurrentBuild >= 22000;
        string headline = fail == 0 && list.All(r => r.State == ReqState.Ok) ? "Este PC atende a todos os requisitos do Windows 11"
            : fail == 0 ? "Este PC parece atender aos requisitos (alguns itens não deu para conferir)"
            : $"Este PC não atende a {fail} requisito(s) do Windows 11";
        var summary = new StackPanel();
        summary.Children.Add(new TextBlock { Text = (fail == 0 ? "✔  " : "✖  ") + headline, FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Brush(fail == 0 ? "OkBrush" : "BadBrush"), TextWrapping = TextWrapping.Wrap });
        summary.Children.Add(Muted(on11 ? "Ele já está no Windows 11." : "Ele está no Windows 10. " + (fail == 0 ? "Pode atualizar pelo Windows Update ou pelo Assistente de Instalação do Windows 11." : "Resolva os itens marcados com ✖ antes de atualizar.")));
        summary.Children.Add(Muted($"Windows deste PC: {SystemSummary.WindowsName()}"));
        Body.Children.Add(CardOf(summary));

        foreach (var r in list)
        {
            string mark = r.State switch { ReqState.Ok => "✔", ReqState.Fail => "✖", _ => "?" };
            string brush = r.State switch { ReqState.Ok => "OkBrush", ReqState.Fail => "BadBrush", _ => "WarnBrush" };
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = $"{mark}  {r.Title}", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brush(brush) });
            text.Children.Add(new TextBlock { Text = r.Have, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(22, 2, 0, 0) });
            text.Children.Add(Muted("Exigido: " + r.Needed, 11.5)); ((TextBlock)text.Children[^1]).Margin = new Thickness(22, 0, 0, 0);
            if (r.Fix.Length > 0) { var fix = Muted("O que fazer: " + r.Fix, 11.5); fix.Margin = new Thickness(22, 2, 0, 0); fix.Foreground = Brush("WarnBrush"); text.Children.Add(fix); }
            Body.Children.Add(CardOf(text));
        }
        Body.Children.Add(Muted("Base: requisitos oficiais da Microsoft. Processadores pela geração (Intel 8ª geração ou mais nova, AMD Ryzen 2000 ou mais novo); para a resposta oficial, a Microsoft tem o app Verificação de Integridade do PC.", 11));
        SetStatus($"{ok} de {list.Count} requisitos atendidos.", fail == 0 ? "OkBrush" : "WarnBrush");
    }

    private double VisualTreeHelperDpi()
    {
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
    }
}
