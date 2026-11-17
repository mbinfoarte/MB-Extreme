using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Ensaio: a live inteira montada de verdade, com a imagem final na tela, sem gravar nem transmitir nada.</summary>
public class EnsaioLiveTests
{
    private static readonly EncoderOption Av1 = new(VideoCodec.Av1, EncoderKind.Nvenc, "av1_nvenc");

    private static StudioRequest Rehearsal() => new()
    {
        Source = new CaptureSource(SourceKind.Monitor, 0, new PixelRect(0, 0, 1920, 1080), "Tela 1"),
        OutputWidth = 1920, OutputHeight = 1080, Fps = 60, Encoder = Av1, BitrateMbps = 8, WorkingDirectory = "/tmp/x",
        Rehearsal = true, ProgramView = true, ProgramPipe = @"\\.\pipe\mbx-program-teste",
    };

    [Fact]
    public void Ensaio_conta_como_saida_e_passa_na_conferencia()
    {
        var r = Rehearsal();
        Assert.True(r.AnyOutput);
        Assert.Null(StudioCommand.Validate(r));
        Assert.False((r with { Rehearsal = false }).AnyOutput);   // sem ensaio e sem saída: continua pedindo uma opção
    }

    [Fact]
    public void Ensaio_codifica_de_verdade_mas_descarta_e_mostra_a_imagem_no_ar()
    {
        var args = StudioCommand.Build(Rehearsal()).ToList();
        Assert.Contains("av1_nvenc", args);                                       // mesmo peso da live (codificador de verdade)
        int nul = args.LastIndexOf("null");
        Assert.True(nul > 0 && args[nul - 1] == "-f" && args[nul + 1] == "-");   // saída descartada
        Assert.DoesNotContain("tee", args);
        Assert.DoesNotContain(args, a => a.Contains("rtmp", StringComparison.OrdinalIgnoreCase) || a.EndsWith(".mkv") || a.EndsWith(".mp4"));
        Assert.Contains(@"\\.\pipe\mbx-program-teste", args);                     // a imagem "No ar" chega na tela
    }
}
