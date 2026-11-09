using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

public class CameraCelularTests
{
    [Theory]
    [InlineData("rtsp://192.168.0.10:8554/live", true)]
    [InlineData("http://192.168.0.10:8080/video", true)]
    [InlineData("https://celular.local/stream.mjpg?x=1", true)]
    [InlineData("rtsp://usuario:senha@192.168.0.10/h264", true)]
    [InlineData("rtsp://", false)]
    [InlineData("http://host com espaço/x", false)]
    [InlineData("http://host/x\"; calc", false)]
    [InlineData("ftp://host/x", false)]
    public void Endereco_de_camera_na_rede_e_validado(string url, bool valid)
    {
        Assert.Equal(valid, CameraSource.IsValidStream(url));
        if (CameraSource.IsStream(url)) Assert.Equal(valid, CameraOverlay.IsValidName(url));
    }

    [Fact]
    public void Webcam_continua_usando_dshow_e_stream_usa_o_endereco()
    {
        var dshow = CameraSource.InputArgs("Logitech C920");
        Assert.Contains("dshow", dshow);
        Assert.Equal("video=Logitech C920", dshow[^1]);

        var rtsp = CameraSource.InputArgs("rtsp://192.168.0.10:8554/live");
        Assert.DoesNotContain("dshow", rtsp);
        Assert.Contains("tcp", rtsp);
        Assert.Equal("rtsp://192.168.0.10:8554/live", rtsp[^1]);

        var http = CameraSource.InputArgs("http://192.168.0.10:8080/video");
        Assert.DoesNotContain("-rtsp_transport", http);
    }

    [Fact]
    public void Imagem_unica_para_testar_a_camera()
    {
        var args = CameraSource.StillArgs("Camera X", @"C:\t\c.png", 640);
        Assert.Contains("-frames:v", args);
        Assert.Equal(@"C:\t\c.png", args[^1]);
        Assert.Contains("scale=640:-2", args);
        Assert.DoesNotContain("-rw_timeout", args);
        Assert.Contains("-rw_timeout", CameraSource.StillArgs("http://10.0.0.2/v", "x.png"));
    }

    [Fact]
    public void Endereco_mostrado_nao_expoe_a_senha()
    {
        string shown = CameraSource.Display("rtsp://ana:segredo@192.168.0.10:8554/live");
        Assert.DoesNotContain("segredo", shown);
        Assert.Equal("rtsp://192.168.0.10:8554/live", shown);
        Assert.Equal("Logitech", CameraSource.Display("Logitech"));
    }

    [Fact]
    public void Camera_na_rede_entra_no_comando_do_estudio_no_lugar_do_dshow()
    {
        var args = CameraSource.InputArgs("http://192.168.0.10:8080/video");
        Assert.Equal("-i", args[^2]);
    }

    [Fact]
    public void Explicacoes_apontam_a_causa()
    {
        Assert.Contains("usuário e senha", CameraSource.Explain("rtsp://x/y", "Server returned 401 Unauthorized"));
        Assert.Contains("mesma rede", CameraSource.Explain("http://x/y", "Connection timed out"));
        Assert.Contains("em uso", CameraSource.Explain("Webcam", "Could not run graph (sometimes caused by a device already in use)"));
    }
}
