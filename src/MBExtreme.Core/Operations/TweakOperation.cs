using MBExtreme.Core.Tweaks;

namespace MBExtreme.Core.Operations;

/// <summary>Um ajuste do Windows como operação da fila: aplicar tem volta (o valor original fica guardado); restaurar volta ao original.</summary>
public sealed class TweakOperation : IReversibleOperation
{
    private readonly Tweak _tweak;
    private readonly bool _apply;

    public TweakOperation(Tweak tweak, bool apply)
    {
        _tweak = tweak;
        _apply = apply;
        Info = new OperationInfo(
            (apply ? "tweak.apply." : "tweak.revert.") + tweak.Id,
            apply ? OperationKind.Tweak : OperationKind.TweakRestore,
            (apply ? "Aplicar: " : "Restaurar: ") + tweak.Title,
            tweak.Risk switch { TweakRisk.High => OperationRisk.High, TweakRisk.Medium => OperationRisk.Medium, _ => OperationRisk.Low },
            NeedsAdmin: true,
            Reversible: apply,
            TargetId: tweak.Id);
    }

    public OperationInfo Info { get; }

    public Tweak Tweak => _tweak;

    public OperationResult Execute(IProgress<string>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_apply) _tweak.Apply();
        else _tweak.Revert();
        return _tweak.RequiresRestart ? OperationResult.Reboot() : OperationResult.Ok(_apply ? "Aplicado." : "Valor original restaurado.");
    }

    /// <summary>Volta ao valor original e confere: se o Windows ainda mostra o ajuste aplicado, avisa.</summary>
    public OperationResult Rollback()
    {
        if (!_apply) return OperationResult.Skip("Esta já era uma restauração.");
        if (!_tweak.HasBackup) return OperationResult.Skip("Não há valor original guardado (já foi restaurado antes).");
        _tweak.Revert();
        bool? still = _tweak.IsApplied();
        if (still == true) return OperationResult.Fail("O Windows ainda mostra o ajuste aplicado. Reinicie e confira.");
        return _tweak.RequiresRestart ? OperationResult.Reboot("Restaurado; vale depois de reiniciar o computador.") : OperationResult.Ok("Restaurado ao valor original.");
    }
}
