using MBExtreme.Core.Support;
using Xunit;

namespace MBExtreme.Core.Tests;

public class SuporteTests
{
    [Theory]
    [InlineData("https://download.anydesk.com/AnyDesk.exe", true)]
    [InlineData("https://anydesk.com/pt/downloads", true)]
    [InlineData("http://download.anydesk.com/AnyDesk.exe", false)]
    [InlineData("https://anydesk.com.evil.com/AnyDesk.exe", false)]
    [InlineData("https://evilanydesk.com/AnyDesk.exe", false)]
    public void So_baixa_do_dominio_oficial_por_https(string url, bool expected) =>
        Assert.Equal(expected, AnyDeskRules.IsOfficialUrl(new Uri(url)));

    [Theory]
    [InlineData("Valid", "CN=AnyDesk Software GmbH, O=AnyDesk Software GmbH, C=DE", true)]
    [InlineData("Valid", "CN=philandro Software GmbH, C=DE", true)]
    [InlineData("HashMismatch", "CN=AnyDesk Software GmbH", false)]
    [InlineData("NotSigned", "", false)]
    [InlineData("Valid", "CN=Outro Fabricante Ltda", false)]
    public void Assinatura_precisa_ser_valida_e_do_fabricante(string status, string subject, bool expected) =>
        Assert.Equal(expected, AnyDeskRules.IsTrustedSigner(status, subject));

    [Theory]
    [InlineData("123456789", "123456789")]
    [InlineData("1234567890\r\n", "1234567890")]
    [InlineData("ID: 987654321 ok", "987654321")]
    [InlineData("sem id", null)]
    [InlineData("12345", null)]
    public void Id_e_lido_da_saida_do_programa(string output, string? expected) =>
        Assert.Equal(expected, AnyDeskRules.ParseId(output));

    [Fact]
    public void Id_e_formatado_em_grupos()
    {
        Assert.Equal("123 456 789", AnyDeskRules.FormatId("123456789"));
        Assert.Equal("1 234 567 890", AnyDeskRules.FormatId("1234567890"));
    }

    [Theory]
    [InlineData("123 456 789", "anydesk:123456789")]
    [InlineData("1234567890", "anydesk:1234567890")]
    [InlineData("123 456 78", null)]
    [InlineData("123456789 && calc", null)]
    [InlineData("abc", null)]
    [InlineData("", null)]
    public void Endereco_de_conexao_so_aceita_numeros(string input, string? expected) =>
        Assert.Equal(expected, AnyDeskRules.ConnectUri(input));

    [Fact]
    public void Mensagem_de_suporte_traz_id_nome_e_o_pedido()
    {
        string message = AnyDeskRules.SupportMessage("Maria", "123456789", "Computador lento", "Beta 01");
        Assert.Contains("123 456 789", message);
        Assert.Contains("Maria", message);
        Assert.Contains("Computador lento", message);
        Assert.Contains("aceitar a conexão", message);
        Assert.Contains("ainda não gerado", AnyDeskRules.SupportMessage("", null, "", "Beta 01"));
    }
}
