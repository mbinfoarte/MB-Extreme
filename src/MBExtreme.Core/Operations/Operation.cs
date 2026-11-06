namespace MBExtreme.Core.Operations;

/// <summary>Estados de uma operação (os mesmos em todos os módulos: ajustes, reparos, limpeza, programas).</summary>
public enum OperationState
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Skipped,
    RebootRequired,
}

/// <summary>De onde vem a operação (para o histórico e a Central Desfazer).</summary>
public enum OperationKind
{
    Tweak,
    TweakRestore,
    Repair,
    Cleanup,
    AppRemoval,
    Program,
    /// <summary>Configuração que se desfaz na própria página de origem (Modo de Jogo, plano de energia, inicialização...).</summary>
    Setting,
    Other,
}

public enum OperationRisk
{
    Low,
    Medium,
    High,
}

/// <summary>
/// O que a operação é, antes de rodar: identificador, nome, risco, se precisa de administrador, se tem volta e quanto costuma demorar.
/// <paramref name="TargetId"/> liga ao item original (ex.: o id do ajuste), para a Central Desfazer achar como restaurar.
/// </summary>
public sealed record OperationInfo(
    string Id, OperationKind Kind, string Title, OperationRisk Risk = OperationRisk.Low,
    bool NeedsAdmin = false, bool Reversible = false, string? TargetId = null, TimeSpan? Typical = null);

/// <summary>Resultado de uma operação: estado final e uma frase para a tela.</summary>
public sealed record OperationResult(OperationState State, string Message)
{
    public static OperationResult Ok(string message = "Concluído.") => new(OperationState.Succeeded, message);
    public static OperationResult Fail(string message) => new(OperationState.Failed, message);
    public static OperationResult Skip(string message) => new(OperationState.Skipped, message);
    public static OperationResult Reboot(string message = "Concluído; vale depois de reiniciar o computador.") => new(OperationState.RebootRequired, message);

    public bool IsSuccess => State is OperationState.Succeeded or OperationState.RebootRequired or OperationState.Skipped;
}

/// <summary>
/// Uma operação do programa. A fila (<see cref="OperationQueue"/>) roda uma de cada vez, registra tudo no diário e não para se uma falhar.
/// Diagnósticos nunca são operações: eles só leem e não alteram o Windows.
/// </summary>
public interface IOperation
{
    OperationInfo Info { get; }

    /// <summary>Executa. Deve observar o cancelamento quando der; o que não puder ser interrompido com segurança termina antes de parar.</summary>
    OperationResult Execute(IProgress<string>? progress, CancellationToken ct);
}

/// <summary>Operação que sabe desfazer o que fez (com o valor original guardado) e conferir que desfez.</summary>
public interface IReversibleOperation : IOperation
{
    OperationResult Rollback();
}
