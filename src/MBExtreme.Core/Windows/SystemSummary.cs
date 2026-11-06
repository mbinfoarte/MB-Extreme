using Microsoft.Win32;

namespace MBExtreme.Core.Windows;

/// <summary>Resumo rápido do computador pelo registro (sem WMI): nome do Windows e do processador.</summary>
public static class SystemSummary
{
    private const string CurrentVersion = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    public static string WindowsName()
    {
        using var key = Registry.LocalMachine.OpenSubKey(CurrentVersion);
        string product = key?.GetValue("ProductName") as string ?? "Windows";
        string display = key?.GetValue("DisplayVersion") as string ?? "";
        string build = key?.GetValue("CurrentBuild") as string ?? "";
        int ubr = key?.GetValue("UBR") is int u ? u : 0;
        return Describe(product, display, build, ubr);
    }

    /// <summary>O registro do Windows 11 ainda diz "Windows 10" no nome; a compilação 22000 em diante é Windows 11.</summary>
    public static string Describe(string product, string displayVersion, string build, int ubr)
    {
        if (int.TryParse(build, out int b) && b >= 22000) product = product.Replace("Windows 10", "Windows 11", StringComparison.Ordinal);
        string text = product;
        if (displayVersion.Length > 0) text += " " + displayVersion;
        if (build.Length > 0) text += $" (compilação {build}{(ubr > 0 ? "." + ubr : "")})";
        return text;
    }

    public static string ProcessorName()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? Environment.ProcessorCount + " núcleos";
    }
}
