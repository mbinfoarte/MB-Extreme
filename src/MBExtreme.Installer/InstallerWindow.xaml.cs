using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MBExtreme.Core.Setup;

namespace MBExtreme.Installer;

public partial class InstallerWindow : Window
{
    private enum State { Welcome, Installing, Done, Failed }

    private static readonly (string Glyph, string Title, string Text)[] SlideInfo =
    {
        ("▤", "Tudo organizado no menu", "Otimizar, Rede, Windows e Ferramentas: cada grupo reúne o que você precisa."),
        ("✔", "Você escolhe cada ajuste", "Cada ajuste mostra o risco e o que se perde. Marque, aplique e pronto."),
        ("◔", "Acompanhe cada passo", "Uma janela com porcentagem mostra o que está sendo feito, e você pode parar."),
        ("↺", "Sempre dá para voltar", "O valor original é guardado antes de mudar. Restaurar originais desfaz tudo."),
    };

    private readonly List<Border> _dots = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(4.5) };
    private State _state = State.Welcome;
    private string _directory = InstallInfo.DefaultDirectory;
    private int _slide;

    public InstallerWindow()
    {
        InitializeComponent();
        DirectoryBox.Text = _directory;
        for (int i = 0; i < SlideInfo.Length; i++)
        {
            int index = i;
            var dot = new Border { Style = (Style)FindResource("Dot") };
            dot.MouseLeftButtonUp += (_, _) => ShowSlide(index);
            _dots.Add(dot);
            DotsPanel.Children.Add(dot);
        }
        _timer.Tick += (_, _) => ShowSlide((_slide + 1) % SlideInfo.Length);
        ShowSlide(0);
    }

    private void ShowSlide(int index)
    {
        _slide = index;
        SlideGlyph.Text = SlideInfo[index].Glyph;
        SlideTitle.Text = SlideInfo[index].Title;
        SlideText.Text = SlideInfo[index].Text;
        for (int i = 0; i < _dots.Count; i++)
            _dots[i].Background = i == index ? (Brush)FindResource("AccentBrush") : new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));

        // Reinicia o tempo para a pessoa conseguir ler depois de trocar o slide manualmente.
        _timer.Stop();
        _timer.Start();
    }

    private void PreviousSlide_Click(object sender, RoutedEventArgs e) =>
        ShowSlide((_slide + SlideInfo.Length - 1) % SlideInfo.Length);

    private void NextSlide_Click(object sender, RoutedEventArgs e) =>
        ShowSlide((_slide + 1) % SlideInfo.Length);

    private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left) DragMove();
    }

    private void ChangeDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Escolha a pasta onde instalar" };
        if (dialog.ShowDialog(this) != true) return;

        string folder = dialog.FolderName;
        _directory = Path.GetFileName(folder.TrimEnd('\\')).Equals(InstallInfo.FolderName, StringComparison.OrdinalIgnoreCase)
            ? folder
            : Path.Combine(folder, InstallInfo.FolderName);
        DirectoryBox.Text = _directory;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        switch (_state)
        {
            case State.Welcome:
                await InstallAsync();
                break;
            case State.Done:
                if (LaunchBox.IsChecked == true) Launch();
                Close();
                break;
            default:
                Close();
                break;
        }
    }

    private void Launch()
    {
        try
        {
            string exe = Path.Combine(_directory, InstallInfo.ExecutableName);
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = _directory });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show("Não foi possível abrir o " + InstallInfo.AppName + ": " + ex.Message, InstallInfo.AppName);
        }
    }

    private async Task InstallAsync()
    {
        string root = Path.GetPathRoot(Path.GetFullPath(_directory)) ?? "";
        if (string.IsNullOrWhiteSpace(_directory) || Path.GetFullPath(_directory).TrimEnd('\\') == root.TrimEnd('\\'))
        {
            MessageBox.Show("Escolha uma pasta, não a raiz do disco.", InstallInfo.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _state = State.Installing;
        WelcomePage.Visibility = Visibility.Collapsed;
        InstallPage.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Collapsed;
        CloseButton.Visibility = Visibility.Collapsed;
        StatusPanel.Visibility = Visibility.Visible;
        PercentText.Visibility = Visibility.Visible;
        Bar.Visibility = Visibility.Visible;
        PrimaryButton.IsEnabled = false;
        StatusText.Text = "Preparando a instalação";

        var options = new InstallOptions(_directory, DesktopBox.IsChecked == true, StartMenuBox.IsChecked == true);
        var progress = new Progress<(double Percent, string Message)>(p =>
        {
            Bar.Value = p.Percent;
            PercentText.Text = $"{Math.Round(p.Percent)}%";
            StatusText.Text = p.Message;
        });

        try
        {
            await Task.Run(() => InstallRunner.Run(options, progress));
            _state = State.Done;
            StatusText.Text = "Instalação concluída.";
            CloseButton.Visibility = Visibility.Visible;
            LaunchBox.Visibility = Visibility.Visible;
            PrimaryButton.Content = "Concluir";
        }
        catch (Exception ex)
        {
            _state = State.Failed;
            StatusText.Text = "Não foi possível instalar: " + ex.Message;
            StatusText.Foreground = (Brush)FindResource("BadBrush");
            CloseButton.Visibility = Visibility.Visible;
            PrimaryButton.Content = "Fechar";
        }

        PrimaryButton.IsEnabled = true;
    }
}
