using System.Text;

namespace MBExtreme.Core.Hardware;

public sealed record SupportLink(string Vendor, string Tool, string Url);

public sealed record RecipeSection(string Title, IReadOnlyList<string> Steps);

/// <summary>Orientação sobre BIOS: onde procurar atualização e a "Receita de BIOS" (memória, Resizable BAR, PBO). Nada é aplicado automaticamente.</summary>
public static class BiosGuide
{
    private static readonly (string Match, string Vendor, string Domain, string Tool)[] Vendors =
    {
        ("asus", "ASUS", "asus.com", "MyASUS / Armoury Crate"),
        ("micro-star", "MSI", "msi.com", "MSI Center"),
        ("msi", "MSI", "msi.com", "MSI Center"),
        ("gigabyte", "Gigabyte", "gigabyte.com", "Gigabyte Control Center"),
        ("asrock", "ASRock", "asrock.com", "ASRock Live Update"),
        ("dell", "Dell", "dell.com", "Dell Command | Update"),
        ("hewlett", "HP", "hp.com", "HP Support Assistant"),
        ("hp", "HP", "hp.com", "HP Support Assistant"),
        ("lenovo", "Lenovo", "lenovo.com", "Lenovo Vantage"),
        ("acer", "Acer", "acer.com", "Acer Care Center"),
        ("samsung", "Samsung", "samsung.com", "Samsung Update"),
    };

    private static (string Vendor, string Domain, string Tool)? FindVendor(string text)
    {
        string lower = text.ToLowerInvariant();
        foreach (var v in Vendors)
            if (lower.Contains(v.Match)) return (v.Vendor, v.Domain, v.Tool);
        return null;
    }

    /// <summary>Busca restrita ao site oficial da fabricante (placa-mãe em PC montado; modelo do equipamento em PC de marca).</summary>
    public static SupportLink SupportSearch(HardwareInventory hw)
    {
        var board = FindVendor(hw.Board.Manufacturer);
        var system = FindVendor(hw.System.Manufacturer);

        // PC de marca (Dell, HP, Lenovo...): vale o modelo do equipamento; PC montado: o modelo da placa-mãe.
        bool branded = system is not null && board is not null && system.Value.Vendor != board.Value.Vendor
            || (system is not null && system.Value.Vendor is "Dell" or "HP" or "Lenovo" or "Acer" or "Samsung");
        var vendor = branded ? system : board ?? system;
        string model = branded ? hw.System.Model : hw.Board.Product;
        string maker = branded ? hw.System.Manufacturer : hw.Board.Manufacturer;

        if (vendor is null)
        {
            string query = $"{maker} {model} BIOS update";
            return new SupportLink(maker, "", "https://www.google.com/search?q=" + Uri.EscapeDataString(query));
        }

        string search = $"{vendor.Value.Vendor} {model} BIOS download site:{vendor.Value.Domain}";
        return new SupportLink(vendor.Value.Vendor, vendor.Value.Tool, "https://www.google.com/search?q=" + Uri.EscapeDataString(search));
    }

    public static IReadOnlyList<string> UpdateChecklist() => new[]
    {
        "Baixe a BIOS somente do site oficial da fabricante (confirme o modelo exato da placa ou do equipamento).",
        "Leia as notas da versão: veja se corrige algo que afeta você (estabilidade, memória, microcódigo da CPU).",
        "Notebook: conecte o carregador. Desktop: se possível use nobreak. Nunca desligue o PC durante a atualização.",
        "Anote as configurações atuais da BIOS (fotografe as telas) porque a atualização costuma voltar tudo ao padrão.",
        "Use o recurso de atualização da própria BIOS (EZ Flash, M-Flash, Q-Flash, etc.) com o arquivo num pendrive FAT32.",
        "Depois, ative de novo o perfil de memória (XMP/EXPO) e confira os outros ajustes.",
        "O MB Extreme não atualiza BIOS sozinho: um erro nessa etapa pode deixar o PC sem ligar.",
    };

    public static IReadOnlyList<RecipeSection> BuildRecipe(HardwareInventory hw)
    {
        var sections = new List<RecipeSection>();
        string cpu = hw.Cpu.Name;
        bool amd = hw.Cpu.IsAmd;

        sections.Add(new RecipeSection("Antes de começar", new[]
        {
            "Crie um ponto de restauração do Windows e anote as configurações atuais da BIOS.",
            "Rode o Benchmark e o Teste de estresse do MB Extreme para ter os números de antes.",
            "Qualquer ajuste de CPU é overclock/undervolt: pode afetar a garantia e, se for exagerado, travar o PC.",
            hw.IsLaptop ? "Notebook: muitas dessas opções não existem na BIOS. Se não aparecerem, pule." : "Entre na BIOS pelo DEL ou F2 ao ligar o PC.",
        }));

        string memType = hw.Memory.FirstOrDefault(m => m.Type.Length > 0)?.Type ?? "DDR";
        string profile = amd ? "EXPO" : "XMP";
        string target = memType switch
        {
            "DDR5" => amd ? "Em Ryzen com DDR5, o melhor equilíbrio costuma ser 6000 MT/s (testes publicados indicam o ponto ideal entre 6000 e 6400)." : "Use a velocidade anunciada no kit (o perfil XMP já a configura).",
            "DDR4" => "Para DDR4 o ponto comum é 3200 a 3600 MT/s, conforme o kit.",
            _ => "Use a velocidade anunciada no kit.",
        };
        var memorySteps = new List<string>
        {
            $"Na BIOS, procure \"{profile}\" (ou DOCP / A-XMP em algumas placas ASUS e Gigabyte) e ative o perfil 1.",
            target,
            "Salve e reinicie. No MB Extreme, abra Hardware: o aviso de memória lenta deve sumir.",
        };
        if (hw.Memory.Count == 1) memorySteps.Add("Você tem só 1 pente: com 2 pentes iguais o canal duplo rende de 8 a 10% a mais de FPS médio.");
        sections.Add(new RecipeSection($"Memória ({memType}) - o ganho mais comprovado", memorySteps));

        if (hw.Gpus.Count > 0 && !hw.IsLaptop)
            sections.Add(new RecipeSection("Resizable BAR (opcional)", new[]
            {
                "Na BIOS: ative \"Above 4G Decoding\" e \"Re-Size BAR Support\"; o modo de inicialização deve ser UEFI (CSM desligado).",
                "Os ganhos publicados variam de −2% a +5% (alguns jogos chegam a mais, outros perdem). Meça antes e depois com o Benchmark.",
                "NVIDIA liga o recurso por jogo; AMD liga de forma global.",
            }));

        if (HardwareAdvisor.IsIntel13Or14GenDesktop(hw.Cpu))
        {
            sections.Add(new RecipeSection("CPU Intel 13ª/14ª geração: estabilidade", new[]
            {
                "Atualize a BIOS para uma versão com microcódigo 0x12B ou mais novo (0x12F).",
                "Selecione o perfil \"Intel Default Settings\" na BIOS.",
                "Não aumente os limites de potência (PL1/PL2) nem a tensão além do padrão da Intel.",
                "O microcódigo evita novos danos, mas não repara chip já afetado. Se o PC trava em jogos, fale com a Intel ou a loja.",
            }));
        }
        else if (amd)
        {
            int gen = HardwareAdvisor.RyzenGeneration(cpu);
            string start = gen >= 4 ? "ponto de partida −10, subindo de 5 em 5 até no máximo cerca de −20 a −25" :
                           gen == 3 ? "ponto de partida −10, subindo de 5 em 5 até no máximo cerca de −15 a −20 (o Zen 3 costuma ter menos margem)" :
                           "ponto de partida −10";
            var pbo = new List<string>();
            if (gen == 2)
            {
                pbo.Add("O Ryzen 3000 (Zen 2) não tem Curve Optimizer. Deixe o PBO em Auto, ou ajuste só os limites oficiais.");
            }
            else
            {
                pbo.Add("Na BIOS: Advanced > AMD Overclocking > Precision Boost Overdrive > Advanced. Deixe PPT, TDC e EDC no padrão (Auto/Motherboard).");
                pbo.Add($"Curve Optimizer: modo \"All Cores\", sinal \"Negative\", {start}.");
                pbo.Add("Depois de cada passo, rode o Teste de estresse por núcleo do MB Extreme. Se um núcleo der erro ou o PC reiniciar, volte um passo (5 pontos).");
                pbo.Add("Resultado típico em reviews: menos temperatura e consumo com pouca perda de desempenho; em jogos o ganho de FPS é pequeno.");
                pbo.Add("Offsets muito agressivos podem causar travamentos em repouso. Se acontecer, reduza o valor.");
                if (hw.Cpu.Name.Contains("X3D", StringComparison.OrdinalIgnoreCase))
                    pbo.Add("CPU com 3D V-Cache: a tensão e o clock são limitados de fábrica. Use apenas Curve Optimizer, sem aumentar o clock máximo.");
            }

            pbo.Add("Perdeu o controle? Na BIOS use \"Load Optimized Defaults\" para voltar a tudo ao padrão.");
            sections.Add(new RecipeSection($"PBO e Curve Optimizer ({(gen > 0 ? "Zen " + gen : "AMD Ryzen")})", pbo));
        }
        else
        {
            sections.Add(new RecipeSection("Processador", new[]
            {
                "Para esta CPU não há ajuste de BIOS recomendado pelo MB Extreme. Mantenha os limites de potência no padrão do fabricante.",
            }));
        }

        sections.Add(new RecipeSection("Conferir o resultado", new[]
        {
            "No MB Extreme, rode o Benchmark de novo e compare em Antes e depois.",
            "Rode o Teste de estresse por núcleo por 10 a 30 minutos e veja temperatura e erros.",
            "Jogue por um tempo e observe travamentos. Instabilidade significa que o ajuste foi longe demais.",
        }));

        sections.Add(new RecipeSection("Voltar atrás", new[]
        {
            "BIOS: \"Load Optimized Defaults\" restaura os valores de fábrica (depois ative o perfil de memória de novo).",
            "Se o PC não liga depois de um ajuste: limpe o CMOS (jumper ou bateria da placa) e tente de novo com valores menores.",
        }));

        return sections;
    }

    public static string RecipeToText(HardwareInventory hw)
    {
        var text = new StringBuilder();
        text.AppendLine($"MB Extreme Beta - receita de BIOS para: {hw.Cpu.Name} / {hw.Board.Manufacturer} {hw.Board.Product}");
        foreach (var section in BuildRecipe(hw))
        {
            text.AppendLine().AppendLine("== " + section.Title);
            int i = 1;
            foreach (string step in section.Steps) text.AppendLine($"{i++}. {step}");
        }

        return text.ToString();
    }
}
