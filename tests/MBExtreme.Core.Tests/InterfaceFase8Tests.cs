using MBExtreme.Core.Ui;
using Xunit;

namespace MBExtreme.Core.Tests;

/// <summary>Fase 8: nomes para o Narrador (texto visível, dica ou etiqueta; nunca só o ícone) e escala da interface.</summary>
public class InterfaceFase8Tests
{
    [Theory]
    [InlineData("Analisar o PC", true)]
    [InlineData("", false)]          // só o ícone de "voltar" da fonte de símbolos
    [InlineData("  ", false)]
    [InlineData(" Voltar", true)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    [InlineData("Licença: Grátis", true)]
    public void Texto_legivel_para_o_narrador(string? text, bool readable) => Assert.Equal(readable, AccessibleText.IsReadable(text));

    [Fact]
    public void Botao_so_com_icone_usa_a_dica()
    {
        Assert.Equal("Voltar", AccessibleText.Pick("", " Voltar ", "etiqueta"));
        Assert.Equal("Salvar", AccessibleText.Pick("Salvar", "dica"));
        Assert.Equal("Recommended", AccessibleText.Pick(null, null, "Recommended"));
        Assert.Null(AccessibleText.Pick("", null, null));
    }

    [Fact]
    public void Escala_sobe_e_desce_dentro_dos_limites()
    {
        Assert.Equal(1.1, AccessibleText.Step(1.0, +1));
        Assert.Equal(0.9, AccessibleText.Step(1.0, -1));
        Assert.Equal(0.9, AccessibleText.Step(0.9, -1));   // não passa do menor
        Assert.Equal(1.5, AccessibleText.Step(1.5, +1));   // nem do maior
        Assert.Equal(1.1, AccessibleText.Step(1.37, +1));  // valor estranho: parte do 100%
    }
}
