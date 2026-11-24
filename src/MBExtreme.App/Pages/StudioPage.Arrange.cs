using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Arrumação da parte de cima do estúdio: prévia ao lado dos controles (monitor deitado) ou prévia em cima, na largura toda,
/// com os controles embaixo (monitor em pé). As duas alças de arrastar mudam a largura dos controles e a altura da prévia.
/// </summary>
public sealed partial class StudioPage
{
    /// <summary>Abaixo desta largura (a da área do estúdio), a prévia vai para cima automaticamente.</summary>
    private const double StackBelowWidth = 980;

    private Border? _previewCard;
    private FrameworkElement? _controlsColumn;
    private readonly UniformGrid _outputsGrid = new() { Columns = 1 };
    private readonly Thumb _widthGrip = Grip(vertical: true);
    private readonly Thumb _heightGrip = Grip(vertical: false);
    private readonly Button _layoutButton = new() { Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
    private bool _stacked;

    private static readonly string[] LayoutNames = { "Automático", "Lado a lado", "Prévia em cima" };

    /// <summary>Alça de arrastar: uma faixa transparente com um tracinho no meio que fica amarelo com o mouse em cima.</summary>
    private static Thumb Grip(bool vertical)
    {
        var line = new FrameworkElementFactory(typeof(Border));
        line.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        line.SetValue(FrameworkElement.WidthProperty, vertical ? 4.0 : 56.0);
        line.SetValue(FrameworkElement.HeightProperty, vertical ? 56.0 : 4.0);
        line.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        line.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        line.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x52)));
        line.Name = "line";
        var root = new FrameworkElementFactory(typeof(Border));
        root.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        root.AppendChild(line);
        var template = new ControlTemplate(typeof(Thumb)) { VisualTree = root };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xFF, 0xC4, 0x00)), "line"));
        template.Triggers.Add(hover);
        return new Thumb
        {
            Template = template,
            Cursor = vertical ? Cursors.SizeWE : Cursors.SizeNS,
            Width = vertical ? 16 : double.NaN,
            Height = vertical ? double.NaN : 16,
            ToolTip = vertical ? "Arraste para mudar a largura da prévia e dos controles" : "Arraste para deixar a prévia maior ou menor",
        };
    }

    private void AddLayoutGrips()
    {
        _widthGrip.DragDelta += (_, e) =>
        {
            // arrastar para a esquerda alarga os controles; para a direita, a prévia
            double width = Math.Clamp(_top.ColumnDefinitions[2].ActualWidth - e.HorizontalChange, 240, 560);
            _top.ColumnDefinitions[2].Width = new GridLength(width);
            _s.StudioControlsWidth = width;
        };
        _heightGrip.DragDelta += (_, e) =>
        {
            double height = Math.Clamp((_preview.ActualHeight > 0 ? _preview.ActualHeight : _preview.MaxHeight) + e.VerticalChange, 200, 1400);
            _preview.MaxHeight = height;
            if (_stacked) _s.StudioPreviewHeightStacked = height;
            else _s.StudioPreviewHeight = height;
        };
        _widthGrip.DragCompleted += (_, _) => _s.Save();
        _heightGrip.DragCompleted += (_, _) => { _s.Save(); UpdateGhosts(); };
        _heightGrip.Margin = new Thickness(0, 2, 0, 6);
        _top.Children.Add(_widthGrip);
        _top.Children.Add(_heightGrip);
        SizeChanged += (_, e) => { if (e.WidthChanged) ArrangeTop(); };

        _layoutButton.Style = (Style)FindResource("GhostButton");
        _layoutButton.ToolTip = "Arrumação da tela: automática (prévia em cima num monitor em pé), lado a lado ou prévia em cima";
        _layoutButton.Click += (_, _) =>
        {
            _s.StudioLayoutMode = (_s.StudioLayoutMode + 1) % LayoutNames.Length;
            _s.Save();
            ArrangeTop();
        };
    }

    /// <summary>Põe a prévia ao lado ou em cima conforme a largura (ou a escolha do usuário) e ajusta as alças.</summary>
    private void ArrangeTop()
    {
        if (_previewCard is null || _controlsColumn is null) return;
        double available = ActualWidth > 0 ? ActualWidth : SystemParameters.WorkArea.Width;
        bool stacked = _s.StudioLayoutMode switch { 1 => false, 2 => true, _ => available < StackBelowWidth };
        _layoutButton.Content = "Arrumação: " + LayoutNames[Math.Clamp(_s.StudioLayoutMode, 0, LayoutNames.Length - 1)];
        bool changed = stacked != _stacked || _previewCard.Parent is null;
        _stacked = stacked;

        if (stacked)
        {
            Place(_previewCard, 0, 0, 3);
            Place(_heightGrip, 1, 0, 3);
            Place(_controlsColumn, 2, 0, 3);
            _previewCard.Margin = new Thickness(0);
            _widthGrip.Visibility = Visibility.Collapsed;
            _outputsGrid.Columns = 3;   // Gravar, Transmitir e Replay lado a lado, embaixo da prévia
            _preview.MaxHeight = Math.Clamp(_s.StudioPreviewHeightStacked, 200, 1400);
        }
        else
        {
            Place(_previewCard, 0, 0, 1);
            Place(_heightGrip, 1, 0, 1);
            Place(_widthGrip, 0, 1, 1);
            Place(_controlsColumn, 0, 2, 1);
            Grid.SetRowSpan(_controlsColumn, 3);
            Grid.SetRowSpan(_widthGrip, 2);
            _previewCard.Margin = new Thickness(0);
            _widthGrip.Visibility = Visibility.Visible;
            _outputsGrid.Columns = 1;
            _preview.MaxHeight = Math.Clamp(_s.StudioPreviewHeight, 200, 1400);
        }
        _top.ColumnDefinitions[2].Width = stacked ? new GridLength(0) : new GridLength(Math.Clamp(_s.StudioControlsWidth, 240, 560));
        _top.ColumnDefinitions[0].MinWidth = stacked ? 0 : 320;

        if (changed)
        {
            _verticalShown = null;   // o quadro vertical muda de tamanho junto
            UpdateVerticalLive();
            UpdateGhosts();
        }
    }

    private static void Place(UIElement element, int row, int column, int span)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, span);
        Grid.SetRowSpan(element, 1);
    }
}
