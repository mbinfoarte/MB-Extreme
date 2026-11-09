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

/// <summary>Contas das animações: suavização, contagem da nota e cascata.</summary>
public class AnimacoesTests
{
    [Fact]
    public void Suavizacao_comeca_em_0_termina_em_1_e_nunca_passa()
    {
        Assert.Equal(0, MBExtreme.Core.Ui.Easing.OutCubic(0));
        Assert.Equal(1, MBExtreme.Core.Ui.Easing.OutCubic(1));
        Assert.Equal(1, MBExtreme.Core.Ui.Easing.OutCubic(5));
        Assert.True(MBExtreme.Core.Ui.Easing.OutCubic(0.5) > 0.5);   // rápida no começo
    }

    [Fact]
    public void Contagem_da_nota_chega_no_valor_certo()
    {
        var d = TimeSpan.FromMilliseconds(900);
        Assert.Equal(0, MBExtreme.Core.Ui.Easing.Count(0, 87, TimeSpan.Zero, d));
        Assert.Equal(87, MBExtreme.Core.Ui.Easing.Count(0, 87, d, d));
        Assert.Equal(87, MBExtreme.Core.Ui.Easing.Count(0, 87, TimeSpan.FromSeconds(5), d));
        Assert.Equal(87, MBExtreme.Core.Ui.Easing.Count(0, 87, TimeSpan.Zero, TimeSpan.Zero));
    }

    [Fact]
    public void Cascada_tem_limite_para_listas_grandes()
    {
        Assert.Equal(TimeSpan.Zero, MBExtreme.Core.Ui.Easing.Stagger(0));
        Assert.Equal(TimeSpan.FromMilliseconds(70), MBExtreme.Core.Ui.Easing.Stagger(2));
        Assert.Equal(TimeSpan.FromMilliseconds(420), MBExtreme.Core.Ui.Easing.Stagger(500));
    }
}
