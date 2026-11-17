using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MBExtreme.Core.Memory;

/// <summary>Decide quando a limpeza automática de memória deve rodar.</summary>
public static class MemoryAutoPolicy
{
    public static readonly TimeSpan DefaultCooldown = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Limpa quando o uso passa do limite e já se passou o intervalo mínimo desde a última limpeza
    /// (limpar em sequência só faz o Windows recarregar tudo e piora o desempenho).
    /// </summary>
    public static bool ShouldClean(double usedPercent, int thresholdPercent, DateTime? lastCleanUtc, DateTime nowUtc, TimeSpan? cooldown = null)
    {
        if (thresholdPercent is < 50 or > 98) return false;
        if (usedPercent < thresholdPercent) return false;
        return lastCleanUtc is null || nowUtc - lastCleanUtc.Value >= (cooldown ?? DefaultCooldown);
    }
}

/// <summary>Nome do programa da janela em primeiro plano (para a limpeza automática não mexer no que você está usando).</summary>
[SupportedOSPlatform("windows")]
public static class ForegroundProcess
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    public static string? Name()
    {
        try
        {
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero) return null;
            GetWindowThreadProcessId(window, out uint pid);
            return pid == 0 ? null : System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
