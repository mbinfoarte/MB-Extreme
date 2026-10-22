using System.ComponentModel;
using System.Security.Cryptography;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MBExtreme.Core.Apps;
using MBExtreme.Core.Progress;
using MBExtreme.Core.Recording;
using MBExtreme.Core.Settings;
using MBExtreme.UI.Recording;

namespace MBExtreme.UI.Pages;

/// <summary>Estúdio: grava a tela ou uma janela, transmite ao vivo e guarda os melhores momentos (replay), com som do PC e microfone.</summary>
public sealed class StudioPage : ToolPageBase
{
    private static readonly (string Name, int Height)[] Resolutions =
        { ("Original", 0), ("1080p", 1080), ("1440p", 1440), ("4K 2160p", 2160), ("720p", 720) };
    private static readonly int[] FpsOptions = { 30, 60 };
    private static readonly int[] BitrateOptions = { 0, 8, 12, 20, 35, 50 };
    private static readonly int[] ReplayOptions = { 30, 60, 120 };
    private static readonly (string Name, double Value)[] Volumes = { ("Mudo", 0), ("50%", 0.5), ("100%", 1), ("150%", 1.5) };
    private static readonly int[] AviRates = { 10, 15, 30 };
    private static readonly double[] AviScales = { 1.0, 0.75, 0.5 };

    private readonly AppSettings _s = AppServices.Settings;
    private readonly StudioController _studio = AppServices.Studio;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    private readonly Button _start;
    private readonly Button _stop;
    private readonly Button _saveReplay;
    private readonly StackPanel _config = new();
    private readonly TextBlock _engineStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _qualityEncoder = new();
    private readonly StackPanel _displayPanel = new();
    private readonly StackPanel _windowPanel = new();
    private readonly StackPanel _streamPanel = new();
    private readonly StackPanel _replayPanel = new();
    private readonly StackPanel _recordPanel = new();
    private readonly StackPanel _live = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock _liveText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 14 };
    private readonly TextBlock _replayText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _warnings = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _level = new() { Minimum = 0, Maximum = 100, Height = 8, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel _files = new();
    private readonly PickList _displays;
    private readonly PickList _windows;
    private readonly PickList _mics;
    private readonly PickList _outputs;
    private readonly TextBox _customUrl = new() { Width = 420, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
    private readonly PasswordBox _key = new() { Width = 420, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
    private readonly CheckBox _remember;

    private IReadOnlyList<EncoderOption> _encoders = Array.Empty<EncoderOption>();
    private IReadOnlyList<DisplayInfo> _displayList = Array.Empty<DisplayInfo>();
    private IReadOnlyList<WindowInfo> _windowList = Array.Empty<WindowInfo>();
    private IReadOnlyList<AudioDevice> _micList = Array.Empty<AudioDevice>();
    private IReadOnlyList<AudioDevice> _outList = Array.Empty<AudioDevice>();
    private string? _ffmpegPath;

    private bool _outRecord = true, _outStream, _outReplay;
    private int _sourceTab;          // 0 = tela, 1 = janela
    private int _windowMode;         // 0 = recorte da tela, 1 = conteúdo da janela (GDI)
    private int _encoderIndex;
    private int _aviRate = 1, _aviScale = 1;

    public StudioPage() : base("Gravação e transmissão",
        "Estúdio completo: grave a tela inteira ou só a janela do jogo, transmita ao vivo (Twitch, YouTube ou outro servidor) e guarde os melhores momentos com o replay. A captura e a codificação usam a placa de vídeo (NVIDIA, AMD ou Intel) e o som do PC e do microfone entram na mesma gravação. Os vídeos ficam em Vídeos\\MB Extreme.")
    {
        _start = AddButton("Iniciar", async (_, _) => await BeginAsync());
        _stop = AddButton("Parar", async (_, _) => await EndAsync(), false);
        _saveReplay = AddButton("Salvar replay (Ctrl+Shift+F9)", async (_, _) => await _studio.SaveReplayAsync(), false);
        AddButton("Abrir pasta", (_, _) => OpenFolder(), false);

        _displays = new PickList(this, 160);
        _windows = new PickList(this, 220);
        _mics = new PickList(this, 140);
        _outputs = new PickList(this, 140);
        _remember = new CheckBox { Content = "Lembrar a chave neste PC (protegida pelo Windows, só este usuário lê)", Foreground = Brush("TextBrush"), Margin = new Thickness(0, 8, 0, 0) };

        Body.Children.Add(_live);
        BuildLive();
        Body.Children.Add(_config);
        BuildEngine();
        BuildOutputs();
        BuildSource();
        BuildQuality();
        BuildAudio();
        BuildStream();
        BuildCompat();

        Body.Children.Add(Heading("Gravações e replays"));
        Body.Children.Add(_files);

        _studio.Notice += OnNotice;
        _timer.Tick += (_, _) => Tick();
        Loaded += async (_, _) =>
        {
            _timer.Start();
            RefreshFiles();
            RefreshState();
            await InitAsync();
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            _studio.Notice -= OnNotice;
        };
    }

    // ---------------------------------------------------------------- construção da tela

    private void BuildLive()
    {
        _live.Children.Add(Heading("Ao vivo"));
        var panel = new StackPanel();
        panel.Children.Add(_liveText);
        panel.Children.Add(_level);
        panel.Children.Add(_replayText);
        panel.Children.Add(_warnings);
        _live.Children.Add(CardOf(panel));
        _warnings.Foreground = Brush("WarnBrush");
        _replayText.Foreground = Brush("MutedBrush");
        _replayText.Margin = new Thickness(0, 6, 0, 0);
        _warnings.Margin = new Thickness(0, 6, 0, 0);
    }

    private void BuildEngine()
    {
        _config.Children.Add(Heading("Programa de gravação"));
        _config.Children.Add(CardOf(_engineStatus));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(SmallButton("Instalar FFmpeg", async (_, _) => await InstallFfmpegAsync(), true));
        buttons.Children.Add(SmallButton("Detectar codificadores da minha placa", async (_, _) => await DetectAsync()));
        _config.Children.Add(buttons);
    }

    private void BuildOutputs()
    {
        _config.Children.Add(Heading("O que fazer"));
        _config.Children.Add(Muted("Marque o que você quer. Tudo sai de uma única captura, então ligar mais de uma saída pesa pouco."));

        var record = Check("Gravar em arquivo", "Salva o vídeo em Vídeos\\MB Extreme.");
        record.IsChecked = _outRecord;
        record.Click += (_, _) => { _outRecord = record.IsChecked == true; _recordPanel.Visibility = Vis(_outRecord); };
        _config.Children.Add(record);
        _recordPanel.Visibility = Vis(_outRecord);
        _recordPanel.Children.Add(Choice(new[] { "MKV (mais seguro se o PC travar)", "MP4" }, _s.StudioMp4 ? 1 : 0, i => _s.StudioMp4 = i == 1));
        _config.Children.Add(_recordPanel);

        var stream = Check("Transmitir ao vivo", "Envia para a Twitch, o YouTube ou outro servidor RTMP.");
        stream.IsChecked = _outStream;
        stream.Margin = new Thickness(0, 10, 0, 0);
        stream.Click += (_, _) => { _outStream = stream.IsChecked == true; _streamPanel.Visibility = Vis(_outStream); };
        _config.Children.Add(stream);

        var replay = Check("Replay dos melhores momentos", "Guarda os últimos segundos do jogo. Aconteceu algo incrível? Aperte Ctrl + Shift + F9 e o clipe é salvo.");
        replay.IsChecked = _outReplay;
        replay.Margin = new Thickness(0, 10, 0, 0);
        replay.Click += (_, _) => { _outReplay = replay.IsChecked == true; _replayPanel.Visibility = Vis(_outReplay); };
        _config.Children.Add(replay);
        _replayPanel.Visibility = Vis(_outReplay);
        _replayPanel.Children.Add(Muted("Quanto tempo guardar:"));
        _replayPanel.Children.Add(Choice(ReplayOptions.Select(r => r < 60 ? r + " s" : r / 60 + " min").ToList(),
            Math.Max(0, Array.IndexOf(ReplayOptions, _s.StudioReplaySeconds)), i => _s.StudioReplaySeconds = ReplayOptions[i]));
        var beep = new CheckBox { Content = "Tocar um aviso sonoro quando o clipe for salvo", IsChecked = _s.StudioReplayBeep, Foreground = Brush("TextBrush") };
        beep.Click += (_, _) => _s.StudioReplayBeep = beep.IsChecked == true;
        _replayPanel.Children.Add(beep);
        _replayPanel.Children.Add(Muted("O replay fica só na memória do disco enquanto o estúdio está ligado: os pedaços são apagados quando você para."));
        _config.Children.Add(_replayPanel);
    }

    private void BuildSource()
    {
        _config.Children.Add(Heading("O que capturar"));
        _config.Children.Add(Choice(new[] { "Tela inteira", "Janela de um programa" }, _sourceTab, i =>
        {
            _sourceTab = i;
            _displayPanel.Visibility = Vis(i == 0);
            _windowPanel.Visibility = Vis(i == 1);
            if (i == 1) RefreshWindows();
        }));

        _displayPanel.Children.Add(_displays);
        _displayPanel.Visibility = Vis(_sourceTab == 0);
        _config.Children.Add(_displayPanel);

        _windowPanel.Visibility = Vis(_sourceTab == 1);
        _windowPanel.Children.Add(Muted("Abra o jogo ou programa antes e clique em Atualizar a lista. Só a janela escolhida vai para a gravação e para a transmissão; o restante da tela (notificações, chat, outras janelas) fica de fora desde que não esteja sobre a janela."));
        _windowPanel.Children.Add(_windows);
        var windowButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        windowButtons.Children.Add(SmallButton("Atualizar a lista de janelas", (_, _) => RefreshWindows()));
        windowButtons.Children[0].SetValue(FrameworkElement.MarginProperty, new Thickness(0));
        _windowPanel.Children.Add(windowButtons);
        _windowPanel.Children.Add(Heading("Como capturar a janela"));
        _windowPanel.Children.Add(Choice(new[] { "Recorte da tela (jogos, recomendado)", "Conteúdo da janela (programas comuns)" }, _windowMode, i => _windowMode = i));
        _windowPanel.Children.Add(Muted("Recorte da tela: grava a área onde a janela está, pela placa de vídeo; funciona com jogos em tela cheia ou sem borda. Se outra janela ficar por cima ou o jogo mudar de lugar, isso aparece no vídeo. Conteúdo da janela: acompanha a janela mesmo coberta, mas pesa mais no processador e jogos em DirectX costumam sair pretos."));
        _config.Children.Add(_windowPanel);

        RefreshDisplays();
    }

    private void BuildQuality()
    {
        _config.Children.Add(Heading("Qualidade"));
        _config.Children.Add(Muted("Resolução do vídeo (a proporção da janela ou da tela é mantida):"));
        _config.Children.Add(Choice(Resolutions.Select(r => r.Name).ToList(), Math.Clamp(_s.StudioResolution, 0, Resolutions.Length - 1), i => _s.StudioResolution = i));
        _config.Children.Add(Muted("Quadros por segundo:"));
        _config.Children.Add(Choice(FpsOptions.Select(f => f + " fps").ToList(), Math.Max(0, Array.IndexOf(FpsOptions, _s.StudioFps)), i => _s.StudioFps = FpsOptions[i]));
        _config.Children.Add(Muted("Taxa de vídeo (qualidade da imagem):"));
        _config.Children.Add(Choice(BitrateOptions.Select(b => b == 0 ? "Automática" : b + " Mbps").ToList(),
            Math.Max(0, Array.IndexOf(BitrateOptions, _s.StudioBitrateMbps)), i => _s.StudioBitrateMbps = BitrateOptions[i]));
        _config.Children.Add(Muted("Ao transmitir, a gravação e o replay usam a mesma imagem enviada ao vivo: no máximo 8 Mbps. Para gravar com qualidade máxima, grave sem transmitir. Recomendado para transmitir: 1080p a 60 fps."));
        _config.Children.Add(Muted("Codificador da placa de vídeo:"));
        _config.Children.Add(_qualityEncoder);
    }

    private void BuildAudio()
    {
        _config.Children.Add(Heading("Áudio"));
        var system = Check("Som do PC (jogo, música, Discord...)", "Captura tudo que toca nos alto-falantes ou fones.");
        system.IsChecked = _s.StudioSystemAudio;
        system.Click += (_, _) => _s.StudioSystemAudio = system.IsChecked == true;
        _config.Children.Add(system);
        _config.Children.Add(Choice(Volumes.Select(v => v.Name).ToList(), NearestVolume(_s.StudioSystemVolume), i => _s.StudioSystemVolume = Volumes[i].Value));
        _config.Children.Add(Muted("Saída de som:"));
        _config.Children.Add(_outputs);

        var mic = Check("Microfone", "Sua voz na gravação e na transmissão.");
        mic.IsChecked = _s.StudioMicAudio;
        mic.Margin = new Thickness(0, 10, 0, 0);
        mic.Click += (_, _) => _s.StudioMicAudio = mic.IsChecked == true;
        _config.Children.Add(mic);
        _config.Children.Add(Choice(Volumes.Select(v => v.Name).ToList(), NearestVolume(_s.StudioMicVolume), i => _s.StudioMicVolume = Volumes[i].Value));
        _config.Children.Add(Muted("Microfone:"));
        _config.Children.Add(_mics);
        _config.Children.Add(Muted("Se desmarcar os dois, o vídeo sai sem áudio."));
        RefreshAudioDevices();
    }

    private void BuildStream()
    {
        _streamPanel.Visibility = Vis(_outStream);
        _config.Children.Add(_streamPanel);
        _streamPanel.Children.Add(Heading("Transmissão"));
        _streamPanel.Children.Add(Choice(new[] { "Twitch", "YouTube", "Outro servidor (RTMP)" }, Math.Clamp(_s.StudioStreamService, 0, 2), i =>
        {
            _s.StudioStreamService = i;
            _customUrl.Visibility = Vis(i == 2);
        }));
        _customUrl.Text = _s.StudioCustomUrl;
        _customUrl.Visibility = Vis(_s.StudioStreamService == 2);
        _streamPanel.Children.Add(_customUrl);
        _streamPanel.Children.Add(Muted("Chave de transmissão (Twitch: painel do criador > Configurações > Transmissão; YouTube: Transmitir ao vivo > Chave de transmissão). Quem tem a chave transmite no seu canal: não mostre nem compartilhe."));
        if (SecretStore.Unprotect(_s.StudioProtectedKey) is { } saved)
        {
            _key.Password = saved;
            _remember.IsChecked = true;
        }
        _streamPanel.Children.Add(_key);
        _streamPanel.Children.Add(_remember);
        _streamPanel.Children.Add(Muted("A transmissão usa H.264. A chave nunca aparece em mensagens nem em arquivos de relatório."));
    }

    private void BuildCompat()
    {
        _config.Children.Add(Heading("Modo compatível (sem FFmpeg)"));
        _config.Children.Add(Muted("Grava a tela inteira em AVI sem instalar nada e sem áudio. Mais pesado e limitado; use só se o modo acima não funcionar no seu PC."));
        _config.Children.Add(Choice(AviRates.Select(r => r + " fps").ToList(), _aviRate, i => _aviRate = i));
        _config.Children.Add(Choice(AviScales.Select(s => $"{s:0%}").ToList(), _aviScale, i => _aviScale = i));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var aviStart = SmallButton("Gravar em modo compatível", (_, _) => ToggleAvi(), false);
        aviStart.Margin = new Thickness(0);
        row.Children.Add(aviStart);
        _config.Children.Add(row);
    }

    // ---------------------------------------------------------------- inicialização e dados

    private static Visibility Vis(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private static int NearestVolume(double v)
    {
        int best = 0;
        for (int i = 1; i < Volumes.Length; i++)
            if (Math.Abs(Volumes[i].Value - v) < Math.Abs(Volumes[best].Value - v)) best = i;
        return best;
    }

    private async Task InitAsync()
    {
        _ffmpegPath = await Task.Run(FfmpegRecorder.Locate);
        if (_ffmpegPath is null)
        {
            _engineStatus.Text = "FFmpeg não encontrado. Ele é o programa livre que faz a captura e a codificação. Clique em Instalar FFmpeg (instala pelo winget, o instalador oficial da Microsoft).";
            return;
        }

        _engineStatus.Text = "FFmpeg encontrado. Clique em \"Detectar codificadores da minha placa\" para ver o que ela suporta (leva alguns segundos).";
        if (_encoders.Count == 0 && AppServices.CachedEncoders is { Count: > 0 } cached) _encoders = cached;
        if (_encoders.Count > 0) BuildEncoderChoice();
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
        RefreshFiles();
        await InitAsync();
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
        AppServices.CachedEncoders = _encoders;
        SetStatus(_encoders.Count == 0 ? "Nenhum codificador de GPU funcionou. Atualize o driver da placa de vídeo." : $"{_encoders.Count} codificador(es) disponível(is).",
            _encoders.Count == 0 ? "WarnBrush" : "OkBrush");
        BuildEncoderChoice();
    }

    private void BuildEncoderChoice()
    {
        _qualityEncoder.Children.Clear();
        if (_encoders.Count == 0) return;
        _engineStatus.Text = "Pronto: FFmpeg e codificadores encontrados.";
        _encoderIndex = Math.Min(_encoderIndex, _encoders.Count - 1);
        _qualityEncoder.Children.Add(Choice(_encoders.Select(e => e.Title).ToList(), _encoderIndex, i => _encoderIndex = i));
    }

    private void RefreshDisplays()
    {
        _displayList = CaptureCatalog.Displays();
        var names = _displayList.Count > 0
            ? _displayList.Select(d => d.Title).ToList()
            : new List<string> { $"Tela principal · {ScreenInfo.Primary.Width}×{ScreenInfo.Primary.Height}" };
        _displays.SetItems(names, 0);
    }

    private void RefreshWindows()
    {
        _windowList = CaptureCatalog.Windows();
        _windows.SetItems(_windowList.Select(w => w.DisplayName).ToList(), _windowList.Count > 0 ? 0 : -1);
        if (_windowList.Count == 0) SetStatus("Nenhuma janela encontrada. Abra o programa e atualize a lista.", "WarnBrush");
    }

    private void RefreshAudioDevices()
    {
        _outList = AudioDevices.Outputs();
        _micList = AudioDevices.Microphones();
        var outs = new List<string> { "Padrão do Windows" };
        outs.AddRange(_outList.Select(d => d.Name));
        _outputs.SetItems(outs, Math.Max(0, _outList.ToList().FindIndex(d => d.Id == _s.StudioOutputId) + 1));

        var mics = new List<string> { "Padrão do Windows" };
        mics.AddRange(_micList.Select(d => d.Name));
        _mics.SetItems(mics, Math.Max(0, _micList.ToList().FindIndex(d => d.Id == _s.StudioMicId) + 1));
    }

    // ---------------------------------------------------------------- iniciar e parar

    private async Task BeginAsync()
    {
        if (_studio.IsRunning || AppServices.Recorder.IsRecording) return;
        if (_ffmpegPath is null || _encoders.Count == 0)
        {
            SetStatus("Instale o FFmpeg e detecte os codificadores antes de começar.", "WarnBrush");
            return;
        }
        if (!_outRecord && !_outStream && !_outReplay)
        {
            SetStatus("Marque pelo menos uma opção: gravar, transmitir ou replay.", "WarnBrush");
            return;
        }

        // ---- origem
        CaptureSource? source;
        string? game = null;
        var displays = _displayList.Count > 0 ? _displayList : new[] { new DisplayInfo(0, "", new PixelRect(0, 0, ScreenInfo.Primary.Width, ScreenInfo.Primary.Height), true) };
        if (_sourceTab == 0)
        {
            int index = Math.Clamp(_displays.Selected, 0, displays.Count - 1);
            source = CaptureRules.ForDisplay(displays[index]);
        }
        else
        {
            if (_windows.Selected < 0 || _windows.Selected >= _windowList.Count)
            {
                SetStatus("Escolha a janela que será capturada.", "WarnBrush");
                return;
            }

            var chosen = _windowList[_windows.Selected];
            var current = await Task.Run(() => CaptureCatalog.Windows().FirstOrDefault(w => w.Handle == chosen.Handle));
            if (current is null)
            {
                SetStatus("A janela escolhida foi fechada ou minimizada. Atualize a lista.", "WarnBrush");
                RefreshWindows();
                return;
            }

            game = current.ProcessName;
            source = CaptureRules.ForWindow(current, displays, gdi: _windowMode == 1);
            if (source is null)
            {
                SetStatus("A janela está fora da área visível das telas.", "WarnBrush");
                return;
            }
        }

        // ---- codificador
        var encoder = _encoders[Math.Min(_encoderIndex, _encoders.Count - 1)];

        // ---- transmissão
        StreamTarget? target = null;
        if (_outStream)
        {
            var h264 = encoder.Codec == VideoCodec.H264 ? encoder : _encoders.FirstOrDefault(e => e.Codec == VideoCodec.H264);
            if (h264 is null)
            {
                SetStatus("A transmissão exige um codificador H.264, que não foi encontrado na sua placa.", "BadBrush");
                return;
            }
            encoder = h264;

            target = new StreamTarget((StreamService)Math.Clamp(_s.StudioStreamService, 0, 2), _customUrl.Text, _key.Password);
            // se a chave foi colada dentro do endereço, o endereço não é gravado
            if (target.PathSecret is null) _s.StudioCustomUrl = target.CustomUrl;
            if (MessageBox.Show($"Começar a transmitir ao vivo para {target.DisplayName}?\n\nQuem estiver assistindo verá exatamente o que está sendo capturado.",
                    "Transmissão ao vivo", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        }

        // ---- áudio
        AudioConfig? audio = null;
        if (_s.StudioSystemAudio || _s.StudioMicAudio)
        {
            _s.StudioOutputId = _outputs.Selected > 0 && _outputs.Selected - 1 < _outList.Count ? _outList[_outputs.Selected - 1].Id : null;
            _s.StudioMicId = _mics.Selected > 0 && _mics.Selected - 1 < _micList.Count ? _micList[_mics.Selected - 1].Id : null;
            audio = new AudioConfig(_s.StudioSystemAudio, _s.StudioOutputId, (float)_s.StudioSystemVolume,
                _s.StudioMicAudio, _s.StudioMicId, (float)_s.StudioMicVolume);
        }

        _s.StudioRememberKey = _remember.IsChecked == true;
        try
        {
            _s.StudioProtectedKey = _s.StudioRememberKey && _key.Password.Length > 0 ? SecretStore.Protect(_key.Password) : null;
        }
        catch (CryptographicException)
        {
            _s.StudioProtectedKey = null;
            SetStatus("Não foi possível guardar a chave com segurança; ela não será lembrada.", "WarnBrush");
        }
        _s.Save();

        var config = new StudioConfig(source, game, Resolutions[Math.Clamp(_s.StudioResolution, 0, Resolutions.Length - 1)].Height,
            _s.StudioFps, encoder, _s.StudioBitrateMbps, _outRecord, _s.StudioMp4, target, _outReplay, _s.StudioReplaySeconds, _s.StudioReplayBeep, audio);

        _start.IsEnabled = false;
        SetStatus("Iniciando...");
        try
        {
            await _studio.StartAsync(_ffmpegPath, config);
            var parts = new List<string>();
            if (_outRecord) parts.Add("gravando");
            if (_outStream) parts.Add("transmitindo para " + target!.DisplayName);
            if (_outReplay) parts.Add("replay ligado");
            SetStatus($"{char.ToUpper(parts[0][0])}{parts[0][1..]}{(parts.Count > 1 ? ", " + string.Join(", ", parts.Skip(1)) : "")} · {source.Description}", "OkBrush");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or Win32Exception)
        {
            SetStatus("Não foi possível iniciar: " + StudioCommand.MaskSecrets(ex.Message, target), "BadBrush");
        }

        RefreshState();
    }

    private async Task EndAsync()
    {
        _stop.IsEnabled = false;
        if (AppServices.Recorder.IsRecording)
        {
            string? avi = AppServices.Recorder.Stop();
            if (avi is not null) SetStatus("Gravação salva em " + avi, "OkBrush");
        }
        else
        {
            SetStatus("Finalizando o arquivo...");
            string? file = await Task.Run(_studio.Stop);
            SetStatus(file is null ? "Estúdio encerrado." : "Gravação salva em " + file, "OkBrush");
        }

        RefreshState();
        RefreshFiles();
    }

    private void ToggleAvi()
    {
        try
        {
            if (AppServices.Recorder.IsRecording) { _ = EndAsync(); return; }
            if (_studio.IsRunning) { SetStatus("Pare o estúdio antes de usar o modo compatível.", "WarnBrush"); return; }
            string file = AppServices.Recorder.Start("tela", AviRates[_aviRate], AviScales[_aviScale]);
            SetStatus("Gravando em " + file);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            SetStatus("Não foi possível gravar: " + ex.Message, "BadBrush");
        }

        RefreshState();
    }

    private void OnNotice(string message, bool ok)
    {
        SetStatus(message, ok ? "OkBrush" : "WarnBrush");
        RefreshFiles();
    }

    // ---------------------------------------------------------------- estado em tempo real

    private void RefreshState()
    {
        bool running = _studio.IsRunning || AppServices.Recorder.IsRecording;
        _start.IsEnabled = !running;
        _stop.IsEnabled = running;
        _saveReplay.IsEnabled = _studio.IsRunning && _studio.Config?.Replay == true;
        _config.IsEnabled = !running;
        _live.Visibility = Vis(_studio.IsRunning);
    }

    private void Tick()
    {
        var session = _studio.Session;
        if (session.EndedUnexpectedly)
        {
            string? last = session.LastMessage;
            _studio.Stop();
            SetStatus("O estúdio terminou sozinho" + (last is null ? "." : ": " + last), "WarnBrush");
            RefreshState();
            RefreshFiles();
            return;
        }

        if (session.IsRunning)
        {
            var p = session.Progress;
            var elapsed = DateTime.Now - session.StartedAt;
            string text = $"{elapsed:hh\\:mm\\:ss} · {p.Fps:0} fps · {p.BitrateKbps / 1000:0.0} Mbps · {p.TotalBytes / 1_048_576.0:0} MB";
            if (p.DroppedFrames > 0) text += $" · {p.DroppedFrames} quadros perdidos";
            if (session.Request?.Stream is { } stream) text = (session.StreamDropped ? "⚠ Transmissão com problema · " : $"● AO VIVO em {stream.DisplayName} · ") + text;
            _liveText.Text = text;
            _liveText.Foreground = Brush(session.StreamDropped ? "WarnBrush" : "TextBrush");
            _level.Visibility = Vis(session.HasAudio);
            _level.Value = Math.Min(100, session.AudioPeak * 100);

            if (_studio.Config?.Replay == true && session.ReplayPath is { } folder)
            {
                int ready = ReplayBuffer.SelectSegments(ReplayBuffer.ListSegments(folder), _studio.Config.ReplaySeconds).Count * StudioCommand.SegmentSeconds;
                _replayText.Text = $"Replay pronto: {ready} s de {_studio.Config.ReplaySeconds} s" + (_studio.HotkeyActive ? " · Ctrl + Shift + F9 salva o clipe" : "");
                _replayText.Visibility = Visibility.Visible;
            }
            else _replayText.Visibility = Visibility.Collapsed;

            var warnings = session.Warnings;
            _warnings.Text = string.Join("\n", warnings.TakeLast(3));
            _warnings.Visibility = Vis(warnings.Count > 0);
            return;
        }

        var avi = AppServices.Recorder;
        if (avi.IsRecording)
            SetStatus($"Gravando (modo compatível) {DateTime.Now - avi.StartedAt:mm\\:ss} · {avi.CurrentBytes / 1_048_576.0:0} MB");
    }

    // ---------------------------------------------------------------- arquivos

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(StudioController.Folder);
            Open(StudioController.Folder);
        }
        catch (IOException ex)
        {
            SetStatus(ex.Message, "BadBrush");
        }
    }

    private static readonly string[] VideoExtensions = { ".avi", ".mkv", ".mp4" };

    private void RefreshFiles()
    {
        _files.Children.Clear();
        var files = new List<string>();
        try
        {
            foreach (string folder in new[] { StudioController.Folder, StudioController.ClipsFolder })
                if (Directory.Exists(folder))
                    files.AddRange(Directory.GetFiles(folder).Where(f => VideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        if (files.Count == 0)
        {
            _files.Children.Add(Muted("Nada gravado ainda."));
            return;
        }

        foreach (string file in files.OrderByDescending(f => File.GetCreationTime(f)).Take(20))
        {
            var info = new FileInfo(file);
            bool clip = info.Name.StartsWith("Replay_", StringComparison.OrdinalIgnoreCase);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(SmallButton("Abrir", (_, _) => Open(file), true));
            buttons.Children.Add(SmallButton("Excluir", (_, _) => Delete(file)));
            _files.Children.Add(Row((clip ? "🎬 " : "") + info.Name, $"{info.Length / 1_048_576.0:0.0} MB · {info.CreationTime:dd/MM/yyyy HH:mm}", buttons));
        }
    }

    private void Delete(string file)
    {
        if (MessageBox.Show($"Excluir {Path.GetFileName(file)}?\n\nO arquivo é apagado de vez.", "Excluir vídeo",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Não foi possível excluir: " + ex.Message, "BadBrush");
        }

        RefreshFiles();
    }

    // ---------------------------------------------------------------- lista de escolha

    /// <summary>Lista rolável de opções com uma escolhida (amarela).</summary>
    private sealed class PickList : ScrollViewer
    {
        private readonly StackPanel _items = new();
        private readonly FrameworkElement _owner;
        private readonly List<Button> _buttons = new();

        public PickList(FrameworkElement owner, double maxHeight)
        {
            _owner = owner;
            MaxHeight = maxHeight;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Content = _items;
            Margin = new Thickness(0, 6, 0, 0);
        }

        public int Selected { get; private set; } = -1;

        public event Action<int>? Picked;

        public void SetItems(IReadOnlyList<string> names, int selected)
        {
            _items.Children.Clear();
            _buttons.Clear();
            for (int i = 0; i < names.Count; i++)
            {
                int index = i;
                var button = new Button
                {
                    Content = new TextBlock { Text = names[i], TextTrimming = TextTrimming.CharacterEllipsis },
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 4),
                };
                button.Click += (_, _) => Select(index, true);
                _buttons.Add(button);
                _items.Children.Add(button);
            }

            Select(names.Count == 0 ? -1 : Math.Clamp(selected, 0, names.Count - 1), false);
        }

        private void Select(int index, bool notify)
        {
            Selected = index;
            for (int i = 0; i < _buttons.Count; i++)
                _buttons[i].Style = (Style)_owner.FindResource(i == index ? "PrimaryButton" : "GhostButton");
            if (notify) Picked?.Invoke(index);
        }
    }
}
