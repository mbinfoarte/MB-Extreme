using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

public class PrevisaoAoVivoTests
{
    private static byte[] Jpeg(params byte[] body) => new byte[] { 0xFF, 0xD8 }.Concat(body).Concat(new byte[] { 0xFF, 0xD9 }).ToArray();

    [Fact]
    public void Separa_imagens_que_chegam_inteiras_ou_em_pedacos()
    {
        var a = Jpeg(1, 2, 3);
        var b = Jpeg(9, 8, 7, 6);
        var all = a.Concat(b).ToArray();

        var one = new MjpegSplitter();
        var frames = one.Push(all);
        Assert.Equal(2, frames.Count);
        Assert.Equal(a, frames[0]);
        Assert.Equal(b, frames[1]);

        var pieces = new MjpegSplitter();
        var got = new List<byte[]>();
        foreach (byte x in all) got.AddRange(pieces.Push(new[] { x })); // um byte por vez
        Assert.Equal(2, got.Count);
        Assert.Equal(b, got[1]);
    }

    [Fact]
    public void Ignora_lixo_antes_da_imagem_e_espera_o_fim()
    {
        var s = new MjpegSplitter();
        Assert.Empty(s.Push(new byte[] { 0, 1, 2, 0xFF }));
        Assert.Empty(s.Push(new byte[] { 0xD8, 5, 5 }));
        var done = s.Push(new byte[] { 0xFF, 0xD9 });
        Assert.Single(done);
        Assert.Equal(new byte[] { 0xFF, 0xD8, 5, 5, 0xFF, 0xD9 }, done[0]);
    }

    [Fact]
    public void Comando_ao_vivo_leva_o_filtro_da_camera_e_sai_em_jpeg_pelo_pipe()
    {
        var args = CameraSource.LiveArgs("Webcam X", "hflip,format=yuv420p,eq=brightness=0.05:contrast=1:saturation=1:gamma=1,format=bgra");
        Assert.Equal("pipe:1", args[^1]);
        Assert.Contains("mjpeg", args);
        string vf = args[args.ToList().IndexOf("-vf") + 1];
        Assert.StartsWith("hflip,", vf);
        Assert.EndsWith("scale=480:-2,format=yuvj420p", vf);
        Assert.Contains("dshow", args);
        Assert.DoesNotContain("-rw_timeout", args);
        Assert.Contains("-rw_timeout", CameraSource.LiveArgs("http://10.0.0.2/v", null));
    }

    [Fact]
    public void Cores_da_previa_seguem_brilho_saturacao_e_nao_mexem_no_neutro()
    {
        byte[] pixel() => new byte[] { 100, 150, 200, 255 };   // B G R A
        var same = pixel();
        ColorPreview.Apply(same, 1, 1, 4, ImageFilter.None);
        Assert.Equal(pixel(), same);

        var brighter = pixel();
        ColorPreview.Apply(brighter, 1, 1, 4, new ImageFilter(Brightness: 0.1));
        Assert.True(brighter[2] > 200 && brighter[0] > 100);

        var gray = pixel();
        ColorPreview.Apply(gray, 1, 1, 4, new ImageFilter(Saturation: 0));
        Assert.InRange(Math.Abs(gray[0] - gray[2]), 0, 2);   // sem cor: B, G e R quase iguais
        Assert.Equal(255, gray[3]);
    }
}
