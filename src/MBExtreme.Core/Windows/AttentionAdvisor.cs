namespace MBExtreme.Core.Windows;

/// <summary>Dados do PC usados para montar a lista "itens que precisam de atenção" do painel.</summary>
public sealed record AttentionFacts(
    double MemoryUsedPercent,
    int StartupEnabledCount,
    double? WorstDiskUsedPercent,
    string? WorstDiskName,
    int SecurityProblems,
    double? CpuTemperature,
    bool MemoryAutoCleanEnabled = false);

/// <param name="FixKind">Correção automática disponível: "security", "memory", "cleanup" ou vazio.</param>
public sealed record AttentionItem(string Title, string Detail, string ActionText, string TargetPage, bool Urgent, string FixKind = "", string FixText = "");

/// <summary>Resumo do painel: o que merece atenção agora e para onde levar o usuário.</summary>
public static class AttentionAdvisor
{
    public static IReadOnlyList<AttentionItem> Evaluate(AttentionFacts f)
    {
        var items = new List<AttentionItem>();

        if (f.SecurityProblems > 0)
            items.Add(new AttentionItem("Proteção do Windows precisa de atenção",
                $"{f.SecurityProblems} {(f.SecurityProblems == 1 ? "item" : "itens")} de segurança com problema.", "Ver detalhes", "security", true, "security", "Corrigir agora"));

        if (f.WorstDiskUsedPercent is double disk && f.WorstDiskName is not null && disk >= 90)
            items.Add(new AttentionItem($"Disco {f.WorstDiskName} quase cheio", $"{disk:0}% usado. Pouco espaço livre deixa o Windows lento.", "Ver discos", "disks", disk >= 95, "cleanup", "Limpar agora"));

        if (f.MemoryUsedPercent >= 80)
            items.Add(new AttentionItem("Memória em uso alto", $"{f.MemoryUsedPercent:0}% da memória está em uso.", "Ver memória", "ram-detail", f.MemoryUsedPercent >= 90,
                f.MemoryAutoCleanEnabled ? "" : "memory", f.MemoryAutoCleanEnabled ? "" : "Ligar limpeza automática"));

        if (f.CpuTemperature is double temp && temp >= 85)
            items.Add(new AttentionItem("Processador quente", $"{temp:0} °C. Verifique a refrigeração e a poeira.", "Ver processador", "cpu-detail", temp >= 92));

        if (f.StartupEnabledCount >= 8)
            items.Add(new AttentionItem("Muitos programas iniciam com o Windows",
                $"{f.StartupEnabledCount} programas ligam junto com o PC e deixam a inicialização mais lenta.", "Ver inicialização", "startup", false));

        return items;
    }

    public static string Headline(IReadOnlyList<AttentionItem> items) => items.Count switch
    {
        0 => "Seu PC está em boas condições.",
        1 => "1 item precisa de atenção.",
        _ => $"{items.Count} itens precisam de atenção.",
    };
}
