using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace MBExtreme.UI;

/// <summary>
/// Acessibilidade para o programa inteiro, de uma vez: todo botão, caixa de marcar e campo ganha um nome para o Narrador do Windows
/// (pelo texto que mostra ou, nos botões só com ícone, pela dica), sem precisar mexer em cada página. Também guarda e aplica a escala da interface.
/// </summary>
internal static class Accessibility
{
    public static double[] Scales => MBExtreme.Core.Ui.AccessibleText.Scales;

    public static void Install()
    {
        foreach (var type in new[] { typeof(ButtonBase), typeof(TextBox), typeof(ComboBox), typeof(Slider), typeof(ListBox) })
            EventManager.RegisterClassHandler(type, FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || AutomationProperties.GetName(element).Length > 0) return;
        string? name = NameOf(element);
        if (!string.IsNullOrWhiteSpace(name)) AutomationProperties.SetName(element, name);
        if (element.ToolTip is string tip && tip.Length > 0 && AutomationProperties.GetHelpText(element).Length == 0) AutomationProperties.SetHelpText(element, tip);
    }

    /// <summary>O texto que a pessoa vê; se for só um ícone (fonte de símbolos), a dica do botão.</summary>
    internal static string? NameOf(FrameworkElement element)
    {
        string? text = element is ContentControl content ? TextOf(content.Content) : null;
        return MBExtreme.Core.Ui.AccessibleText.Pick(text, element.ToolTip as string, element.Tag as string);
    }

    private static string? TextOf(object? content) => content switch
    {
        string s => s,
        TextBlock t => t.Text,
        Panel panel => panel.Children.OfType<object>().Select(TextOf).FirstOrDefault(IsReadable),
        Decorator d => TextOf(d.Child),
        ContentControl c => TextOf(c.Content),
        _ => null,
    };

    private static bool IsReadable(string? text) => MBExtreme.Core.Ui.AccessibleText.IsReadable(text);

    /// <summary>Aplica a escala (90% a 150%) no conteúdo da janela.</summary>
    public static void ApplyScale(FrameworkElement root, double scale)
    {
        scale = Math.Clamp(scale, 0.8, 1.6);
        root.LayoutTransform = Math.Abs(scale - 1) < 0.001 ? Transform.Identity : new ScaleTransform(scale, scale);
    }

    /// <summary>Próxima escala para cima ou para baixo (Ctrl + e Ctrl −).</summary>
    public static double Step(double current, int direction) => MBExtreme.Core.Ui.AccessibleText.Step(current, direction);
}
