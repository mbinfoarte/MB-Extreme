using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MBExtreme.Core.Devices;

/// <summary>
/// Testes de som: um tom no alto-falante esquerdo, no direito ou nos dois, e o microfone (nível ao vivo e uma gravação curta que toca de volta).
/// Só usa os aparelhos padrão do Windows; nada é configurado.
/// </summary>
public static class AudioTests
{
    /// <summary>Nível de 0 a 1 a partir do pico (em escala de decibéis: -60 dB = 0, 0 dB = 1), como os medidores de volume.</summary>
    public static double LevelOf(float peak)
    {
        if (peak <= 0) return 0;
        double db = 20 * Math.Log10(peak);
        return Math.Clamp((db + 60) / 60, 0, 1);
    }

    /// <summary>Maior valor absoluto das amostras de 16 bits.</summary>
    public static float Peak16(byte[] buffer, int bytes)
    {
        int max = 0;
        for (int i = 0; i + 1 < bytes; i += 2)
        {
            int v = Math.Abs((int)BitConverter.ToInt16(buffer, i));
            if (v > max) max = v;
        }
        return max / 32768f;
    }

    /// <summary>Maior valor absoluto das amostras em ponto flutuante (formato normal do Windows).</summary>
    public static float PeakFloat(byte[] buffer, int bytes)
    {
        float max = 0;
        for (int i = 0; i + 3 < bytes; i += 4) max = Math.Max(max, Math.Abs(BitConverter.ToSingle(buffer, i)));
        return Math.Min(1, max);
    }

    /// <summary>Toca um tom de 440 Hz: pan -1 = só esquerdo, 1 = só direito, 0 = os dois.</summary>
    public static void PlayTone(float pan, TimeSpan duration, CancellationToken ct = default)
    {
        var tone = new SignalGenerator(44100, 1) { Frequency = 440, Gain = 0.25, Type = SignalGeneratorType.Sin }.Take(duration);
        var panned = new PanningSampleProvider(tone) { Pan = pan };
        using var output = new WasapiOut(AudioClientShareMode.Shared, 80);
        output.Init(panned);
        output.Play();
        while (output.PlaybackState == PlaybackState.Playing && !ct.IsCancellationRequested) Thread.Sleep(50);
    }

    public static string DefaultOutputName() => DeviceName(DataFlow.Render);
    public static string DefaultInputName() => DeviceName(DataFlow.Capture);

    private static string DeviceName(DataFlow flow)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            return device.FriendlyName;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return flow == DataFlow.Render ? "Nenhuma saída de som encontrada" : "Nenhum microfone encontrado";
        }
    }
}

/// <summary>Microfone: avisa o nível ao vivo e guarda os últimos segundos para tocar de volta.</summary>
public sealed class MicTester : IDisposable
{
    private readonly WasapiCapture _capture;
    private readonly MemoryStream _recording = new();
    private bool _recordingOn;

    public event Action<double>? Level;

    public MicTester()
    {
        _capture = new WasapiCapture();
        _capture.DataAvailable += (_, e) =>
        {
            float peak = _capture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat ? AudioTests.PeakFloat(e.Buffer, e.BytesRecorded)
                : _capture.WaveFormat.BitsPerSample == 16 ? AudioTests.Peak16(e.Buffer, e.BytesRecorded) : 0;
            Level?.Invoke(AudioTests.LevelOf(peak));
            if (_recordingOn) lock (_recording) _recording.Write(e.Buffer, 0, e.BytesRecorded);
        };
    }

    public void Start() => _capture.StartRecording();

    /// <summary>Grava alguns segundos e toca de volta.</summary>
    public void RecordAndPlay(TimeSpan length, CancellationToken ct)
    {
        lock (_recording) _recording.SetLength(0);
        _recordingOn = true;
        Thread.Sleep(length);
        _recordingOn = false;
        byte[] data;
        lock (_recording) data = _recording.ToArray();
        if (data.Length == 0) return;
        using var provider = new RawSourceWaveStream(new MemoryStream(data), _capture.WaveFormat);
        using var output = new WasapiOut(AudioClientShareMode.Shared, 100);
        output.Init(provider);
        output.Play();
        while (output.PlaybackState == PlaybackState.Playing && !ct.IsCancellationRequested) Thread.Sleep(50);
    }

    public void Dispose()
    {
        try { _capture.StopRecording(); } catch (InvalidOperationException) { }
        _capture.Dispose();
        _recording.Dispose();
    }
}
