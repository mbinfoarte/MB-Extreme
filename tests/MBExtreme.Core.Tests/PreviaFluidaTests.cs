using System.Diagnostics;
using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Prévia da tela pela placa de vídeo e prévia da câmera com atraso mínimo.</summary>
public class PreviaFluidaTests
{
    private static readonly PixelRect Monitor2 = new(1920, 0, 2560, 1440);

    [Fact]
    public void Previa_da_tela_usa_a_placa_e_sai_pequena_em_jpeg()
    {
        var args = ScreenPreview.Args(1, null, null);
        Assert.Contains("ddagrab=output_idx=1:framerate=20:draw_mouse=1", args);
        string vf = args[args.ToList().IndexOf("-vf") + 1];
        Assert.Equal("hwdownload,format=bgra,scale=640:-2:flags=fast_bilinear,format=yuvj420p", vf);
        Assert.Equal("1", args[args.ToList().IndexOf("-flush_packets") + 1]);
        Assert.Equal("pipe:1", args[^1]);
    }

    [Fact]
    public void Recorte_da_janela_e_filtro_de_cor_entram_na_previa()
    {
        var args = ScreenPreview.Args(0, new PixelRect(100, 50, 1280, 720), "eq=brightness=0.1");
        string vf = args[args.ToList().IndexOf("-vf") + 1];
        Assert.Contains("crop=1280:720:100:50,scale=640", vf);
        Assert.EndsWith("eq=brightness=0.1,format=yuvj420p", vf);
    }

    [Fact]
    public void Recorte_fica_dentro_da_tela_e_em_medidas_pares()
    {
        // janela do jogo na segunda tela, passando um pouco da borda
        var crop = ScreenPreview.CropWithin(Monitor2, new PixelRect(1920 + 2000, 101, 1001, 501))!;
        Assert.Equal(new PixelRect(2000, 101, 560, 500), crop);
        Assert.Null(ScreenPreview.CropWithin(Monitor2, Monitor2));                          // a tela inteira: sem recorte
        Assert.Null(ScreenPreview.CropWithin(Monitor2, new PixelRect(1920, 0, 10, 10)));    // pequeno demais
    }

    [Fact]
    public void Acha_a_tela_pelo_centro_da_janela()
    {
        var displays = new[]
        {
            new DisplayInfo(0, "\\\\.\\DISPLAY1", new PixelRect(0, 0, 1920, 1080), true),
            new DisplayInfo(1, "\\\\.\\DISPLAY2", Monitor2, false),
        };
        Assert.Equal(1, ScreenPreview.DisplayFor(displays, new PixelRect(1800, 100, 800, 600)));   // centro em x=2200
        Assert.Equal(0, ScreenPreview.DisplayFor(displays, new PixelRect(0, 0, 1920, 1080)));
        Assert.Equal(-1, ScreenPreview.DisplayFor(displays, new PixelRect(-5000, -5000, 100, 100)));
    }

    /// <summary>Os filtros da prévia (tela e câmera) rodam no FFmpeg de verdade, com imagens sintéticas no lugar da captura.</summary>
    [Fact]
    public void Filtros_das_previas_rodam_no_ffmpeg_de_verdade()
    {
        string? ffmpeg = new[] { "/usr/bin/ffmpeg", "/usr/local/bin/ffmpeg" }.FirstOrDefault(File.Exists);
        if (ffmpeg is null || OperatingSystem.IsWindows()) return;

        var screen = ScreenPreview.Args(0, new PixelRect(100, 50, 1280, 720), null).ToList();
        string screenVf = screen[screen.IndexOf("-vf") + 1].Replace("hwdownload,format=bgra,", "format=bgra,");
        Assert.True(Frames(ffmpeg, "color=c=red:s=2560x1440:r=20:d=1", screenVf) >= 15);

        var camera = CameraSource.LiveArgs("Webcam", "hflip").ToList();
        string cameraVf = camera[camera.IndexOf("-vf") + 1];
        int cameraFrames = Frames(ffmpeg, "color=c=green:s=1280x720:r=30:d=1", cameraVf);
        Assert.InRange(cameraFrames, 15, 25);   // 30 da câmera viram ~20 já no começo
    }

    private static int Frames(string ffmpeg, string source, string vf)
    {
        var info = new ProcessStartInfo(ffmpeg) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (string a in new[] { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", source, "-vf", vf, "-f", "image2pipe", "-c:v", "mjpeg", "-q:v", "6", "pipe:1" })
            info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        using var ms = new MemoryStream();
        var err = p.StandardError.ReadToEndAsync();
        p.StandardOutput.BaseStream.CopyTo(ms);
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, err.Result);
        return new MjpegSplitter().Push(ms.ToArray()).Count;
    }
}
