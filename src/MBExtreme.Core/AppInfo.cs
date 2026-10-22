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
    /// A chave pública sai de https://SEU-DOMINIO/v1/public-key (não é segredo).
    /// </summary>
    public const string DefaultServerUrl = "";
    public const string DefaultPublicKey = "";
}
