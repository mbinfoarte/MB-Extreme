using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MBExtreme.Core.Progress;

namespace MBExtreme.UI.Controls;

/// <summary>
/// Janela de progresso padrão do aplicativo: lista de etapas, anel com porcentagem, status e botões.
/// Todas as operações longas usam este mesmo componente.
/// </summary>
public partial class ProgressPanel : UserControl
{
    private readonly List<(string Name, TextBlock Text)> _steps = new();
    private int _current = -1;

    public ProgressPanel()
    {
        InitializeComponent();
    }

    /// <summary>Pedido do usuário para interromper a operação.</summary>
    public event EventHandler? StopRequested;

    /// <summary>O usuário fechou o painel depois do fim da operação.</summary>
    public event EventHandler? CloseRequested;

    public void Begin(string title, IEnumerable<string> steps)
    {
        TitleText.Text = title;
        StepsList.Children.Clear();
        _steps.Clear();
        _current = -1;

        foreach (var name in steps)
        {
            var text = new TextBlock { Margin = new Thickness(0, 4, 0, 4), FontSize = 14 };
            _steps.Add((name, text));
            StepsList.Children.Add(text);
            SetStep(text, name, StepState.Pending);
        }

        Ring.BeginAnimation(ProgressRing.PercentProperty, null);
        Ring.Percent = 0;
        StatusText.Text = "Preparando...";
        DetailText.Text = "";
        StopButton.IsEnabled = true;
        StopButton.Visibility = Visibility.Visible;
        CloseButton.Visibility = Visibility.Collapsed;
        Visibility = Visibility.Visible;
        Motion.Enter(this, fromY: 16);
        Spinner.Visibility = Visibility.Visible;
        Motion.Stop(Spinner);
        Motion.Spin(Spinner, 1.2);
        Motion.Cascade(StepsList, 45);
    }

    public void Report(ProgressEvent e)
    {
        Motion.To(Ring, ProgressRing.PercentProperty, e.Percent, Motion.Normal);   // o anel desliza até o valor novo
        StatusText.Text = e.Message;

        int current = _steps.FindIndex(s => s.Name == e.Step);
        if (current < 0) return;
        if (current != _current)
        {
            _current = current;
            Motion.Flash(_steps[current].Text);   // etapa nova acende
        }

        for (int i = 0; i < _steps.Count; i++)
        {
            var state = i < current ? StepState.Done : i == current ? StepState.Current : StepState.Pending;
            SetStep(_steps[i].Text, _steps[i].Name, state);
        }
    }

    public void Finish(string summary, bool success, string? detail = null)
    {
        StatusText.Text = summary;
        DetailText.Text = detail ?? "";
        Motion.Flash(StatusText);
        Motion.Stop(Spinner);
        Spinner.Visibility = Visibility.Collapsed;
        if (success)
        {
            Motion.To(Ring, ProgressRing.PercentProperty, 100, Motion.Normal);
            foreach (var (name, text) in _steps) SetStep(text, name, StepState.Done);
        }

        StopButton.Visibility = Visibility.Collapsed;
        CloseButton.Visibility = Visibility.Visible;
    }

    private enum StepState { Pending, Current, Done }

    private static void SetStep(TextBlock text, string name, StepState state)
    {
        text.Text = state switch
        {
            StepState.Done => $"✓  {name}",
            StepState.Current => $"▶  {name}",
            _ => $"•  {name}",
        };
        text.Foreground = (Brush)Application.Current.FindResource(state switch
        {
            StepState.Done => "TextBrush",
            StepState.Current => "AccentBrush",
            _ => "MutedBrush",
        });
        text.FontWeight = state == StepState.Current ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopButton.IsEnabled = false;
        StatusText.Text = "Parando...";
        StopRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Visibility = Visibility.Collapsed;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
