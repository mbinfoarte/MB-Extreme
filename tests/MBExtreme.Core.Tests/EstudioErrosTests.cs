using MBExtreme.Core.Recording;
using Xunit;

namespace MBExtreme.Core.Tests;

public class EstudioErrosTests
{
    [Theory]
    [InlineData(@"Error opening input file \\.\pipe\mbx-overlay-abc.", "não abriu a camada do logo (Invalid argument)")]
    [InlineData(@"Error opening input file \\.\pipe\mbx-audio-abc.", "não abriu o áudio (Invalid argument)")]
    [InlineData("Error opening input file video=Câmera 1.", "não abriu a câmera Câmera 1 (Invalid argument)")]
    [InlineData("Error opening input file ddagrab=output_idx=0:framerate=60:draw_mouse=1.", "não abriu a captura da tela (Invalid argument)")]
    [InlineData(@"Error opening input file C:\Users\x\pix.png.", "não abriu o arquivo pix.png (Invalid argument)")]
    public void Diz_qual_entrada_nao_abriu(string previous, string expected) =>
        Assert.Equal(expected, StudioErrors.Explain(previous, "Error opening input files: Invalid argument"));

    [Fact]
    public void Outras_mensagens_ficam_como_estao()
    {
        Assert.Equal("Conversion failed!", StudioErrors.Explain("x", "Conversion failed!"));
        Assert.Equal("Error opening input files: I/O error", StudioErrors.Explain(null, "Error opening input files: I/O error"));
    }
}
