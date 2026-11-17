namespace MBExtreme.Core.Tweaks;

public sealed record TweakProfile(string Name, string Description, IReadOnlyList<Tweak> Tweaks);

/// <summary>Conjuntos prontos de ajustes. Só entram ajustes de risco baixo e reversíveis.</summary>
public static class TweakProfiles
{
    public static IReadOnlyList<TweakProfile> Build(IReadOnlyList<Tweak> all)
    {
        IReadOnlyList<Tweak> Pick(Func<Tweak, bool> filter) =>
            all.Where(t => t.Risk == TweakRisk.Low && filter(t)).ToList();

        var game = Pick(t => t.Recommended && t.Category is TweakCategory.Performance or TweakCategory.Gaming or TweakCategory.Network);

        var competitive = Pick(t =>
            (t.Recommended && t.Category is TweakCategory.Performance or TweakCategory.Gaming or TweakCategory.Network)
            || t.Id is "power-min-cpu" or "power-pcie-aspm" or "power-core-parking");

        var weak = Pick(t =>
            (t.Recommended && t.Category is TweakCategory.Performance or TweakCategory.Gaming or TweakCategory.Network
                or TweakCategory.Visual or TweakCategory.Privacy)
            || t.Id is "perf-svc-diagtrack" or "perf-svc-maps");

        return new[]
        {
            new TweakProfile("Jogo",
                "O básico que ajuda em qualquer PC: prioridade para o jogo, sem gravação em segundo plano e sem limites desnecessários de rede.", game),
            new TweakProfile("Competitivo (na tomada)",
                "Jogo + processador sem economia, núcleos sempre disponíveis e PCI Express/USB sem suspensão. Gasta mais energia; use ligado na tomada.", competitive),
            new TweakProfile("PC fraco",
                "Jogo + visual leve (sem transparência e animações), menos serviços de telemetria e menos anúncios, para liberar CPU, RAM e GPU.", weak),
        };
    }
}
