using System.IO;
using System.Windows;
using MBExtreme.Core.Games;
using MBExtreme.Core.Licensing;
using MBExtreme.Core.Tweaks;

namespace MBExtreme.UI;

/// <summary>
/// Quando o teste grátis ou o plano Pro acaba, desfaz sozinho o que o MB Extreme otimizou (ajustes de desempenho, jogos e rede, e as
/// otimizações por jogo) usando os valores originais guardados. Só age com a aplicação de licença ligada e o servidor configurado.
/// </summary>
internal sealed class ProLock
{
    private bool _running;

    public async Task CheckAsync()
    {
        var license = AppServices.License;
        license.RefreshTrial();
        // só desfaz o que foi otimizado com teste/plano de uma conta; quem nunca entrou (beta antigo) não perde nada de repente
        if (!license.EverSignedIn) return;
        if (_running || !LicenseExpiry.ShouldRevert(license.Config.Enforce, license.Config.Configured, license.Status, license.Trial.Active)) return;

        var tweaks = AppServices.Tweaks.Where(t => t.HasBackup && LicenseExpiry.ProTweakCategories.Contains(t.Category)).ToList();
        var games = AppServices.GameLibrary.Optimized();
        if (tweaks.Count == 0 && games.Count == 0) return;

        _running = true;
        try
        {
            var result = await Task.Run(() => RevertAll(tweaks, games));
            string text = $"O seu teste grátis ou plano pago terminou, então o MB Extreme desfez as otimizações: {result.Tweaks} ajuste(s) do Windows e {result.Games} jogo(s) voltaram ao original.";
            if (result.Failed.Count > 0) text += $"\n\nNão foi possível desfazer {result.Failed.Count} item(ns) (execute como administrador):\n" + string.Join("\n", result.Failed.Take(5));
            text += "\n\nAssine um plano (Essencial, Gamer, Creator ou Técnico) para usar de novo.";
            MessageBox.Show(text, "MB Extreme (Beta 01)", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            _running = false;
        }
    }

    private static (int Tweaks, int Games, List<string> Failed) RevertAll(IReadOnlyList<Tweak> tweaks, IReadOnlyList<(string Id, GameSettings Settings)> games)
    {
        var failed = new List<string>();
        var outcomes = TweakRunner.Run(tweaks.Select(t => new TweakWork(t, false)).ToList());
        failed.AddRange(outcomes.Where(o => !o.Success).Select(o => $"{o.Tweak.Title}: {o.Error}"));

        int gamesDone = 0;
        foreach (var (id, settings) in games)
        {
            try
            {
                AppServices.GameOptimization.Undo(id);
                AppServices.GameDriver.Undo(id);
                if (settings.ExeName is { } exe)
                {
                    if (settings.QosApplied && QosCommands.IsSafeExe(exe)) AppServices.GameQos.Remove(exe);
                    if (settings.HighPriority) AppServices.GamePriority.Revert(exe);
                }
                AppServices.GameLibrary.Update(id, s => { s.QosApplied = false; s.HighPriority = false; s.OptimizedAt = null; });
                gamesDone++;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
            {
                failed.Add($"Jogo {id}: {ex.Message}");
            }
        }
        return (outcomes.Count(o => o.Success), gamesDone, failed);
    }
}
