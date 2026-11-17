namespace MBExtreme.Core.Apps;

/// <summary>
/// Códigos de saída do winget (conferidos em github.com/microsoft/winget-cli, doc/windows/package-manager/winget/returnCodes.md)
/// traduzidos para frases em português.
/// </summary>
public static class WingetErrors
{
    public static (bool Ok, string Message, bool Reboot) Explain(int exitCode, bool upgrade) => unchecked((uint)exitCode) switch
    {
        0 => (true, upgrade ? "Atualizado." : "Instalado.", false),
        0x8A15002B => (true, "Já está na versão mais nova.", false),                       // APPINSTALLER_CLI_ERROR_UPDATE_NOT_APPLICABLE
        0x8A150061 => (true, "Já está instalado.", false),                                 // PACKAGE_ALREADY_INSTALLED
        0x8A150109 => (true, "Concluído; termina depois de reiniciar o computador.", true),  // INSTALL_REBOOT_REQUIRED_TO_FINISH
        0x8A15010A => (true, "Concluído; termina depois de reiniciar o computador.", true),  // INSTALL_REBOOT_REQUIRED_FOR_INSTALL
        0x8A150101 => (false, "O programa está aberto. Feche-o e tente de novo.", false),    // INSTALL_PACKAGE_IN_USE
        0x8A150102 => (false, "Outra instalação está em andamento no Windows. Espere terminar e tente de novo.", false), // INSTALL_INSTALL_IN_PROGRESS
        0x8A150103 => (false, "Um arquivo está em uso. Feche os programas abertos e tente de novo.", false), // INSTALL_FILE_IN_USE
        0x8A150104 => (false, "Falta uma dependência do programa.", false),                 // INSTALL_MISSING_DEPENDENCY
        0x8A150105 => (false, "Disco cheio: libere espaço e tente de novo.", false),         // INSTALL_DISK_FULL
        0x8A150106 => (false, "Sem memória suficiente para instalar.", false),              // INSTALL_INSUFFICIENT_MEMORY
        0x8A150107 => (false, "Sem internet: confira a conexão.", false),                    // INSTALL_NO_NETWORK
        0x8A150108 => (false, "O instalador pediu para falar com o suporte do programa.", false), // INSTALL_CONTACT_SUPPORT
        0x8A15010B => (true, "Concluído; o instalador já está reiniciando o computador.", true), // INSTALL_REBOOT_INITIATED
        0x8A15010C => (false, "O instalador foi cancelado.", false),                       // INSTALL_CANCELLED_BY_USER
        0x8A15010D => (true, "Já está instalado.", false),                                 // INSTALL_ALREADY_INSTALLED
        0x8A15010E => (false, "Já existe uma versão mais nova instalada.", false),         // INSTALL_DOWNGRADE
        0x8A15010F => (false, "Bloqueado pela política da empresa.", false),               // INSTALL_BLOCKED_BY_POLICY
        0x8A150110 => (false, "Falta uma dependência do sistema.", false),                 // INSTALL_DEPENDENCIES
        0x8A150111 => (false, "O programa está aberto. Feche-o e tente de novo.", false),    // INSTALL_PACKAGE_IN_USE_BY_APPLICATION
        0x8A150014 => (false, "Programa não encontrado no catálogo do winget.", false),     // NO_APPLICATIONS_FOUND
        0x8A150008 => (false, "Não deu para baixar o instalador. Confira a internet.", false), // DOWNLOAD_FAILED
        _ => (false, $"O winget terminou com erro (código 0x{unchecked((uint)exitCode):X8}).", false),
    };
}
