using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MBExtreme.Core.Windows;

/// <summary>O Windows está esperando reiniciar para terminar uma atualização ou instalação?</summary>
[SupportedOSPlatform("windows")]
public static class RebootState
{
    /// <summary>Chaves que o próprio Windows cria enquanto falta reiniciar.</summary>
    public static readonly string[] Keys =
    {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired",
    };

    public static bool IsPending()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            return Keys.Any(k => { using var key = hklm.OpenSubKey(k); return key is not null; });
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return false; }
    }
}
