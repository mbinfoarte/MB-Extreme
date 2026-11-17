using System.Text.RegularExpressions;
using MBExtreme.Core.Drivers;

namespace MBExtreme.Core.Hardware;

/// <summary>
/// Regras que transformam a leitura do hardware em avisos. Baseadas em testes publicados (veja docs/PESQUISA-OTIMIZACAO-GAMER.md);
/// nenhuma promete percentual de FPS.
/// </summary>
public static class HardwareAdvisor
{
    public const uint IntelMicrocodeMinimum = 0x12B;
    public const uint IntelMicrocodePreferred = 0x12F;
    private const int Windows11Build24H2 = 26100;

    private static readonly Regex RaptorLakeDesktop = new(@"\bi[3579]-1[34]\d{3}(K|KF|KS|F|T)?\b", RegexOptions.IgnoreCase);

    public static bool IsIntel13Or14GenDesktop(CpuInfo cpu) => cpu.IsIntel && RaptorLakeDesktop.IsMatch(cpu.Name);

    public static IReadOnlyList<HardwareCheck> Evaluate(HardwareInventory hw, DateTime now)
    {
        var checks = new List<HardwareCheck>();
        CheckMemorySpeed(hw, checks);
        CheckChannels(hw, checks);
        CheckMemorySize(hw, checks);
        CheckWindows(hw, checks);
        CheckBios(hw, now, checks);
        CheckMicrocode(hw, checks);
        CheckChipset(hw, checks);
        CheckResizableBar(hw, checks);
        if (hw.IsLaptop)
            checks.Add(new HardwareCheck(CheckLevel.Info, "Notebook",
                "Jogue com o carregador na tomada e o plano de energia de alto desempenho. Muitas opções avançadas da BIOS ficam bloqueadas em notebook."));
        return checks;
    }

    private static void CheckMemorySpeed(HardwareInventory hw, List<HardwareCheck> checks)
    {
        var slow = hw.Memory.Where(m => m.RatedMhz > 0 && m.ConfiguredMhz > 0 && m.ConfiguredMhz * 1.05 < m.RatedMhz).ToList();
        if (hw.Memory.Count == 0) return;

        if (hw.IsLaptop)
        {
            checks.Add(new HardwareCheck(CheckLevel.Info, "Velocidade da memória",
                $"Memória em {hw.Memory.Max(m => m.ConfiguredMhz)} MHz. Em notebook a velocidade costuma ser fixa pela fabricante."));
            return;
        }

        if (slow.Count > 0)
        {
            string profile = hw.Cpu.IsAmd ? "EXPO" : "XMP";
            var worst = slow.OrderBy(m => m.ConfiguredMhz).First();
            checks.Add(new HardwareCheck(CheckLevel.Attention, "Memória abaixo da velocidade do pente",
                $"A memória roda a {worst.ConfiguredMhz} MHz, mas o pente suporta {worst.RatedMhz} MHz. Testes publicados mostram ganho de 4 a 10% de FPS médio e até cerca de 20% nos 1% pior ao ligar o perfil de memória.",
                $"Entre na BIOS e ative o perfil {profile} (também chamado DOCP/A-XMP em algumas placas). Veja a Receita de BIOS."));
        }
        else
        {
            checks.Add(new HardwareCheck(CheckLevel.Ok, "Velocidade da memória",
                $"Memória em {hw.Memory.Max(m => m.ConfiguredMhz)} MHz, sem indício de perfil XMP/EXPO desligado."));
        }
    }

    private static void CheckChannels(HardwareInventory hw, List<HardwareCheck> checks)
    {
        if (hw.Memory.Count == 0) return;
        if (hw.Memory.Count == 1)
        {
            checks.Add(hw.IsLaptop
                ? new HardwareCheck(CheckLevel.Info, "Memória em canal único",
                    "Só há um pente de memória. Um segundo pente igual ativa o canal duplo, se o notebook tiver o encaixe.")
                : new HardwareCheck(CheckLevel.Attention, "Memória em canal único",
                    "Só há um pente de memória. Em testes de DDR5, um pente só custou de 8 a 10% de FPS médio em relação a dois (menos nos Ryzen com 3D V-Cache).",
                    "Use dois pentes iguais, nos encaixes indicados no manual da placa-mãe."));
            return;
        }

        checks.Add(new HardwareCheck(CheckLevel.Ok, "Canais de memória", $"{hw.Memory.Count} pentes instalados."));
    }

    private static void CheckMemorySize(HardwareInventory hw, List<HardwareCheck> checks)
    {
        double gb = hw.TotalMemoryBytes / 1024.0 / 1024 / 1024;
        if (gb <= 0) return;
        if (gb < 7.5)
            checks.Add(new HardwareCheck(CheckLevel.Attention, "Pouca memória RAM",
                $"{gb:0.#} GB de RAM é pouco para jogos atuais e o Windows 11.",
                "Se possível, vá para 16 GB. Enquanto isso, use a Limpeza de RAM e o Modo Jogo (fecha programas pesados)."));
        else if (gb < 15)
            checks.Add(new HardwareCheck(CheckLevel.Info, "Memória RAM", $"{gb:0.#} GB. Funciona, mas 16 GB é o ideal para jogos atuais."));
        else
            checks.Add(new HardwareCheck(CheckLevel.Ok, "Memória RAM", $"{gb:0.#} GB."));
    }

    private static void CheckWindows(HardwareInventory hw, List<HardwareCheck> checks)
    {
        var w = hw.Windows;
        if (!w.IsWindows11)
        {
            checks.Add(new HardwareCheck(CheckLevel.Attention, "Windows 10",
                "O suporte do Windows 10 terminou em 14/10/2025 (sem correções de segurança, exceto no programa ESU). Em jogos o Windows 10 e o 11 estão praticamente empatados.",
                "Planeje a atualização para o Windows 11 se o PC for compatível."));
            return;
        }

        if (hw.Cpu.IsAmd && w.Build < Windows11Build24H2)
            checks.Add(new HardwareCheck(CheckLevel.Attention, "Windows 11 anterior ao 24H2",
                $"Você está no Windows 11 {w.DisplayVersion} (compilação {w.Build}). Testes de Hardware Unboxed e ComputerBase mediram cerca de +10% de FPS em Ryzen 7000/9000 ao passar para o 24H2.",
                "Atualize pelo Windows Update (Configurações > Windows Update)."));
        else
            checks.Add(new HardwareCheck(CheckLevel.Ok, "Windows", $"Windows 11 {w.DisplayVersion} (compilação {w.Build})."));
    }

    private static void CheckBios(HardwareInventory hw, DateTime now, List<HardwareCheck> checks)
    {
        if (hw.Bios.ReleaseDate is not { } date) return;
        int months = (int)((now - date).TotalDays / 30.4);
        string text = $"BIOS {hw.Bios.Version} de {date:dd/MM/yyyy} ({months} meses).";
        if (months >= 36)
            checks.Add(new HardwareCheck(CheckLevel.Attention, "BIOS antiga", text + " Pode haver correções de estabilidade, segurança e compatibilidade de memória.",
                "Veja a página BIOS para abrir o suporte da sua placa e ler as notas das versões."));
        else if (months >= 18)
            checks.Add(new HardwareCheck(CheckLevel.Info, "BIOS com mais de 1 ano e meio", text, "Confira no site do fabricante se há versão mais nova."));
        else
            checks.Add(new HardwareCheck(CheckLevel.Ok, "BIOS recente", text));
    }

    private static void CheckMicrocode(HardwareInventory hw, List<HardwareCheck> checks)
    {
        if (!IsIntel13Or14GenDesktop(hw.Cpu)) return;
        if (hw.Cpu.Microcode is not { } micro)
        {
            checks.Add(new HardwareCheck(CheckLevel.Attention, "Microcódigo da CPU Intel 13ª/14ª geração",
                "Não consegui ler a versão do microcódigo. A Intel recomenda BIOS com microcódigo 0x12B ou mais novo (0x12F) e o perfil \"Intel Default Settings\".",
                "Confirme a versão nas notas da BIOS do fabricante."));
        }
        else if (micro < IntelMicrocodeMinimum)
        {
            checks.Add(new HardwareCheck(CheckLevel.Problem, "BIOS sem a correção da Intel",
                $"Microcódigo 0x{micro:X}. Processadores Intel Core 13ª e 14ª geração precisam de 0x12B ou mais novo para evitar a instabilidade por elevação de tensão (Vmin shift). O microcódigo não repara chips já afetados.",
                "Atualize a BIOS (veja a página BIOS) e use o perfil \"Intel Default Settings\"."));
        }
        else if (micro < IntelMicrocodePreferred)
        {
            checks.Add(new HardwareCheck(CheckLevel.Info, "Microcódigo da CPU Intel",
                $"Microcódigo 0x{micro:X} (inclui a correção 0x12B). A Intel liberou o 0x12F, que reduz ainda mais as condições do problema.",
                "Se a fabricante da placa tiver BIOS com 0x12F, atualize."));
        }
        else
        {
            checks.Add(new HardwareCheck(CheckLevel.Ok, "Microcódigo da CPU Intel", $"Microcódigo 0x{micro:X}, com a correção da Intel."));
        }
    }

    private static void CheckChipset(HardwareInventory hw, List<HardwareCheck> checks)
    {
        if (!hw.Cpu.IsAmd || hw.IsLaptop || hw.AmdChipsetDriverInstalled is null) return;
        if (hw.AmdChipsetDriverInstalled == false)
            checks.Add(new HardwareCheck(CheckLevel.Attention, "Driver de chipset AMD não encontrado",
                "O driver de chipset da AMD instala o plano de energia Ryzen e correções de agendamento. Sem ele, o Windows usa o plano genérico.",
                "Baixe o AMD Chipset Software na página da AMD (Drivers e programas > Programas)."));
        else
            checks.Add(new HardwareCheck(CheckLevel.Ok, "Driver de chipset AMD", "Instalado."));
    }

    private static void CheckResizableBar(HardwareInventory hw, List<HardwareCheck> checks)
    {
        if (hw.ResizableBar is null) return;
        checks.Add(hw.ResizableBar == true
            ? new HardwareCheck(CheckLevel.Ok, "Resizable BAR", "Ligado.")
            : new HardwareCheck(CheckLevel.Info, "Resizable BAR desligado",
                "Em testes publicados o ganho varia de −2% a +5% (com picos bem maiores em poucos jogos). É opcional.",
                "Se quiser testar: ative \"Above 4G Decoding\" e \"Re-Size BAR\" na BIOS e meça antes e depois."));
    }

    // ---------- leitura de dados brutos ----------

    /// <summary>Revisão do microcódigo (valor "Update Revision" do registro). Nas CPUs Intel ela fica nos 4 bytes finais.</summary>
    public static uint? ParseMicrocode(byte[]? data)
    {
        if (data is null) return null;
        if (data.Length >= 8) return BitConverter.ToUInt32(data, 4);
        if (data.Length == 4) return BitConverter.ToUInt32(data, 0);
        return null;
    }

    public static string MemoryTypeName(int smbiosType) => smbiosType switch
    {
        20 => "DDR",
        21 => "DDR2",
        24 => "DDR3",
        26 => "DDR4",
        34 => "DDR5",
        29 => "LPDDR2",
        30 => "LPDDR3",
        31 => "LPDDR4",
        35 => "LPDDR5",
        _ => "",
    };

    /// <summary>Total e BAR1 em MiB a partir da saída de "nvidia-smi -q -d MEMORY". BAR1 do tamanho da VRAM indica Resizable BAR ligado.</summary>
    public static bool? ParseResizableBar(string nvidiaSmiOutput)
    {
        long? Grab(string section)
        {
            int start = nvidiaSmiOutput.IndexOf(section, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return null;
            var match = Regex.Match(nvidiaSmiOutput[start..], @"Total\s*:\s*(\d+)\s*MiB", RegexOptions.IgnoreCase);
            return match.Success ? long.Parse(match.Groups[1].Value) : null;
        }

        long? bar1 = Grab("BAR1 Memory Usage");
        long? fb = Grab("FB Memory Usage");
        if (bar1 is null || fb is null || fb == 0) return null;
        return bar1 >= fb * 0.9;
    }

    /// <summary>Geração do Ryzen a partir do nome (Zen 2 a Zen 5). 0 = não identificada.</summary>
    public static int RyzenGeneration(string cpuName)
    {
        if (cpuName.Contains("Ryzen AI", StringComparison.OrdinalIgnoreCase)) return 5;
        var match = Regex.Match(cpuName, @"Ryzen\s+(?:Threadripper\s+)?(?:[3579]\s+)?(?:PRO\s+)?(\d)\d{3}", RegexOptions.IgnoreCase);
        if (!match.Success) return 0;
        return match.Groups[1].Value switch
        {
            "3" => 2,
            "5" => 3,
            "6" => 3,
            "7" => 4,
            "8" => 4,
            "9" => 5,
            _ => 0,
        };
    }
}
