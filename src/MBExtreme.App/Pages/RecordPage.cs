using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MBExtreme.Core.Apps;
using MBExtreme.Core.Progress;
using MBExtreme.Core.Recording;
using MBExtreme.UI.Recording;

namespace MBExtreme.UI.Pages;

/// <summary>Gravação de tela para guardar testes e melhorias: pela placa de vídeo (FFmpeg: 1080p, 1440p, 4K a 30/60 fps) ou no modo compatível (AVI embutido).</summary>
public sealed class RecordPage : ToolPageBase
{
    private static readonly int[] Rates = { 10, 15, 30 };
    private static readonly double[] Scales = { 1.0, 0.75, 0.5 };
    private static readonly (string Name, int W, int H)[] Resolutions = { ("Tela atual", 0, 0), ("Full HD 1080p", 1920, 1080), ("Quad HD 1440p", 2560, 1440), ("4K 2160p", 3840, 2160) };
    private static readonly int[] GpuRates = { 30, 60 };

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly StackPanel _files = new();
    private readonly StackPanel _gpuPanel = new();
    private readonly StackPanel _gpuOptions = new();
    private readonly StackPanel _avi = new();
    private readonly TextBlock _gpuStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _start;
    private readonly Button _stop;
    private readonly FfmpegRecorder _ffmpeg = AppServices.GpuRecorder;
    private IReadOnlyList<EncoderOption> _encoders = Array.Empty<EncoderOption>();
    private string? _ffmpegPath;
    private DateTime _gpuStarted;
    private int _engine;
    private int _rateIndex = 1;
    private int _scaleIndex = 1;
    private int _resIndex = 1;
    private int _gpuRateIndex = 1;
    private int _encoderIndex;
    private int _containerIndex;

    public RecordPage() : base("Gravação de tela",
        "Grave a tela para guardar testes e resultados de antes e depois. Pela placa de vídeo (NVIDIA NVENC, AMD AMF ou Intel Quick Sync) a gravação é leve e vai até 4K a 60 fps. O modo compatível não precisa de nada instalado, mas é mais pesado e limitado. Os vídeos ficam em Vídeos\\MB Extreme. Sem som nesta versão.")
    {
        _start = AddButton("Iniciar gravação", async (_, _) => await BeginAsync());
        _stop = AddButton("Parar", (_, _) => End(), false);
        AddButton("Abrir pasta", (_, _) => OpenFolder(), false);

        Body.Children.Add(Heading("Modo de gravação"));
        Body.Children.Add(Choice(new[] { "Pela placa de vídeo (FFmpeg)", "Compatível (AVI)" }, _engine, i =>
        {
            _engine = i;
            _gpuPanel.Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed;
            _avi.Visibility = i == 1 ? Visibility.Visible : Visibility.Collapsed;
        }));

        // ---- placa de vídeo
        _gpuPanel.Children.Add(CardOf(_gpuStatus));
        var gpuButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        gpuButtons.Children.Add(SmallButton("Instalar FFmpeg", async (_, _) => await InstallFfmpegAsync(), true));
        gpuButtons.Children.Add(SmallButton("Detectar codificadores da minha placa", async (_, _) => await DetectAsync()));
        _gpuPanel.Children.Add(gpuButtons);
        _gpuPanel.Children.Add(_gpuOptions);

        // ---- AVI
        _avi.Visibility = Visibility.Collapsed;
        _avi.Children.Add(Heading("Quadros por segundo"));
        _avi.Children.Add(Choice(Rates.Select(r => r + " fps").ToList(), _rateIndex, i => _rateIndex = i));
        _avi.Children.Add(Heading("Tamanho do vídeo"));
        _avi.Children.Add(Choice(Scales.Select(s => $"{s:0%}").ToList(), _scaleIndex, i => _scaleIndex = i));

        Body.Children.Add(_gpuPanel);
        Body.Children.Add(_avi);
        Body.Children.Add(Muted("Dica: a gravação também pode ser ligada nas páginas Benchmark e Teste de estresse (usa o modo compatível)."));
        Body.Children.Add(Heading("Gravações"));
        Body.Children.Add(_files);

        _timer.Tick += (_, _) => Tick();
        AppServices.Recorder.EndedByItself += message => Dispatcher.BeginInvoke(new Action(() =>
        {
            SetStatus(message, "WarnBrush");
            Refresh();
        }));
        Loaded += async (_, _) =>
        {
            _timer.Start();
            Refresh();
            await InitGpuAsync();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    private async Task InitGpuAsync()
    {
        _ffmpegPath = await Task.Run(FfmpegRecorder.Locate);
        if (_ffmpegPath is null)
        {
            _gpuStatus.Text = "FFmpeg não encontrado. Ele é o programa livre que faz a gravação acelerada pela placa de vídeo. Clique em Instalar FFmpeg (instala pelo winget, o instalador oficial da Microsoft).";
            return;
        }

        _gpuStatus.Text = "FFmpeg encontrado. Clique em \"Detectar codificadores da minha placa\" para ver o que ela suporta (leva alguns segundos).";
        if (_encoders.Count > 0) BuildGpuOptions();
    }

    private async Task InstallFfmpegAsync()
    {
        ShowProgress("Instalando o FFmpeg", "Instalação");
        IProgress<ProgressEvent> progress = new Progress<ProgressEvent>(Progress.Report);
        var (ok, message) = await Task.Run(() =>
        {
            progress.Report(new ProgressEvent("Instalação", 20, "Instalando pelo winget (Gyan.FFmpeg)"));
            return WingetService.Install("Gyan.FFmpeg");
        });
        Progress.Finish(ok ? "FFmpeg instalado." : "Não foi possível instalar.", ok, message);
    }

    protected override async void OnProgressClosed()
    {
        Refresh();
        await InitGpuAsync();
    }

    private async Task DetectAsync()
    {
        _ffmpegPath ??= await Task.Run(FfmpegRecorder.Locate);
        if (_ffmpegPath is null)
        {
            SetStatus("Instale o FFmpeg primeiro.", "WarnBrush");
            return;
        }

        SetStatus("Testando os codificadores da placa de vídeo...");
        string path = _ffmpegPath;
        _encoders = await Task.Run(() => FfmpegRecorder.ProbeEncoders(path));
        SetStatus(_encoders.Count == 0 ? "Nenhum codificador de GPU funcionou. Atualize o driver da placa de vídeo." : $"{_encoders.Count} codificador(es) disponível(is).",
            _encoders.Count == 0 ? "WarnBrush" : "OkBrush");
        BuildGpuOptions();
    }

    private void BuildGpuOptions()
    {
        _gpuOptions.Children.Clear();
        if (_encoders.Count == 0) return;
        _gpuStatus.Text = "Pronto para gravar pela placa de vídeo.";

        _gpuOptions.Children.Add(Heading("Resolução"));
        _gpuOptions.Children.Add(Choice(Resolutions.Select(r => r.Name).ToList(), _resIndex, i => _resIndex = i));
        _gpuOptions.Children.Add(Muted("Se a resolução for diferente da tela, a imagem é redimensionada pelo processador, o que pesa mais (principalmente em 4K a 60 fps). Gravar na resolução da tela mantém tudo na placa de vídeo."));
        _gpuOptions.Children.Add(Heading("Quadros por segundo"));
        _gpuOptions.Children.Add(Choice(GpuRates.Select(r => r + " fps").ToList(), _gpuRateIndex, i => _gpuRateIndex = i));
        _gpuOptions.Children.Add(Heading("Codificador"));
        _encoderIndex = Math.Min(_encoderIndex, _encoders.Count - 1);
        _gpuOptions.Children.Add(Choice(_encoders.Select(e => e.Title).ToList(), _encoderIndex, i => _encoderIndex = i));
        _gpuOptions.Children.Add(Heading("Arquivo"));
        _gpuOptions.Children.Add(Choice(new[] { "MKV (mais seguro se o PC travar)", "MP4" }, _containerIndex, i => _containerIndex = i));
    }

    private async Task BeginAsync()
    {
        try
        {
            if (_engine == 1)
            {
                string file = AppServices.Recorder.Start("tela", Rates[_rateIndex], Scales[_scaleIndex]);
                SetStatus("Gravando em " + file);
            }
            else
            {
                if (_ffmpegPath is null || _encoders.Count == 0)
                {
                    SetStatus("Instale o FFmpeg e detecte os codificadores antes de gravar.", "WarnBrush");
                    return;
                }

                var (nativeW, nativeH) = ScreenInfo.Primary;
                var res = Resolutions[_resIndex];
                int width = res.W == 0 ? nativeW : res.W;
                int height = res.H == 0 ? nativeH : res.H;
                int fps = GpuRates[_gpuRateIndex];
                var encoder = _encoders[Math.Min(_encoderIndex, _encoders.Count - 1)];
                Directory.CreateDirectory(ScreenRecorder.Folder);
                string path = Path.Combine(ScreenRecorder.Folder,
                    $"gravacao-{DateTime.Now:yyyyMMdd-HHmmss}{FfmpegCommand.ExtensionFor(_containerIndex == 1)}");
                var request = new CaptureRequest(width, height, nativeW, nativeH, fps, encoder,
                    FfmpegCommand.DefaultBitrateMbps(width, height, fps), path);
                string ffmpeg = _ffmpegPath;
                await Task.Run(() => _ffmpeg.Start(ffmpeg, request));
                _gpuStarted = DateTime.Now;
                SetStatus($"Gravando {width}x{height} a {fps} fps ({encoder.Title}) em {path}");
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            SetStatus("Não foi possível gravar: " + ex.Message, "BadBrush");
        }

        Refresh();
    }

    private void End()
    {
        string? file = _ffmpeg.IsRecording ? _ffmpeg.Stop() : AppServices.Recorder.Stop();
        if (file is not null) SetStatus("Gravação salva em " + file, "OkBrush");
        Refresh();
    }

    private void Tick()
    {
        if (_ffmpeg.IsRecording)
        {
            long size = _ffmpeg.CurrentFile is { } f && File.Exists(f) ? new FileInfo(f).Length : 0;
            SetStatus($"Gravando {DateTime.Now - _gpuStarted:mm\\:ss} · {size / 1_048_576.0:0} MB");
            return;
        }

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
        bool recording = _ffmpeg.IsRecording || AppServices.Recorder.IsRecording;
        _start.IsEnabled = !recording;
        _stop.IsEnabled = recording;

        _files.Children.Clear();
        string[] files;
        try
        {
            files = Directory.Exists(ScreenRecorder.Folder)
                ? Directory.GetFiles(ScreenRecorder.Folder).Where(f => f.EndsWith(".avi") || f.EndsWith(".mkv") || f.EndsWith(".mp4")).ToArray()
                : Array.Empty<string>();
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
