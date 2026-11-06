using MBExtreme.Core.Tweaks;

namespace MBExtreme.Core.Operations;

/// <summary>Uma alteração do histórico, com o que dá (ou não dá) para fazer com ela.</summary>
public sealed record UndoItem(JournalEntry Entry, Tweak? Tweak, bool CanUndo, string Note);

/// <summary>
/// Monta a Central Desfazer a partir do diário: só oferece "restaurar" quando existe o valor original guardado e conferível;
/// o resto aparece como "sem volta", com o motivo, para nunca prometer o que não dá para cumprir.
/// </summary>
public static class UndoCenter
{
    public static IReadOnlyList<UndoItem> Build(IReadOnlyList<JournalEntry> entries, IReadOnlyList<Tweak> tweaks)
    {
        var byId = tweaks.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());
        var items = new List<UndoItem>();
        var seenTweak = new HashSet<string>();
        foreach (var entry in entries.OrderByDescending(e => e.StartedAt))
        {
            if (!entry.Changed || entry.Kind == OperationKind.TweakRestore) continue;   // restaurações são o próprio desfazer
            if (entry.Kind == OperationKind.Tweak && entry.TargetId is { } id)
            {
                bool latest = seenTweak.Add(id);
                if (entry.RolledBack) { items.Add(new UndoItem(entry, byId.GetValueOrDefault(id), false, "Já desfeito.")); continue; }
                if (!latest) { items.Add(new UndoItem(entry, byId.GetValueOrDefault(id), false, "Aplicado de novo depois: desfaça a mais recente.")); continue; }
                if (!byId.TryGetValue(id, out var tweak)) { items.Add(new UndoItem(entry, null, false, "Este ajuste não existe mais nesta versão do programa.")); continue; }
                items.Add(tweak.HasBackup
                    ? new UndoItem(entry, tweak, true, "Dá para voltar ao valor original.")
                    : new UndoItem(entry, tweak, false, "O valor original já foi restaurado (sem nada guardado para voltar)."));
                continue;
            }
            items.Add(new UndoItem(entry, null, false, IrreversibleNote(entry.Kind)));
        }
        return items;
    }

    public static string IrreversibleNote(OperationKind kind) => kind switch
    {
        OperationKind.Cleanup => "Sem volta: os arquivos apagados foram para a Lixeira quando possível; o resto não dá para recuperar.",
        OperationKind.AppRemoval => "Sem volta pelo programa: reinstale o aplicativo pela Microsoft Store.",
        OperationKind.Repair => "Sem volta: os reparos do Windows (DISM, SFC, CHKDSK) não têm como ser desfeitos; use um ponto de restauração se precisar.",
        OperationKind.Program => "Sem volta pelo programa: desinstale ou reinstale pelo winget ou pelo Windows.",
        _ => "Sem volta pelo programa.",
    };
}
