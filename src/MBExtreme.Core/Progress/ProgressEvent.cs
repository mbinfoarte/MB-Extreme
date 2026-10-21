namespace MBExtreme.Core.Progress;

/// <summary>
/// Evento publicado por qualquer tarefa longa (limpeza, DNS, benchmark...).
/// A janela de progresso da interface apenas exibe estes eventos.
/// </summary>
public sealed class ProgressEvent
{
    public ProgressEvent(string step, double percent, string message)
    {
        Step = step;
        Percent = Math.Clamp(percent, 0, 100);
        Message = message;
    }

    /// <summary>Nome da etapa atual (igual a um dos nomes passados à janela de progresso).</summary>
    public string Step { get; }

    /// <summary>Progresso total da operação, de 0 a 100.</summary>
    public double Percent { get; }

    /// <summary>Texto curto do que está sendo feito agora.</summary>
    public string Message { get; }
}
