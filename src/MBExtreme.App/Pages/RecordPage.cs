using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MBExtreme.UI.Recording;

namespace MBExtreme.UI.Pages;

/// <summary>Gravação da tela para guardar testes e melhorias.</summary>
public sealed class RecordPage : ToolPageBase
{
    private static readonly int[] Rates = { 10, 15, 30 };
    private static readonly double[] Scales = { 1.0, 0.75, 0.5 };

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly StackPanel _files = new();
    private readonly Button _start;
    private readonly Button _stop;
    private int _rateIndex = 1;
    private int _scaleIndex = 1;

    public RecordPage() : base("Gravação de tela",
        "Grava a tela principal em vídeo (AVI, sem som) para guardar testes e resultados de antes e depois. O arquivo vai para Vídeos\\MB Extreme e abre no Windows Media Player ou no VLC. Qualidade menor e menos quadros geram arquivos menores.")
    {
        _start = AddButton("Iniciar gravação", (_, _) => Begin());
        _stop = AddButton("Parar", (_, _) => End(), false);
        AddButton("Abrir pasta", (_, _) => OpenFolder(), false);

        Body.Children.Add(Heading("Quadros por segundo"));
        Body.Children.Add(Choice(Rates.Select(r => r + " fps").ToList(), _rateIndex, i => _rateIndex = i));
        Body.Children.Add(Heading("Tamanho do vídeo"));
        Body.Children.Add(Choice(Scales.Select(s => $"{s:0%}").ToList(), _scaleIndex, i => _scaleIndex = i));
        Body.Children.Add(Muted("Dica: a gravação da tela também pode ser ligada nas páginas Benchmark e Teste de estresse."));
        Body.Children.Add(Heading("Gravações"));
        Body.Children.Add(_files);

        _timer.Tick += (_, _) => Tick();
        AppServices.Recorder.EndedByItself += message => Dispatcher.BeginInvoke(new Action(() =>
        {
            SetStatus(message, "WarnBrush");
            Refresh();
        }));
        Loaded += (_, _) =>
        {
            _timer.Start();
            Refresh();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    private void Begin()
    {
        try
        {
            string file = AppServices.Recorder.Start("tela", Rates[_rateIndex], Scales[_scaleIndex]);
            SetStatus("Gravando em " + file);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            SetStatus("Não foi possível gravar: " + ex.Message, "BadBrush");
        }

        Refresh();
    }

    private void End()
    {
        string? file = AppServices.Recorder.Stop();
        if (file is not null) SetStatus("Gravação salva em " + file, "OkBrush");
        Refresh();
    }

    private void Tick()
    {
        var recorder = AppServices.Recorder;
        if (!recorder.IsRecording) return;
        var elapsed = DateTime.Now - recorder.StartedAt;
        SetStatus($"Gravando {elapsed:mm\\:ss} · {recorder.CurrentBytes / 1_048_576.0:0} MB");
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(ScreenRecorder.Folder);
            Open(ScreenRecorder.Folder);
        }
        catch (IOException ex)
        {
            SetStatus(ex.Message, "BadBrush");
        }
    }

    private void Refresh()
    {
        _start.IsEnabled = !AppServices.Recorder.IsRecording;
        _stop.IsEnabled = AppServices.Recorder.IsRecording;

        _files.Children.Clear();
        string[] files;
        try
        {
            files = Directory.Exists(ScreenRecorder.Folder) ? Directory.GetFiles(ScreenRecorder.Folder, "*.avi") : Array.Empty<string>();
        }
        catch (IOException)
        {
            files = Array.Empty<string>();
        }

        if (files.Length == 0)
        {
            _files.Children.Add(Muted("Nenhuma gravação ainda."));
            return;
        }

        foreach (string file in files.OrderByDescending(f => File.GetCreationTime(f)).Take(15))
        {
            var info = new FileInfo(file);
            _files.Children.Add(Row(info.Name, $"{info.Length / 1_048_576.0:0.0} MB · {info.CreationTime:dd/MM/yyyy HH:mm}",
                SmallButton("Abrir", (_, _) => Open(file), true)));
        }
    }
}
