using System.Diagnostics;

namespace MBExtreme.UI;

/// <summary>"Otimize com um especialista": abre a página dos pacotes no site (pedido, inclusos do plano e pagamento).</summary>
internal static class Specialist
{
    public static void Open()
    {
        try { Process.Start(new ProcessStartInfo(MBExtreme.Core.AppInfo.SiteUrl + "/especialista") { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { /* sem navegador padrão: nada a fazer */ }
    }
}
