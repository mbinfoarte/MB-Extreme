using MBExtreme.Core.Licensing;
using Xunit;

namespace MBExtreme.Core.Tests;

public class IndicacoesTests
{
    [Theory]
    [InlineData(" ab-12 cd ", "AB12CD")]
    [InlineData("mbx7k2", "MBX7K2")]
    [InlineData("ÀB12", "B12")]
    [InlineData("ABCDEFGHIJKLMNOP", "ABCDEFGHIJKL")]
    public void Codigo_fica_so_com_letras_e_numeros(string typed, string expected) => Assert.Equal(expected, InviteClient.NormalizeCode(typed));

    [Fact]
    public void Avaliacao_aparece_so_a_partir_do_segundo_dia_e_uma_vez()
    {
        var now = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(RatingPrompt.ShouldShow(now, now.AddHours(-5), null, false, true));     // primeiro dia
        Assert.True(RatingPrompt.ShouldShow(now, now.AddDays(-1), null, false, true));       // 2º dia
        Assert.False(RatingPrompt.ShouldShow(now, now.AddDays(-3), null, true, true));       // já respondeu
        Assert.False(RatingPrompt.ShouldShow(now, now.AddDays(-3), now.AddDays(1), false, true)); // "agora não"
        Assert.True(RatingPrompt.ShouldShow(now, now.AddDays(-5), now.AddMinutes(-1), false, true));
        Assert.False(RatingPrompt.ShouldShow(now, now.AddDays(-3), null, false, false));     // sem conta
    }

    [Fact]
    public void Mensagem_pronta_leva_o_link_e_os_dias()
    {
        var me = new InviteSummary("AB12", "https://x/i/AB12", 0, 0, 0, 0, 7, 7, new());
        Assert.Contains("https://x/i/AB12", me.ShareText);
        Assert.Contains("7 dias", me.ShareText);
    }
}
