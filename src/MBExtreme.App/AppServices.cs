using MBExtreme.Core.Tweaks;

namespace MBExtreme.UI;

/// <summary>Objetos compartilhados entre as páginas (o backup dos ajustes é um só).</summary>
internal static class AppServices
{
    public static TweakStore TweakStore { get; } = new();

    public static IReadOnlyList<Tweak> Tweaks { get; } = TweakCatalog.Create(TweakStore);
}
