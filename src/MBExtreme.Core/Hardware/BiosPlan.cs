namespace MBExtreme.Core.Hardware;

public enum BiosStepState { Done, Todo, Suggested, Optional, Unknown }

/// <summary>Um passo do plano de BIOS: o que fazer, o que o MB conferiu no PC e o quanto é difícil.</summary>
public sealed record BiosStep(string Key, string Title, string Detail, string Status, BiosStepState State, string Level, IReadOnlyList<string> HowTo);

/// <summary>
/// Plano de "Otimização de BIOS" montado pelo hardware lido: atualizar a BIOS, perfil de memória (EXPO/XMP), Resizable BAR,
/// PBO/Curve Optimizer (Ryzen) ou estabilidade (Intel 13ª/14ª), limite de temperatura e overclock de memória (avançado).
/// O MB não muda a BIOS: orienta, confere o que dá para conferir pelo Windows e testa.
/// </summary>
public static class BiosPlan
{
    public static IReadOnlyList<BiosStep> Build(HardwareInventory hw, double? maxCpuTemp = null)
    {
        var steps = new List<BiosStep>();
        var recipe = BiosGuide.BuildRecipe(hw);
        IReadOnlyList<string> How(string titleStart) => recipe.FirstOrDefault(r => r.Title.StartsWith(titleStart, StringComparison.OrdinalIgnoreCase))?.Steps ?? Array.Empty<string>();

        // 1) BIOS
        string biosAge = hw.Bios.ReleaseDate is { } d ? $" de {d:MM/yyyy}" : "";
        bool old = hw.Bios.ReleaseDate is { } date && DateTime.Now - date > TimeSpan.FromDays(365);
        steps.Add(new BiosStep("bios", "Atualizar a BIOS",
            $"Placa {hw.Board.Manufacturer} {hw.Board.Product} · BIOS {hw.Bios.Version}{biosAge}. Versões novas trazem correções de memória, estabilidade e (em Ryzen) os ajustes mais recentes da AMD.",
            old ? "BIOS com mais de 1 ano" : "Confira se há versão nova", old ? BiosStepState.Todo : BiosStepState.Unknown, "fácil · 5 min", BiosGuide.UpdateChecklist()));

        // 2) memória: velocidade configurada x a do kit
        var mods = hw.Memory.Where(m => m.CapacityBytes > 0).ToList();
        string profile = hw.Cpu.IsAmd ? "EXPO" : "XMP";
        int rated = mods.Count > 0 ? mods.Max(m => m.RatedMhz) : 0, configured = mods.Count > 0 ? mods.Min(m => m.ConfiguredMhz) : 0;
        string type = mods.FirstOrDefault(m => m.Type.Length > 0)?.Type ?? "DDR";
        if (mods.Count == 0)
            steps.Add(new BiosStep("memory", $"Memória no perfil {profile}", "Não deu para ler a memória pelo Windows.", "Sem leitura", BiosStepState.Unknown, "fácil", How("Memória")));
        else if (rated > 0 && configured > 0 && configured < rated * 0.95)
            steps.Add(new BiosStep("memory", $"Memória no perfil {profile}",
                $"{mods.Count}×{mods[0].CapacityBytes / 1024 / 1024 / 1024} GB {type} rodando a {configured} MT/s, mas o kit é de {rated} MT/s. Ativar o {profile} costuma dar de 5 a 10% a mais em jogos.",
                "Desligado", BiosStepState.Todo, "fácil · ganho alto", How("Memória")));
        else
            steps.Add(new BiosStep("memory", $"Memória no perfil {profile}",
                $"{mods.Count}×{mods[0].CapacityBytes / 1024 / 1024 / 1024} GB {type} a {configured} MT/s" + (rated > 0 ? $" (kit de {rated})." : "."),
                "Na velocidade do kit", BiosStepState.Done, "fácil", How("Memória")));

        // 3) Resizable BAR
        if (hw.Gpus.Count > 0 && !hw.IsLaptop)
            steps.Add(new BiosStep("rebar", "Resizable BAR",
                $"{hw.Gpus[0].Name}. Na BIOS: Above 4G Decoding + Re-Size BAR (modo UEFI, CSM desligado).",
                hw.ResizableBar switch { true => "Ligado", false => "Desligado", _ => "Não deu para conferir" },
                hw.ResizableBar switch { true => BiosStepState.Done, false => BiosStepState.Suggested, _ => BiosStepState.Unknown }, "fácil", How("Resizable")));

        // 4) processador
        string hot = maxCpuTemp is { } t ? $" O processador chegou a {t:0} °C." : "";
        if (HardwareAdvisor.IsIntel13Or14GenDesktop(hw.Cpu))
            steps.Add(new BiosStep("cpu", "Intel 13ª/14ª: estabilidade primeiro",
                "BIOS com microcódigo 0x12B ou mais novo e perfil Intel Default Settings. Não suba limites de potência." + hot,
                hw.Cpu.Microcode is { } mc && mc >= 0x12B ? $"Microcódigo 0x{mc:X} ✓" : "Confira o microcódigo", hw.Cpu.Microcode is { } m2 && m2 >= 0x12B ? BiosStepState.Done : BiosStepState.Todo, "importante", How("CPU Intel")));
        else if (hw.Cpu.IsAmd && HardwareAdvisor.RyzenGeneration(hw.Cpu.Name) >= 3)
        {
            int gen = HardwareAdvisor.RyzenGeneration(hw.Cpu.Name);
            int start = gen == 3 ? -10 : -15;
            steps.Add(new BiosStep("cpu", "PBO + Curve Optimizer (menos calor)",
                $"Curve Optimizer All Cores negativo, começando em {start}. Costuma baixar a temperatura sem perder desempenho; depois de cada teste estável o MB sugere o próximo passo." + hot,
                $"Sugerido: {start}", BiosStepState.Suggested, "médio · testar", How("PBO")));
        }
        else
            steps.Add(new BiosStep("cpu", "Processador", "Para este processador não há ajuste de BIOS recomendado. Mantenha os limites do fabricante." + hot, "Padrão", BiosStepState.Done, "—", How("Processador")));

        // 5) limite de temperatura (desktop)
        if (!hw.IsLaptop && hw.Cpu.IsAmd)
            steps.Add(new BiosStep("thermal", "Limite de temperatura",
                "Opcional: Thermal Limit em 85 °C (PBO > Advanced) para o processador nunca passar disso em jogos longos.",
                maxCpuTemp is > 85 ? "Recomendado: passou de 85 °C" : "Opcional", maxCpuTemp is > 85 ? BiosStepState.Suggested : BiosStepState.Optional, "fácil", Array.Empty<string>()));

        // 6) overclock de memória (avançado)
        if (type == "DDR5" || type == "DDR4")
            steps.Add(new BiosStep("memoc", "Overclock de memória (avançado)",
                type == "DDR5" && hw.Cpu.IsAmd ? "Só depois dos passos acima estáveis: FCLK 2000 MHz e tempos mais curtos. Teste 30 minutos." : "Só depois dos passos acima estáveis: tempos mais curtos ou um degrau de velocidade. Teste 30 minutos.",
                "Opcional", BiosStepState.Optional, "avançado", Array.Empty<string>()));
        return steps;
    }

    /// <summary>Quantos passos ainda dão ganho (Todo + Suggested).</summary>
    public static int Pending(IReadOnlyList<BiosStep> steps) => steps.Count(s => s.State is BiosStepState.Todo or BiosStepState.Suggested);
}
