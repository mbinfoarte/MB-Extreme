using System.ComponentModel;
using MBExtreme.Core.Progress;

namespace MBExtreme.Core.Tweaks;

public sealed record TweakWork(Tweak Tweak, bool Apply);

public sealed record TweakOutcome(Tweak Tweak, bool Applied, bool Success, string? Error);

public static class TweakRunner
{
    /// <summary>Aplica ou restaura os ajustes um a um, relatando o progresso. Uma falha não interrompe os demais.</summary>
    public static IReadOnlyList<TweakOutcome> Run(
        IReadOnlyList<TweakWork> work,
        IProgress<ProgressEvent>? progress = null,
        CancellationToken ct = default)
    {
        var outcomes = new List<TweakOutcome>();
        for (int i = 0; i < work.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (tweak, apply) = work[i];
            progress?.Report(new ProgressEvent("Ajustes", 100.0 * i / work.Count,
                (apply ? "Aplicando: " : "Restaurando: ") + tweak.Title));

            try
            {
                if (apply) tweak.Apply();
                else tweak.Revert();
                outcomes.Add(new TweakOutcome(tweak, apply, true, null));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException
                                           or InvalidOperationException or IOException or Win32Exception or ArgumentException)
            {
                outcomes.Add(new TweakOutcome(tweak, apply, false, ex.Message));
            }
        }

        progress?.Report(new ProgressEvent("Ajustes", 100, "Concluído"));
        return outcomes;
    }
}
