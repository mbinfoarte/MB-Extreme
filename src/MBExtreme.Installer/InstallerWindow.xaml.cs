using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MBExtreme.Core.Setup;

namespace MBExtreme.Installer;

public partial class InstallerWindow : Window
{
    private enum State { Welcome, Installing, Done, Failed }

    private static readonly (string Title, string Text)[] SlideInfo =
    {
        ("Tudo organizado no menu", "Otimizar, Rede, Windows e Ferramentas: cada grupo reúne o que você precisa, sem confusão."),
        ("Você escolhe cada ajuste", "Cada ajuste mostra o risco e o que se perde. Marque o que quer, clique em Aplicar e pronto."),
        ("Acompanhe cada passo", "Uma janela com porcentagem mostra o que está sendo feito, e você pode parar a qualquer momento."),
        ("Sempre dá para voltar", "O programa guarda o valor original antes de mudar. Restaurar originais desfaz tudo o que foi aplicado."),
    };

    private readonly UIElement[] _slides;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(4.5) };
    private State _state = State.Welcome;
    private string _directory = InstallInfo.DefaultDirectory;
    private int _slide;

    public InstallerWindow()
    {
        InitializeComponent();
        _slides = new UIElement[] { Slide1, Slide2, Slide3, Slide4 };
        DirectoryBox.Text = _directory;
        _timer.Tick += (_, _) => ShowSlide((_slide + 1) % _slides.Length);
        ShowSlide(0);
    }

    private void ShowSlide(int index)
    {
        _slide = index;
        for (int i = 0; i < _slides.Length; i++)
            _slides[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;

        SlideTitle.Text = SlideInfo[index].Title;
        SlideText.Text = SlideInfo[index].Text;
        SlideDots.Text = string.Join(" ", Enumerable.Range(0, _slides.Length).Select(i => i == index ? "●" : "○"));
        SlideDots.Foreground = (Brush)FindResource("AccentBrush");

        // Reinicia o tempo para a pessoa conseguir ler depois de trocar o slide manualmente.
        _timer.Stop();
        _timer.Start();
    }

    private void PreviousSlide_Click(object sender, RoutedEventArgs e) =>
        ShowSlide((_slide + _slides.Length - 1) % _slides.Length);

    private void NextSlide_Click(object sender, RoutedEventArgs e) =>
        ShowSlide((_slide + 1) % _slides.Length);

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
            StatusText.Text = "Instalação concluída. Veja o tutorial acima enquanto quiser.";
            LaunchBox.Visibility = Visibility.Visible;
            PrimaryButton.Content = "Concluir";
        }
        catch (Exception ex)
        {
            _state = State.Failed;
            StatusText.Text = "Não foi possível instalar: " + ex.Message;
            StatusText.Foreground = (Brush)FindResource("BadBrush");
            PrimaryButton.Content = "Fechar";
        }

        PrimaryButton.IsEnabled = true;
    }
}
