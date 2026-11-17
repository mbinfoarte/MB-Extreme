using System.Windows;
using System.Windows.Controls;

namespace MBExtreme.UI;

/// <summary>No WPF um elemento só pode ter um pai. Telas que se remontam usam isto antes de pôr um elemento guardado num painel novo.</summary>
internal static class UiTree
{
    public static T Free<T>(T element) where T : FrameworkElement
    {
        switch (element.Parent)
        {
            case Panel panel: panel.Children.Remove(element); break;
            case Decorator decorator when ReferenceEquals(decorator.Child, element): decorator.Child = null; break;
            case ContentControl content when ReferenceEquals(content.Content, element): content.Content = null; break;
        }
        return element;
    }
}
