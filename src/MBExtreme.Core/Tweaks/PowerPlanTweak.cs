using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Tweaks;

/// <summary>Cria e ativa o plano de energia "Desempenho Máximo"; ao restaurar, volta ao plano anterior e apaga o criado.</summary>
[SupportedOSPlatform("windows")]
public sealed class PowerPlanTweak : Tweak
{
    private const string UltimateTemplate = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    private const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
    private static readonly Regex GuidPattern = new(@"[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}");

    public PowerPlanTweak(TweakStore store, string id, TweakCategory category, TweakRisk risk, string title, string description)
        : base(store, id, category, risk, title, description)
    {
    }

    private string PreviousKey => $"{Id}|previous";
    private string CreatedKey => $"{Id}|created";

    public static string? FirstGuid(string text)
    {
        var match = GuidPattern.Match(text);
        return match.Success ? match.Value : null;
    }

    private static string? ActiveScheme() => FirstGuid(Shell.Run("powercfg.exe", "/getactivescheme").Output);

    public override bool? IsApplied()
    {
        string? active = ActiveScheme();
        if (active is null) return null;
        string? created = Store.Get(CreatedKey);
        return created is not null && string.Equals(active, created, StringComparison.OrdinalIgnoreCase);
    }

    public override void Apply()
    {
        string previous = ActiveScheme()
            ?? throw new InvalidOperationException("Não foi possível ler o plano de energia atual.");
        if (!Store.Has(PreviousKey)) Store.Set(PreviousKey, previous);

        string? created = Store.Get(CreatedKey);
        if (created is null)
        {
            var duplicate = Shell.Run("powercfg.exe", "-duplicatescheme", UltimateTemplate);
            created = FirstGuid(duplicate.Output);
            if (duplicate.ExitCode != 0 || created is null)
                throw new InvalidOperationException("Este Windows não oferece o plano Desempenho Máximo.");
            Store.Set(CreatedKey, created);
        }

        var activate = Shell.Run("powercfg.exe", "/setactive", created);
        if (activate.ExitCode != 0)
            throw new InvalidOperationException("O Windows recusou ativar o plano de energia.");
    }

    public override void Revert()
    {
        string previous = Store.Get(PreviousKey) ?? Balanced;
        if (Shell.Run("powercfg.exe", "/setactive", previous).ExitCode != 0)
            Shell.Run("powercfg.exe", "/setactive", Balanced);

        string? created = Store.Get(CreatedKey);
        if (created is not null) Shell.Run("powercfg.exe", "/delete", created);

        Store.Remove(PreviousKey);
        Store.Remove(CreatedKey);
    }
}
