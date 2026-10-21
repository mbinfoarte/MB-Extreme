using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Games;

namespace MBExtreme.UI.Pages;

/// <summary>Assistente de jogo: sugere resolução, predefinição e upscaler conforme o hardware.</summary>
public sealed class GameAdvisorPage : ToolPageBase
{
    public GameAdvisorPage() : base("Assistente de jogo",
        "Em PCs fracos e médios, a maior parte do ganho de FPS vem das configurações dentro do jogo, não do Windows. Aqui estão pontos de partida para o seu hardware.")
    {
        AddButton("Ver meu hardware", (_, _) => MainWindow.Current?.Go("hardware"), false);
        AddButton("Aplicar perfil PC fraco", (_, _) => MainWindow.Current?.Go("profiles"), false);
        Loaded += async (_, _) => { if (Body.Children.Count == 0) await LoadAsync(); };
    }

    private async Task LoadAsync()
    {
        SetStatus("Lendo o hardware...");
        try
        {
            var hw = await AppServices.Hardware();
            var advice = GameAdvisor.Recommend(hw);
            SetStatus("");
            var head = new StackPanel();
            head.Children.Add(new TextBlock { Text = "Nível: " + advice.Tier, FontSize = 20, FontWeight = FontWeights.Bold });
            head.Children.Add(Muted(advice.Headline, 13));
            Body.Children.Add(CardOf(head));
            Body.Children.Add(Heading("O que fazer nos jogos"));
            foreach (string line in advice.Lines)
                Body.Children.Add(CardOf(new TextBlock { Text = "•  " + line, TextWrapping = TextWrapping.Wrap }));
        }
        catch (Exception ex)
        {
            SetStatus("Não foi possível ler o hardware: " + ex.Message, "BadBrush");
        }
    }
}
