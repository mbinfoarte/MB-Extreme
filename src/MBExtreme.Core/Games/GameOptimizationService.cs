using MBExtreme.Core.Tweaks;

namespace MBExtreme.Core.Games;

public sealed record OptimizationResult(IReadOnlyList<string> Applied, IReadOnlyList<string> AlreadyOn, IReadOnlyList<string> Skipped, IReadOnlyList<string> Failed);

/// <summary>
/// Aplica o pacote de otimização de um jogo usando os ajustes do catálogo (com backup do valor original) e guarda o que ELE ligou,
/// para desfazer só isso depois. Só liga ajustes de risco baixo; o que já estava ligado antes não é desligado ao desfazer.
/// </summary>
public sealed class GameOptimizationService
{
    private readonly IReadOnlyList<Tweak> _tweaks;
    private readonly GameLibraryStore _store;

    public GameOptimizationService(IReadOnlyList<Tweak> tweaks, GameLibraryStore store)
    {
        _tweaks = tweaks;
        _store = store;
    }

    public OptimizationResult Apply(string gameId, string? exeName, OptimizationPlan plan)
    {
        var applied = new List<string>();
        var already = new List<string>();
        var skipped = new List<string>();
        var failed = new List<string>();

        foreach (string id in plan.TweakIds)
        {
            var tweak = _tweaks.FirstOrDefault(t => t.Id == id);
            if (tweak is null || tweak.Risk != TweakRisk.Low || !tweak.IsAvailable) { skipped.Add(id); continue; }
            try
            {
                if (tweak.IsApplied() == true) { already.Add(tweak.Title); continue; }
                tweak.Apply();
                applied.Add(id);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException or IOException)
            {
                failed.Add($"{tweak.Title}: {ex.Message}");
            }
        }

        var previous = _store.Get(gameId).AppliedTweaks;
        _store.Update(gameId, s =>
        {
            s.ProfileKey = plan.Profile.Key;
            s.AppliedTweaks = previous.Concat(applied).Distinct().ToList();
            s.OptimizedAt = DateTime.Now;
            if (exeName is not null) s.ExeName = exeName;
        });
        return new OptimizationResult(applied.Select(Title).ToList(), already, skipped, failed);
    }

    private string Title(string id) => _tweaks.FirstOrDefault(t => t.Id == id)?.Title ?? id;

    /// <summary>Desfaz o que esta otimização ligou, exceto o que outro jogo otimizado ainda usa.</summary>
    public OptimizationResult Undo(string gameId)
    {
        var reverted = new List<string>();
        var kept = new List<string>();
        var failed = new List<string>();
        foreach (string id in _store.Get(gameId).AppliedTweaks.ToList())
        {
            var tweak = _tweaks.FirstOrDefault(t => t.Id == id);
            if (tweak is null) continue;
            if (_store.UsedByOthers(id, gameId)) { kept.Add(tweak.Title); continue; }
            try
            {
                tweak.Revert();
                reverted.Add(tweak.Title);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException or IOException)
            {
                failed.Add($"{tweak.Title}: {ex.Message}");
            }
        }

        _store.Update(gameId, s => { s.AppliedTweaks = new List<string>(); s.OptimizedAt = null; });
        return new OptimizationResult(reverted, kept, Array.Empty<string>(), failed);
    }
}
