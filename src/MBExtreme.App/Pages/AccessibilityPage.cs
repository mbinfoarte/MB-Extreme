using System.Windows;

namespace MBExtreme.UI.Pages;

/// <summary>Tamanho da interface e como usar o programa pelo teclado e com o Narrador.</summary>
public sealed class AccessibilityPage : ToolPageBase
{
    public AccessibilityPage() : base("Acessibilidade e tamanho",
        "Deixe a interface maior ou menor e veja como usar o programa só pelo teclado ou com o Narrador do Windows.")
    {
        Body.Children.Add(Heading("Tamanho da interface"));
        int current = Math.Max(0, Array.FindIndex(Accessibility.Scales, s => Math.Abs(s - AppServices.Settings.UiScale) < 0.01));
        Body.Children.Add(Choice(Accessibility.Scales.Select(s => $"{s * 100:0}%").ToList(), current, i =>
        {
            MainWindow.Current?.SetScale(Accessibility.Scales[i]);
            SetStatus($"Interface em {Accessibility.Scales[i] * 100:0}%.", "OkBrush");
        }));
        Body.Children.Add(Muted("Atalhos: Ctrl + aumenta, Ctrl − diminui e Ctrl 0 volta ao normal. Em telas pequenas, 90% mostra mais coisas de uma vez.", 12));

        Body.Children.Add(Heading("Pelo teclado"));
        Body.Children.Add(Row("Tab e Shift+Tab", "Andam entre os botões e caixas. O item com o foco ganha um contorno amarelo."));
        Body.Children.Add(Row("Espaço ou Enter", "Apertam o botão ou marcam a caixa com o foco."));
        Body.Children.Add(Row("Alt + seta para a esquerda", "Volta para a tela anterior (o botão lateral do mouse faz o mesmo)."));

        Body.Children.Add(Heading("Narrador e leitores de tela"));
        Body.Children.Add(Muted("Todos os botões, caixas de marcar e campos têm nome para o Narrador do Windows (Ctrl + Windows + Enter liga e desliga). " +
                                "Botões que só mostram um ícone usam a dica que aparece ao passar o mouse.", 12));
    }
}
