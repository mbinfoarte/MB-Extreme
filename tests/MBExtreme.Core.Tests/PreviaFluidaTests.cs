using System.Diagnostics;
using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Prévia da tela pela placa de vídeo e prévia da câmera com atraso mínimo.</summary>
public class PreviaFluidaTests
{
    private static readonly PixelRect Monitor2 = new(1920, 0, 2560, 1440);

    [Fact]
    public void Previa_da_tela_usa_a_placa_e_sai_crua_no_tamanho_exato()
    {
        var args = ScreenPreview.RawArgs(1, null, null, 1280, 720);
        Assert.Contains("ddagrab=output_idx=1:framerate=60:draw_mouse=1", args);   // 60 qps: fluida como a tela
        string vf = args[args.ToList().IndexOf("-vf") + 1];
        Assert.Equal("hwdownload,format=bgra,scale=1280:720:flags=bilinear,format=bgra", vf);
        Assert.Contains("rawvideo", args);   // sem JPEG: nítida e sem gastar processador comprimindo
        Assert.Equal("pipe:1", args[^1]);
    }

    [Fact]
    public void Recorte_da_janela_e_filtro_de_cor_entram_na_previa()
    {
        var args = ScreenPreview.RawArgs(0, new PixelRect(100, 50, 1280, 720), "eq=brightness=0.1", 960, 540);
        string vf = args[args.ToList().IndexOf("-vf") + 1];
        Assert.Contains("crop=1280:720:100:50,scale=960:540", vf);
        Assert.EndsWith("eq=brightness=0.1,format=bgra", vf);
    }

    [Theory]
    [InlineData(600, 2560, 1440, 640, 360)]
    [InlineData(800, 2560, 1440, 960, 540)]
    [InlineData(1100, 2560, 1440, 1280, 720)]
    [InlineData(1500, 2560, 1440, 1920, 1080)]   // espaço grande: Full HD
    [InlineData(1650, 3440, 1440, 1920, 804)]    // ultrawide: Full HD de largura, na proporção da tela
    [InlineData(1500, 1080, 1920, 1080, 1920)]   // monitor em pé: não passa do tamanho da tela
    [InlineData(1500, 1281, 721, 1280, 720)]
    public void Tamanho_da_previa_acompanha_o_espaco_na_tela(double shown, int w, int h, int ew, int eh)
    {
        Assert.Equal((ew, eh), ScreenPreview.Size(shown, w, h));
    }

    [Fact]
    public void Previa_de_janela_usa_o_windows_graphics_capture_e_reduz_na_placa()
    {
        var args = ScreenPreview.WindowArgs(0x1A2B3C, "eq=brightness=0.1", 1920, 1080).ToList();
        string grab = args[args.IndexOf("-i") + 1];
        Assert.StartsWith("gfxcapture=hwnd=1715004:max_framerate=60:", grab);
        Assert.Contains(":width=1920:height=1080:resize_mode=scale_aspect", grab);   // reduz na placa, sem distorcer
        Assert.Contains("display_border=0", grab);                                   // sem a borda amarela do Windows na janela
        Assert.Equal("hwdownload,format=bgra,eq=brightness=0.1,format=bgra", args[args.IndexOf("-vf") + 1]);
        Assert.Equal("pipe:1", args[^1]);
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

        var screen = ScreenPreview.RawArgs(0, new PixelRect(100, 50, 1280, 720), null, 960, 540).ToList();
        string screenVf = screen[screen.IndexOf("-vf") + 1].Replace("hwdownload,format=bgra,", "format=bgra,");
        Assert.True(Frames(ffmpeg, "color=c=red:s=2560x1440:r=20:d=1", screenVf.Replace(",format=bgra", ",format=yuvj420p").Replace("format=yuvj420p,crop", "format=bgra,crop")) >= 15);

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
