using System.Runtime.Versioning;
using MBExtreme.Core.Apps;

namespace MBExtreme.Core.Operations;

/// <summary>Remover um aplicativo da Microsoft Store (sem volta pelo programa: reinstala pela Loja).</summary>
[SupportedOSPlatform("windows")]
public sealed class AppRemovalOperation(BloatApp app) : IOperation
{
    public OperationInfo Info { get; } = new("app.remove." + app.Id, OperationKind.AppRemoval, "Remover aplicativo: " + app.Title,
        app.Level switch { AppRemovalLevel.Dangerous => OperationRisk.High, AppRemovalLevel.Caution => OperationRisk.Medium, _ => OperationRisk.Low });

    public OperationResult Execute(IProgress<string>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        AppRemovalService.Remove(app);
        return OperationResult.Ok("Removido para este usuário. Para ter de volta, instale pela Microsoft Store.");
    }
}

/// <summary>Instalar ou atualizar um programa pelo winget (uma operação por vez na fila).</summary>
[SupportedOSPlatform("windows")]
public sealed class WingetOperation(string id, string title, bool upgrade) : IOperation
{
    public OperationInfo Info { get; } = new((upgrade ? "winget.upgrade." : "winget.install.") + id, OperationKind.Program,
        (upgrade ? "Atualizar: " : "Instalar: ") + title, Typical: TimeSpan.FromMinutes(2));

    public OperationResult Execute(IProgress<string>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var r = upgrade ? WingetService.UpgradeDetailed(id) : WingetService.InstallDetailed(id);
        return r.Ok ? (r.RebootRequired ? OperationResult.Reboot(r.Message) : OperationResult.Ok(r.Message)) : OperationResult.Fail(r.Message);
    }
}
