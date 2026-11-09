using System.Windows;
using System.Windows.Controls;
using MBExtreme.Core.Operations;

namespace MBExtreme.UI.Pages;

/// <summary>
/// Central Desfazer: o histórico de tudo que o programa mudou no Windows, com seleção para voltar ao valor original
/// e, à parte, o que não tem volta (limpezas, apps removidos, reparos), com o motivo.
/// </summary>
public sealed class UndoCenterPage : ToolPageBase
{
    private readonly List<(CheckBox Box, UndoItem Item)> _boxes = new();
    private bool _busy;

    public UndoCenterPage() : base("Central Desfazer",
        "Tudo que o MB Extreme mudou no Windows, do mais recente para o mais antigo. Marque os ajustes e clique em \"Restaurar selecionados\" para voltar ao valor original; " +
        "o programa confere depois se voltou mesmo. O que não tem como desfazer aparece separado, com o motivo.")
    {
        AddButton("Restaurar selecionados", async (_, _) => await RestoreAsync());
        AddSelectionButtons(() => _boxes.Select(b => b.Box));
        AddButton("Atualizar", (_, _) => Load(), false);
        Loaded += (_, _) => Load();
    }

    private void Load()
    {
        Body.Children.Clear();
        _boxes.Clear();
        var items = UndoCenter.Build(OperationJournal.Shared.Load(), AppServices.Tweaks);
        var undoable = items.Where(i => i.CanUndo).ToList();
        var history = items.Where(i => !i.CanUndo).ToList();

        Body.Children.Add(Heading($"Dá para desfazer ({undoable.Count})"));
        if (undoable.Count == 0) Body.Children.Add(Muted("Nenhum ajuste aplicado esperando para ser desfeito."));
        foreach (var item in undoable)
        {
            var box = Check(item.Entry.Title, $"{When(item.Entry)} · {item.Note}" + (item.Entry.Interrupted ? " Ficou interrompido: confira." : ""));
            box.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, item.Entry.Title);
            _boxes.Add((box, item));
            Body.Children.Add(CardOf(box));
        }

        Body.Children.Add(Heading($"Histórico e o que não tem volta ({history.Count})"));
        if (history.Count == 0) Body.Children.Add(Muted("Nada registrado ainda. As alterações feitas a partir desta versão aparecem aqui."));
        foreach (var item in history.Take(200))
            Body.Children.Add(Row(item.Entry.Title, $"{When(item.Entry)} · {StateText(item.Entry)} · {item.Note}", null,
                item.Entry.State == OperationState.Failed || item.Entry.Interrupted ? "WarnBrush" : "MutedBrush"));

        SetStatus(undoable.Count == 0 ? "Nada para desfazer agora." : $"{undoable.Count} {(undoable.Count == 1 ? "ajuste pode" : "ajustes podem")} voltar ao valor original.");
    }

    private async Task RestoreAsync()
    {
        if (_busy) return;
        var chosen = _boxes.Where(b => b.Box.IsChecked == true && b.Item.Tweak is not null).Select(b => b.Item).ToList();
        if (chosen.Count == 0) { SetStatus("Marque pelo menos um ajuste para restaurar.", "WarnBrush"); return; }
        if (MessageBox.Show($"Voltar {chosen.Count} {(chosen.Count == 1 ? "ajuste" : "ajustes")} ao valor original?", "Central Desfazer",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _busy = true;
        SetStatus("Restaurando...");
        try
        {
            var targets = chosen.Select(i => (i.Entry, (IReversibleOperation)new TweakOperation(i.Tweak!, apply: true))).ToList();
            var results = await Task.Run(() => OperationQueue.Shared.Rollback(targets));
            int ok = results.Count(r => r.Result.IsSuccess), failed = results.Count - ok;
            bool reboot = results.Any(r => r.Result.State == OperationState.RebootRequired);
            Load();
            string text = $"{ok} restaurado(s)" + (failed > 0 ? $", {failed} com problema: " + string.Join("; ", results.Where(r => !r.Result.IsSuccess).Select(r => r.Entry.Title + " — " + r.Result.Message)) : ".")
                          + (reboot ? " Alguns valem depois de reiniciar o computador." : "");
            SetStatus(text, failed == 0 ? "OkBrush" : "WarnBrush");
        }
        catch (InvalidOperationException ex)
        {
            SetStatus(ex.Message, "WarnBrush");
        }
        finally
        {
            _busy = false;
        }
    }

    private static string When(JournalEntry e) => e.StartedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");

    private static string StateText(JournalEntry e) => e.Interrupted ? "interrompido" : e.RolledBack ? "desfeito" : e.State switch
    {
        OperationState.Succeeded => "feito",
        OperationState.RebootRequired => "feito (reiniciar)",
        OperationState.Failed => "falhou",
        OperationState.Cancelled => "cancelado",
        OperationState.Skipped => "pulado",
        _ => e.State.ToString(),
    };
}
