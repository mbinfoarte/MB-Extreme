using System.Windows;
using System.Windows.Threading;

namespace MBExtreme.UI;

/// <summary>
/// Relato de progresso que SEMPRE chega na thread da interface (Dispatcher), mesmo quando criado dentro de um Task.Run.
/// <para>
/// O <see cref="Progress{T}"/> do .NET memoriza a thread onde foi criado. Se ele nascer dentro de um Task.Run
/// (thread de fundo, sem contexto de interface), o retorno roda na thread de fundo e, ao mexer na tela, estoura
/// "O thread de chamada não pode acessar este objeto porque ele pertence a um thread diferente". Este ajudante
/// guarda o Dispatcher da interface e devolve sempre nele, então pode ser criado em qualquer thread com segurança.
/// </para>
/// </summary>
public static class UiProgress
{
    public static IProgress<T> For<T>(Action<T> report)
    {
        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        return new Progress<T>(value =>
        {
            if (dispatcher.CheckAccess()) report(value);
            else dispatcher.BeginInvoke(report, value);
        });
    }

    /// <summary>Atalho para o caso mais comum: texto de status.</summary>
    public static IProgress<string> Text(Action<string> report) => For(report);
}
