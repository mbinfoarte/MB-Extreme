namespace MBExtreme.Core.Hardware;

/// <summary>Um assunto do menu da BIOS (Undervolt, Overclock, TPM...): o que é, ganho, risco, onde fica, valores e passo a passo.</summary>
public sealed record BiosTopic(
    string Key, string Title, string Glyph, string WhatIs, string Gain, string Risk, string Status, string StatusBrush,
    string WhereInBios, IReadOnlyList<(string Setting, string Value)> Settings, IReadOnlyList<string> Steps, string Test, string? Page = null);

/// <summary>
/// Guias de BIOS montados pelo hardware do PC: o caminho do menu muda com a marca da placa-mãe (ASUS, MSI, Gigabyte, ASRock) e os
/// valores mudam com o processador (Intel ou AMD, com ou sem desbloqueio) e a memória (DDR4 ou DDR5).
/// </summary>
public static class BiosTopics
{
    public static IReadOnlyList<(string Key, string Title, string Glyph)> Menu { get; } = new[]
    {
        ("plan", "Plano do seu PC", ""),
        ("undervolt", "Undervolt", ""),
        ("overclock", "Overclock", ""),
        ("tpm", "TPM e Secure Boot", ""),
        ("rebar", "Resizable BAR", ""),
        ("xmp", "XMP / EXPO da memória", ""),
        ("fans", "Fans e coolers", ""),
        ("water", "Water cooler", ""),
    };

    /// <summary>Marca da placa-mãe pelo fabricante informado pelo Windows.</summary>
    public static string Brand(string manufacturer)
    {
        string m = manufacturer.ToUpperInvariant();
        return m.Contains("ASUS") ? "ASUS" : m.Contains("MICRO-STAR") || m.Contains("MSI") ? "MSI" : m.Contains("GIGABYTE") ? "Gigabyte"
             : m.Contains("ASROCK") ? "ASRock" : m.Contains("BIOSTAR") ? "Biostar" : "";
    }

    /// <summary>Caminho do menu na BIOS para o assunto, pela marca da placa.</summary>
    public static string Where(string brand, string key) => (brand, key) switch
    {
        ("ASUS", "undervolt" or "overclock") => "Modo avançado (F7) › Ai Tweaker",
        ("ASUS", "tpm") => "Advanced › PCH-FW Configuration (Intel PTT) ou AMD fTPM Configuration; Boot › Secure Boot",
        ("ASUS", "rebar") => "Advanced › PCI Subsystem Settings › Above 4G Decoding e Re-Size BAR Support",
        ("ASUS", "xmp") => "Modo EZ (F7) › Ai Overclock Tuner › XMP I / EXPO",
        ("ASUS", "fans" or "water") => "Monitor › Q-Fan Configuration (ou Q-Fan Control, F6)",
        ("MSI", "undervolt" or "overclock") => "Advanced (F7) › OC",
        ("MSI", "tpm") => "Settings › Security › Trusted Computing; Settings › Advanced › Windows OS Configuration › Secure Boot",
        ("MSI", "rebar") => "Settings › Advanced › PCI Subsystem Settings › Above 4G memory e Re-Size BAR Support",
        ("MSI", "xmp") => "Tela inicial › botão A-XMP / EXPO",
        ("MSI", "fans" or "water") => "Hardware Monitor › Smart Fan Mode",
        ("Gigabyte", "undervolt" or "overclock") => "Advanced Mode (F2) › Tweaker",
        ("Gigabyte", "tpm") => "Settings › Miscellaneous › Intel Platform Trust Technology (PTT) ou AMD CPU fTPM; Boot › Secure Boot",
        ("Gigabyte", "rebar") => "Settings › IO Ports › Above 4G Decoding e Re-Size BAR Support",
        ("Gigabyte", "xmp") => "Tweaker › Extreme Memory Profile (X.M.P.) / EXPO",
        ("Gigabyte", "fans" or "water") => "Smart Fan 6 (F6)",
        ("ASRock", "undervolt" or "overclock") => "Advanced Mode (F6) › OC Tweaker",
        ("ASRock", "tpm") => "Security › Intel PTT ou AMD fTPM Switch; Security › Secure Boot",
        ("ASRock", "rebar") => "Advanced › Chipset Configuration › Above 4G Decoding e Re-Size BAR Support",
        ("ASRock", "xmp") => "OC Tweaker › DRAM Configuration › Load XMP / EXPO Setting",
        ("ASRock", "fans" or "water") => "H/W Monitor › FAN-Tastic Tuning",
        (_, "undervolt" or "overclock") => "Procure a aba de overclock: Ai Tweaker, OC, Tweaker ou OC Tweaker",
        (_, "tpm") => "Procure Security, Trusted Computing, PTT ou fTPM; e Secure Boot na aba Boot",
        (_, "rebar") => "Procure Above 4G Decoding e Re-Size BAR Support (Advanced ou PCI)",
        (_, "xmp") => "Procure XMP, EXPO, DOCP ou Memory Profile",
        (_, "fans" or "water") => "Procure Fan, Q-Fan, Smart Fan ou Hardware Monitor",
        _ => "",
    };

    public static IReadOnlyList<BiosTopic> Build(HardwareInventory hw)
    {
        string brand = Brand(hw.Board.Manufacturer);
        bool amd = hw.Cpu.IsAmd;
        string cpu = hw.Cpu.Name;
        bool unlockedIntel = !amd && System.Text.RegularExpressions.Regex.IsMatch(cpu, @"\d{4,5}K", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        bool x3d = amd && cpu.Contains("X3D", StringComparison.OrdinalIgnoreCase);
        bool ddr5 = hw.Memory.Any(m => m.Type.Contains("DDR5", StringComparison.OrdinalIgnoreCase));
        int rated = hw.Memory.Count > 0 ? hw.Memory.Max(m => m.RatedMhz) : 0;
        int configured = hw.Memory.Count > 0 ? hw.Memory.Min(m => m.ConfiguredMhz) : 0;
        bool xmpOn = rated > 0 && configured > 0 && configured >= rated - 100;
        string profile = amd && ddr5 ? "EXPO" : "XMP";
        string laptopNote = hw.IsLaptop ? " Em notebook a BIOS quase sempre esconde essas opções; se não aparecerem, não force." : "";
        string[] common =
        {
            "Atualize a BIOS antes (aba Plano do seu PC › Atualizar a BIOS).",
            "Ligue o PC e aperte DEL (ou F2) várias vezes até abrir a BIOS.",
        };
        string[] finish =
        {
            "Salve e reinicie com F10 › Yes.",
            "No Windows, rode o Teste de estresse por 10 minutos e jogue 30 minutos.",
            "Estável: anote e, se quiser, avance um passo. Travou ou reiniciou: volte ao valor anterior.",
        };
        var list = new List<BiosTopic>();

        // ---- undervolt
        list.Add(amd
            ? new BiosTopic("undervolt", "Undervolt (Curve Optimizer)", "",
                "Diminui a tensão do processador em cada núcleo. Ele esquenta menos e, com a folga de temperatura, sustenta clock mais alto.",
                "−5 a −15 °C e 2 a 5% mais desempenho em carga longa.", "Baixo: se instável, o PC só trava ou reinicia; volte um passo.",
                "Recomendado", "AccentBrush", Where(brand, "undervolt") + " › Precision Boost Overdrive › Curve Optimizer",
                new[] { ("Precision Boost Overdrive", "Advanced"), ("Curve Optimizer", "All Cores"), ("Sign", "Negative"), ("Magnitude", "10 (depois 15, 20, até 30)") },
                common.Concat(new[] { "Abra o menu indicado e ligue Precision Boost Overdrive em Advanced.", "Em Curve Optimizer escolha All Cores, Negative, Magnitude 10." }).Concat(finish).ToList(),
                "Teste de estresse (CPU) 10 min + Cinebench ou um jogo pesado. Temperatura deve cair.", "stress")
            : new BiosTopic("undervolt", "Undervolt (Offset de tensão)", "",
                "Diminui a tensão do processador mantendo o clock. Menos calor, menos ruído e clock mais estável nos jogos.",
                "−5 a −12 °C e menos queda de clock por temperatura.", "Baixo: se instável, o PC trava; volte um passo. Algumas placas bloqueiam (Undervolt Protection).",
                HardwareAdvisor.IsIntel13Or14GenDesktop(hw.Cpu) ? "Faça depois da BIOS nova (Intel 13ª/14ª)" : "Recomendado", "AccentBrush",
                Where(brand, "undervolt") + " › CPU Core/Cache Voltage",
                new[] { ("CPU Core/Cache Voltage", "Adaptive (ou Offset)"), ("Offset Mode Sign", "−"), ("Offset", "0,050 V (depois 0,060, 0,070...)"), ("IA VR Voltage Limit", HardwareAdvisor.IsIntel13Or14GenDesktop(hw.Cpu) ? "1400 mV" : "Auto") },
                common.Concat(new[] { "Abra o menu indicado e mude a tensão do núcleo para Adaptive/Offset.", "Coloque Offset negativo de 0,050 V." }).Concat(finish).ToList(),
                "Teste de estresse (CPU) 10 min. Se a temperatura cair e não travar, tente −0,010 V a mais.", "stress"));

        // ---- overclock
        list.Add(amd
            ? new BiosTopic("overclock", "Overclock do processador (PBO)", "",
                x3d ? "Os Ryzen X3D não aceitam aumento de clock: use só o Curve Optimizer (aba Undervolt)."
                    : "Deixa o processador ir além do boost de fábrica quando tem folga de energia e temperatura.",
                x3d ? "—" : "3 a 8% em programas que usam todos os núcleos.", "Médio: mais calor e consumo. Precisa de bom cooler." + laptopNote,
                x3d ? "Não disponível neste processador" : "Opcional", x3d ? "MutedBrush" : "WarnBrush",
                Where(brand, "overclock") + " › Precision Boost Overdrive",
                x3d ? Array.Empty<(string, string)>() : new[] { ("Precision Boost Overdrive", "Advanced"), ("PBO Limits", "Motherboard"), ("Max CPU Boost Clock Override", "+100 MHz (depois +200)"), ("PBO Scalar", "Auto") },
                x3d ? new[] { "Use a aba Undervolt (Curve Optimizer)." }.ToList() : common.Concat(new[] { "Ligue PBO em Advanced com limites da placa (Motherboard).", "Max CPU Boost Clock Override em +100 MHz." }).Concat(finish).ToList(),
                "Teste de estresse 10 min. Acima de 90 °C, volte o Boost Override.", "stress")
            : new BiosTopic("overclock", "Overclock do processador", "",
                unlockedIntel ? "Aumenta o multiplicador dos núcleos de desempenho (P-cores)." : "Este processador não é desbloqueado (sem K no nome): o multiplicador não sobe. Use o Undervolt e o XMP.",
                unlockedIntel ? "3 a 6% de desempenho." : "—", "Médio a alto: mais calor e consumo. Precisa de water cooler ou air cooler grande." + laptopNote,
                unlockedIntel ? "Opcional (avançado)" : "Não disponível neste processador", unlockedIntel ? "WarnBrush" : "MutedBrush",
                Where(brand, "overclock") + " › CPU Core Ratio",
                unlockedIntel ? new[] { ("CPU Core Ratio", "Sync All Cores"), ("P-Core Ratio", "+1 acima do padrão (ex.: 53)"), ("CPU Load-Line Calibration", "Nível médio (4 de 8)"), ("Limite de temperatura", "90 °C") } : Array.Empty<(string, string)>(),
                unlockedIntel ? common.Concat(new[] { "Coloque CPU Core Ratio em Sync All Cores e suba o P-Core Ratio em 1.", "Ajuste a Load-Line Calibration no nível médio." }).Concat(finish).ToList() : new[] { "Use as abas Undervolt e XMP / EXPO." }.ToList(),
                "Teste de estresse 10 min. Acima de 90 °C ou travou: volte o multiplicador.", "stress"));

        // ---- TPM
        list.Add(new BiosTopic("tpm", "TPM 2.0 e Secure Boot", "",
            "Chip de segurança (no processador: " + (amd ? "AMD fTPM" : "Intel PTT") + ") exigido pelo Windows 11, por alguns jogos com antitrapaça (Valorant, Battlefield, Call of Duty) e pelo BitLocker.",
            "Windows 11 e jogos com antitrapaça funcionando.", "Nenhum. Só não desligue o TPM com o BitLocker ligado.",
            "Confira no Windows 11", "MutedBrush", Where(brand, "tpm"),
            new[] { (amd ? "AMD fTPM" : "Intel PTT", "Enabled"), ("Boot Mode / CSM", "UEFI (CSM desligado)"), ("Secure Boot", "Enabled (Windows UEFI mode)") },
            common.Concat(new[] { $"Ligue o {(amd ? "AMD fTPM" : "Intel PTT")}.", "Na aba Boot, desligue o CSM (o disco precisa ser GPT) e ligue o Secure Boot." }).Concat(new[] { "Salve e reinicie com F10 › Yes.", "No MB, abra Meu PC › Windows para conferir TPM e Secure Boot." }).ToList(),
            "Meu PC › Windows mostra TPM 2.0 e Secure Boot ligados.", "win11"));

        // ---- Resizable BAR
        bool? bar = hw.ResizableBar;
        list.Add(new BiosTopic("rebar", "Resizable BAR", "",
            "Deixa o processador acessar toda a memória da placa de vídeo de uma vez. Placas RTX 30/40/50, Radeon RX 6000+ e Intel Arc aproveitam.",
            "0 a 10% de FPS (depende do jogo). Na Intel Arc é obrigatório.", "Nenhum. Precisa de UEFI (CSM desligado).",
            bar switch { true => "Ligado", false => "Desligado", _ => "Sem leitura" }, bar switch { true => "OkBrush", false => "WarnBrush", _ => "MutedBrush" },
            Where(brand, "rebar"),
            new[] { ("Above 4G Decoding", "Enabled"), ("Re-Size BAR Support", "Enabled / Auto"), ("CSM", "Disabled") },
            common.Concat(new[] { "Ligue Above 4G Decoding e depois Re-Size BAR Support.", "Confirme que o CSM está desligado." }).Concat(new[] { "Salve e reinicie com F10 › Yes.", "Abra esta tela de novo: o status muda para Ligado." }).ToList(),
            "O status desta tela muda para Ligado (o MB lê do driver de vídeo)."));

        // ---- XMP / EXPO
        list.Add(new BiosTopic("xmp", $"Perfil {profile} da memória", "",
            $"A memória sai de fábrica numa velocidade baixa de segurança. O perfil {profile} liga a velocidade da etiqueta" + (rated > 0 ? $" ({rated} MHz)." : "."),
            "5 a 15% mais FPS em jogos que dependem do processador.", "Baixo. Se o PC não ligar, ele volta sozinho ou limpe o CMOS.",
            rated == 0 ? "Sem leitura" : xmpOn ? $"Ligado ({configured} MHz)" : $"Desligado ({configured} de {rated} MHz)", rated == 0 ? "MutedBrush" : xmpOn ? "OkBrush" : "BadBrush",
            Where(brand, "xmp"),
            new[] { ($"Perfil {profile}", "Perfil 1 (Profile 1)"), ("Memory Context Restore", ddr5 ? "Enabled (liga mais rápido)" : "—"), ("Gear Mode", !amd && ddr5 ? "Gear 2 (padrão)" : "Auto") },
            common.Concat(new[] { $"Escolha {profile} › Perfil 1." }).Concat(new[] { "Salve e reinicie com F10 › Yes (o primeiro boot pode demorar 1 a 2 minutos).", "Abra esta tela de novo: o status mostra a velocidade nova." }).ToList(),
            "Teste de estresse (Memória) 10 min. Travou: volte para Auto e atualize a BIOS.", "stress"));

        // ---- fans
        list.Add(new BiosTopic("fans", "Fans e coolers", "",
            "Curva das ventoinhas pela temperatura do processador: silêncio no uso leve e força total nos jogos.",
            "PC mais silencioso e sem esquentar.", "Nenhum, desde que a curva chegue a 100% antes de 85 °C.",
            "Ajuste pela BIOS ou pelo MB", "MutedBrush", Where(brand, "fans"),
            new[] { ("CPU_FAN", "Curva: 40 °C 30% · 60 °C 50% · 75 °C 75% · 85 °C 100%"), ("SYS_FAN / CHA_FAN", "Mesma curva, um pouco mais baixa"), ("Step Up / Down", "1 a 2 segundos (sem sobe-desce)"), ("Modo", "PWM (4 pinos) ou DC (3 pinos)") },
            common.Concat(new[] { "Abra o controle de fans, escolha CPU_FAN e monte a curva da tabela.", "Repita nas fans do gabinete." }).Concat(new[] { "Salve e reinicie com F10 › Yes.", "No MB, Personalização › Coolers mostra as rotações ao vivo." }).ToList(),
            "Em jogo, o processador deve ficar abaixo de 85 °C.", "fans"));

        // ---- water cooler
        list.Add(new BiosTopic("water", "Water cooler", "",
            "No water cooler, a bomba precisa girar sempre no máximo; quem varia são as fans do radiador.",
            "Temperatura estável e sem barulho de bomba variando.", "Alto se a bomba ficar desligada: o processador esquenta em segundos.",
            "Confira a bomba", "WarnBrush", Where(brand, "water"),
            new[] { ("AIO_PUMP / W_PUMP / CPU_FAN (bomba)", "100% fixo (Full Speed)"), ("Fans do radiador", "Curva pela temperatura do processador"), ("CPU Fan Fail Warning", "Ligado se a bomba está no CPU_FAN"), ("Rotação da bomba", "acima de 1500 RPM") },
            common.Concat(new[] { "Encontre o conector da bomba (AIO_PUMP, W_PUMP ou CPU_FAN) e deixe em 100% / Full Speed.", "Nas fans do radiador, monte a curva da aba Fans e coolers." }).Concat(new[] { "Salve e reinicie com F10 › Yes.", "Confira a rotação da bomba em Personalização › Coolers." }).ToList(),
            "Em jogo, até 80 °C é ótimo. Acima de 90 °C, a bomba ou a pasta térmica pedem atenção.", "fans"));
        return list;
    }
}
