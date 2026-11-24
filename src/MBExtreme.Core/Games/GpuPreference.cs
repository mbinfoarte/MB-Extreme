using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MBExtreme.Core.Games;

/// <summary>Placa de vídeo usada por um aplicativo (Configurações do Windows > Tela > Elementos gráficos).</summary>
public enum GpuPreferenceMode { Default = 0, PowerSaving = 1, HighPerformance = 2 }

public static class GpuPreference
{
    public static GpuPreferenceMode Parse(string? raw)
    {
        const string key = "GpuPreference=";
        if (string.IsNullOrEmpty(raw)) return GpuPreferenceMode.Default;
        int at = raw.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return GpuPreferenceMode.Default;
        int start = at + key.Length, end = start;
        while (end < raw.Length && char.IsDigit(raw[end])) end++;
        return int.TryParse(raw.AsSpan(start, end - start), out int n) && n is >= 0 and <= 2 ? (GpuPreferenceMode)n : GpuPreferenceMode.Default;
    }

    public static string Format(GpuPreferenceMode mode) => $"GpuPreference={(int)mode};";

    public static string Name(GpuPreferenceMode mode) => mode switch
    {
        GpuPreferenceMode.PowerSaving => "Economia de energia",
        GpuPreferenceMode.HighPerformance => "Alto desempenho",
        _ => "Deixar o Windows decidir",
    };
}

public interface IGpuPreferenceStore
{
    GpuPreferenceMode Get(string exePath);
    void Set(string exePath, GpuPreferenceMode mode);
}

/// <summary>Lê e grava a preferência de GPU do usuário atual (chave do próprio Windows, sem driver).</summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryGpuPreferenceStore : IGpuPreferenceStore
{
    private const string KeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";

    public GpuPreferenceMode Get(string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return GpuPreference.Parse(key?.GetValue(exePath) as string);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return GpuPreferenceMode.Default; }
    }

    public void Set(string exePath, GpuPreferenceMode mode)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        if (mode == GpuPreferenceMode.Default) key.DeleteValue(exePath, false);
        else key.SetValue(exePath, GpuPreference.Format(mode), RegistryValueKind.String);
    }
}
