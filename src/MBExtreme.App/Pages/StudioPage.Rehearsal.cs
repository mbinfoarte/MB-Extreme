using System.ComponentModel;
using System.IO;
using System.Windows;
using MBExtreme.Core.Recording;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Ensaio: liga a live inteira de verdade — cenas, câmeras, sobreposições, Pix, legendas, transições e o mesmo codificador da live —
/// e mostra a imagem final no quadro "No ar", sem transmitir, gravar nem guardar replay. Serve para conferir como vai aparecer
/// (e se o PC aguenta) antes de mandar para o público. Dá para trocar de cena e testar a transição durante o ensaio.
/// </summary>
public sealed partial class StudioPage
{
    /// <summary>A sessão (a próxima ou a que está no ar) é um ensaio.</summary>
    private bool _rehearsing;

    private async Task BeginRehearsalAsync()
    {
        if (_studio.IsRunning || AppServices.Recorder.IsRecording) return;
        if (_ffmpegPath is null || _encoders.Count == 0)
        {
            SetStatus("O motor de vídeo ainda está sendo preparado ou a placa ainda está sendo testada. Aguarde alguns segundos.", "WarnBrush");
            return;
        }
        if (_sourceTab == 1 && !await RefreshChosenWindowAsync()) return;

        _rehearsing = true;
        var (config, error) = BuildConfig(write: true);
        if (config is null)
        {
            _rehearsing = false;
            if (error is not null) SetStatus(error, "WarnBrush");
            return;
        }

        _start.IsEnabled = false;
        SetStatus("Começando o ensaio...");
        try
        {
            await _studio.StartAsync(_ffmpegPath, config);
            SetStatus($"Ensaio no ar só para você: é assim que a live vai sair ({config.Encoder.Title}). Nada está sendo transmitido nem gravado. " +
                      "Troque de cena e teste a transição à vontade; quando estiver bom, clique em \"Entrar no ar\".", "OkBrush");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or Win32Exception)
        {
            _rehearsing = false;
            SetStatus("Não foi possível começar o ensaio: " + ex.Message, "BadBrush");
        }
        RefreshState();
    }

    /// <summary>Do ensaio para o ar: a mesma montagem passa a gravar/transmitir (a captura reinicia por um instante).</summary>
    private async Task GoLiveFromRehearsalAsync()
    {
        if (!_studio.IsRunning || _studio.Session.Request is not { Rehearsal: true } || _applying) return;
        if (!_outRecord && !_outStream && !_outReplay)
        {
            SetStatus("Ligue o que fazer ao entrar no ar (Gravar, Transmitir ou Replay, logo abaixo) e clique de novo.", "WarnBrush");
            return;
        }
        _rehearsing = false;
        var (config, error) = BuildConfig(write: true);
        if (config is null)
        {
            _rehearsing = true;
            SetStatus(error ?? "Não foi possível entrar no ar.", "WarnBrush");
            return;
        }
        if (config.Stream is { } target && MessageBox.Show($"Entrar no ar para {target.DisplayName}?\n\nQuem estiver assistindo passa a ver exatamente o que está no ensaio.",
                "Entrar no ar", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            _rehearsing = true;
            return;
        }

        PersistSettings(config.Stream);
        _applying = true;
        SetStatus("Entrando no ar...");
        try
        {
            await _studio.RestartAsync(config);
            var parts = new List<string>();
            if (config.Record) parts.Add("gravando");
            if (config.Stream is { } s) parts.Add("transmitindo para " + s.DisplayName);
            if (config.Replay) parts.Add("replay ligado");
            SetStatus("No ar: " + string.Join(", ", parts) + ".", "OkBrush");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or Win32Exception)
        {
            SetStatus("Não foi possível entrar no ar (o estúdio parou): " + StudioCommand.MaskSecrets(ex.Message, config.Stream, config.VerticalStream), "BadBrush");
        }
        finally
        {
            _applying = false;
        }
        RefreshState();
    }
}
