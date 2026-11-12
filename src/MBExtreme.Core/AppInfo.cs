namespace MBExtreme.Core;

/// <summary>Nome e versão do produto, em um só lugar.</summary>
public static class AppInfo
{
    public const string Name = "MB Extreme (Beta 01)";
    public const string ShortName = "MB Extreme";
    public const string Channel = "Beta";
    public const string Version = "0.9.0";
    public const string DisplayVersion = "Beta 01";

    /// <summary>
    /// Endereço do servidor (https://...) e chave pública de licença que o programa usa quando o usuário não configurou nada.
    /// Preencha antes de gerar a versão de venda: assim o cliente só abre o programa e o acesso remoto já funciona.
    /// Servidor: Cloud Function "licenseApi" do site (projeto Firebase people-api-493900). A chave pública não é segredo;
    /// a privada fica só no Firebase (segredo MBX_LICENSE_KEY), gerada por scripts/gerar-chaves.mjs do repositório do site.
    /// </summary>
    public const string DefaultServerUrl = "https://southamerica-east1-people-api-493900.cloudfunctions.net/licenseApi";

    /// <summary>Dias de teste grátis do Pro em cada PC (sem pedir conta).</summary>
    public const int TrialDays = 3;

    /// <summary>
    /// Liga a separação Grátis × Pro para todo mundo (na versão de venda, true). Enquanto for false, só quem ligar "Aplicar licença"
    /// nas configurações vê os limites — o beta continua aberto.
    /// </summary>
    public const bool EnforceLicense = false;

    /// <summary>Repositório do GitHub com as Releases (atualização sem servidor; só funciona com o repositório público).</summary>
    public const string UpdateRepository = "mbinfoarte/MB-Extreme-Download";

    /// <summary>
    /// Onde o programa procura versão nova: o repositório de downloads e, enquanto a separação não estiver completa (sem o segredo
    /// RELEASES_TOKEN o CI publica no próprio repositório), o MB-Extreme. Vale a de maior compilação entre os dois.
    /// </summary>
    public static readonly string[] UpdateRepositories = { "mbinfoarte/MB-Extreme-Download", "mbinfoarte/MB-Extreme" };   // repositório público só de downloads; o código-fonte fica privado em mbinfoarte/MB-Extreme
    /// <summary>Site principal (MB Hub): cadastro, painel, planos e ativação do PC.</summary>
    public const string SiteUrl = "https://www.mbinfoarte.com.br";

    public const string DefaultPublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAER0fp7Wn1uvIzT/ZbZ2WVsMujzLnONCNxTB+2agpogzmlbBQTNoNm21t1f0EtZoHG2ncxg7i4T51O4Iotb2X04w==";
}
