using System.IO;
using System.Net.Http;
using MBExtreme.Core.Licensing;
using MBExtreme.Core.Operations;

namespace MBExtreme.UI;

/// <summary>
/// Atendimento técnico em andamento neste PC (conta de administrador). Anota sozinho o que foi feito (cada operação concluída da fila)
/// e avisa a janela para mostrar a faixa "MODO ATENDIMENTO TÉCNICO".
/// </summary>
internal static class ServiceMode
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };

    public static ServiceVisitState? Active => AppServices.Settings.ServiceVisit;

    /// <summary>Começou, terminou ou anotou algo. Pode vir de outra linha.</summary>
    public static event Action? Changed;

    public static ServiceClient? Client => AppServices.License.Config.Configured && AppServices.License.CurrentToken is { } t
        ? new ServiceClient(Http, AppServices.License.Config.ServerUrl, t, AppServices.License.DeviceId)
        : null;

    /// <summary>Liga a anotação automática (chamado uma vez, ao abrir o programa).</summary>
    public static void Hook()
    {
        OperationQueue.Shared.Changed += item =>
        {
            if (Active is null || item.State is not (OperationState.Succeeded or OperationState.RebootRequired)) return;
            if (item.Operation.Info.Kind == OperationKind.Tweak) Active.Tweaks++;
            else if (item.Operation.Info.Kind != OperationKind.TweakRestore) Note(item.Operation.Info.Title, save: false);
            Save();
        };
    }

    public static void Begin(ServiceVisitState visit)
    {
        AppServices.Settings.ServiceVisit = visit;
        Save();
    }

    /// <summary>Anota algo feito (sem repetir a mesma linha seguida).</summary>
    public static void Note(string text, bool save = true)
    {
        if (Active is not { } v || string.IsNullOrWhiteSpace(text)) return;
        text = text.Trim();
        if (v.Items.Count > 0 && v.Items[^1] == text) return;
        if (v.Items.Count < 300) v.Items.Add(text);
        if (save) Save();
    }

    public static void End()
    {
        AppServices.Settings.ServiceVisit = null;
        Save();
    }

    private static void Save()
    {
        try { AppServices.Settings.Save(); }
        catch (IOException) { /* tenta de novo na próxima anotação */ }
        Changed?.Invoke();
    }

    /// <summary>Este PC tem o selo de atendimento (as otimizações ficam mesmo sem assinatura)?</summary>
    public static bool Sealed => ServiceSeal.Present(AppServices.License.Config.PublicKey, AppServices.License.DeviceId);
}
