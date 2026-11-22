namespace MBExtreme.Core.Hardware;

/// <summary>Geração da porta (a velocidade máxima que ela aceita).</summary>
public enum UsbPortKind { Usb2, Usb3, Usb3Plus }

/// <summary>Onde a porta fica, quando a placa-mãe informa.</summary>
public enum UsbPanel { Unknown, Back, Front, Internal }

/// <summary>De onde vem a porta: controlador USB do processador, do chipset, de um chip extra ou de um hub.</summary>
public enum UsbSource { Unknown, Cpu, Chipset, ExtraChip }

/// <summary>Para que serve o aparelho ligado (e para que serve a recomendação).</summary>
public enum UsbUse { None, Controller, BluetoothAdapter, CaptureCard, Webcam, Keyboard, Mouse, Audio, Storage, Hub, Other }

/// <summary>Uma porta como o Windows informa, antes de juntar a parte USB 2.0 com a parte USB 3.x do mesmo conector.</summary>
public sealed record UsbRawPort(string HubId, int Index, bool Usb2, bool Usb3, bool UserConnectable, bool TypeC, int CompanionIndex,
    string ControllerId, string ControllerName, UsbSource Source, bool Controller10G, bool BehindHub, UsbPanel Panel = UsbPanel.Unknown,
    string? Device = null, UsbUse Use = UsbUse.None, bool OperatingSsPlus = false, string? InstanceId = null);

/// <summary>Um conector físico, já com nome, tipo e o que está ligado nele.</summary>
public sealed record UsbPortInfo(string Id, string Name, UsbPortKind Kind, bool TypeC, UsbPanel Panel, UsbSource Source, string ControllerId,
    string ControllerName, bool BehindHub, string? Device, UsbUse Use, string? InstanceId = null)
{
    public string KindText => (TypeC ? "USB-C" : Kind == UsbPortKind.Usb2 ? "USB 2.0" : "USB 3.2") + Kind switch
    {
        UsbPortKind.Usb2 => TypeC ? " · 480 Mb/s" : "",
        UsbPortKind.Usb3 => " · 5 Gb/s",
        _ => " · 10 Gb/s",
    };

    public string SourceText => BehindHub ? "Hub interno" : Source switch
    {
        UsbSource.Cpu => "Processador",
        UsbSource.Chipset => "Chipset",
        UsbSource.ExtraChip => "Chip extra",
        _ => "Placa-mãe",
    };
}

/// <summary>Qual porta usar para cada coisa e por quê.</summary>
public sealed record UsbAdvice(UsbUse For, UsbPortInfo? Port, string Why, bool AlreadyThere);

/// <summary>Junta as portas, dá nomes ("Traseira 1", "Frontal C") e recomenda onde ligar controle, adaptador Bluetooth e placa de captura.</summary>
public static class UsbPorts
{
    /// <summary>Fabricantes de controladores USB pelo PCI (VEN_xxxx) e se o controlador está no processador ou no chipset.</summary>
    public static (UsbSource Source, bool TenGig) ClassifyController(string hardwareId, string description)
    {
        string id = hardwareId.ToUpperInvariant();
        string vendor = Between(id, "VEN_", 4), device = Between(id, "DEV_", 4);
        string desc = description.ToUpperInvariant();
        switch (vendor)
        {
            case "1022": // AMD: 43xx são os chipsets (Promontory); o resto fica no processador
                bool chipset = device.StartsWith("43", StringComparison.Ordinal);
                bool amd10 = device is "43F7" or "43EE" or "43D5" or "15B6" or "15B7" or "15B8" or "149C" or "1639" or "161E" or "161F" or "15E0" or "15E1";
                return (chipset ? UsbSource.Chipset : UsbSource.Cpu, amd10);
            case "8086": // Intel: USB4/Thunderbolt (TCSS) no processador; o resto no chipset (PCH)
                bool cpu = desc.Contains("USB4") || desc.Contains("THUNDERBOLT") || device is "9A13" or "A71E" or "7EC0" or "A0ED" or "461E" or "7E7E";
                bool intel10 = cpu || desc.Contains("3.20") || desc.Contains("3.2") || device is "7AE0" or "7A60" or "51ED" or "54ED" or "A36D" or "06ED" or "43ED";
                return (cpu ? UsbSource.Cpu : UsbSource.Chipset, intel10);
            case "1B21": // ASMedia
                return (UsbSource.ExtraChip, device is "2142" or "2242" or "3042" or "3142" or "3241" or "3242");
            case "1912": case "1033": case "1106": case "1B73": case "1B6F": case "104C": // Renesas, NEC, VIA, Fresco, Etron, TI
                return (UsbSource.ExtraChip, false);
            default:
                return (UsbSource.Unknown, false);
        }
    }

    /// <summary>Lado do gabinete pelo _PLD da ACPI (bits 67 a 69): 4 = frente, 5 = trás.</summary>
    public static UsbPanel PanelFromPld(byte[]? pld)
    {
        if (pld is null || pld.Length < 9) return UsbPanel.Unknown;
        int panel = (pld[8] >> 3) & 0x7;
        return panel switch { 4 => UsbPanel.Front, 5 => UsbPanel.Back, _ => UsbPanel.Unknown };
    }

    /// <summary>Para que serve o aparelho, pelo nome, pela classe do Windows e pelo fabricante.</summary>
    public static UsbUse Classify(string? name, string? deviceClass, ushort vendor, bool isHub)
    {
        if (isHub) return UsbUse.Hub;
        string n = (name ?? "").ToLowerInvariant(), c = (deviceClass ?? "").ToLowerInvariant();
        if (c == "bluetooth" || n.Contains("bluetooth")) return UsbUse.BluetoothAdapter;
        if (vendor is 0x054C or 0x045E or 0x2DC8 or 0x0F0D or 0x0E6F || n.Contains("controller") || n.Contains("gamepad") || n.Contains("dualsense") || n.Contains("wireless controller") || n.Contains("controle"))
            return UsbUse.Controller;
        if (n.Contains("capture") || n.Contains("captura") || n.Contains("elgato") || n.Contains("avermedia") || n.Contains("cam link") || n.Contains("hd60") || n.Contains("4k60") || n.Contains("live gamer") || n.Contains("usb video"))
            return UsbUse.CaptureCard;
        if (c is "camera" or "image" || n.Contains("webcam") || n.Contains("camera") || n.Contains("câmera") || n.Contains("c920") || n.Contains("brio")) return UsbUse.Webcam;
        if (c == "keyboard" || n.Contains("keyboard") || n.Contains("teclado")) return UsbUse.Keyboard;
        if (c == "mouse" || n.Contains("mouse")) return UsbUse.Mouse;
        if (c is "media" or "audioendpoint" || n.Contains("audio") || n.Contains("áudio") || n.Contains("headset") || n.Contains("microfone") || n.Contains("microphone")) return UsbUse.Audio;
        if (c is "diskdrive" or "usbstor" || n.Contains("mass storage") || n.Contains("armazenamento") || n.Contains("ssd") || n.Contains("flash")) return UsbUse.Storage;
        return string.IsNullOrEmpty(name) ? UsbUse.None : UsbUse.Other;
    }

    /// <summary>
    /// Junta cada porta USB 3.x com a sua "gêmea" USB 2.0 (o mesmo conector aparece duas vezes no Windows), esconde as portas internas
    /// vazias e dá nome: "Traseira 1", "Frontal 2", "Traseira C"; sem a informação do lado, "Porta 1".
    /// </summary>
    public static IReadOnlyList<UsbPortInfo> Build(IReadOnlyList<UsbRawPort> raw)
    {
        var byKey = raw.ToDictionary(p => (p.HubId, p.Index));
        var used = new HashSet<(string, int)>();
        var merged = new List<(UsbRawPort Main, UsbRawPort? Twin)>();
        foreach (var p in raw.OrderBy(p => p.HubId, StringComparer.Ordinal).ThenBy(p => p.Usb3 ? 0 : 1).ThenBy(p => p.Index))
        {
            if (used.Contains((p.HubId, p.Index))) continue;
            used.Add((p.HubId, p.Index));
            UsbRawPort? twin = null;
            if (p.CompanionIndex > 0 && byKey.TryGetValue((p.HubId, p.CompanionIndex), out var t) && !used.Contains((t.HubId, t.Index)))
            {
                twin = t;
                used.Add((t.HubId, t.Index));
            }
            merged.Add((p, twin));
        }

        var ports = new List<(UsbPortInfo Info, int Order)>();
        int order = 0;
        foreach (var (main, twin) in merged)
        {
            var withDevice = main.Device is not null ? main : twin?.Device is not null ? twin : null;
            bool usb3 = main.Usb3 || twin?.Usb3 == true;
            bool typeC = main.TypeC || twin?.TypeC == true;
            bool user = main.UserConnectable || twin?.UserConnectable == true;
            var panel = main.Panel != UsbPanel.Unknown ? main.Panel : twin?.Panel ?? UsbPanel.Unknown;
            if (!user)
            {
                if (withDevice is null || withDevice.Use == UsbUse.Hub) continue; // porta interna vazia ou hub interno (as portas dele entram à parte)
                panel = UsbPanel.Internal;
            }
            var kind = !usb3 ? UsbPortKind.Usb2
                : main.Controller10G || main.OperatingSsPlus || twin?.OperatingSsPlus == true ? UsbPortKind.Usb3Plus : UsbPortKind.Usb3;
            ports.Add((new UsbPortInfo($"{main.HubId}#{main.Index}", "", kind, typeC, panel, main.Source, main.ControllerId, main.ControllerName,
                main.BehindHub, withDevice?.Device, withDevice?.Use ?? UsbUse.None, withDevice?.InstanceId), order++));
        }

        // nomes: traseiras primeiro, depois frontais, internas e as sem lado; tipo C ganha "C" (C2, C3 se houver mais de uma)
        var named = new List<UsbPortInfo>();
        foreach (var group in ports.GroupBy(p => p.Info.Panel).OrderBy(g => g.Key switch { UsbPanel.Back => 0, UsbPanel.Front => 1, UsbPanel.Unknown => 2, _ => 3 }))
        {
            string prefix = group.Key switch { UsbPanel.Back => "Traseira", UsbPanel.Front => "Frontal", UsbPanel.Internal => "Interna", _ => "Porta" };
            int a = 0, c = 0;
            var list = group.OrderBy(p => p.Info.TypeC ? 1 : 0).ThenBy(p => p.Order).ToList();
            int cCount = list.Count(p => p.Info.TypeC);
            foreach (var (info, _) in list)
            {
                string name = info.TypeC ? $"{prefix} C{(cCount > 1 ? (++c).ToString() : "")}" : $"{prefix} {++a}";
                named.Add(info with { Name = name });
            }
        }
        return named;
    }

    /// <summary>Recomendação para o controle no cabo, o adaptador Bluetooth e a placa de captura.</summary>
    public static IReadOnlyList<UsbAdvice> Recommend(IReadOnlyList<UsbPortInfo> ports)
    {
        var usable = ports.Where(p => p.Panel != UsbPanel.Internal).ToList();
        var advice = new List<UsbAdvice>
        {
            Pick(UsbUse.Controller, usable, ControllerScore,
                "Direta na placa-mãe, sem hub. Para o controle tanto faz 2.0 ou 3.2: o que conta é ser direta e não dividir com placa de captura."),
            Pick(UsbUse.BluetoothAdapter, usable, BluetoothScore,
                "Uma porta USB 2.0 traseira, com cabo extensor se puder: as USB 3.0 em uso geram interferência em 2,4 GHz, a faixa do Bluetooth."),
            Pick(UsbUse.CaptureCard, usable, p => CaptureScore(p, usable),
                "A mais veloz, de preferência do processador, sem dividir o controlador com webcam ou outro aparelho pesado."),
        };
        return advice;
    }

    private static UsbAdvice Pick(UsbUse use, List<UsbPortInfo> ports, Func<UsbPortInfo, double> score, string why)
    {
        var current = ports.FirstOrDefault(p => p.Use == use);
        var candidates = ports.Where(p => p.Use == UsbUse.None || p.Use == use).ToList();
        if (candidates.Count == 0) return new UsbAdvice(use, null, "Nenhuma porta livre encontrada.", false);
        var best = candidates.OrderByDescending(score).ThenBy(p => p.Name, StringComparer.Ordinal).First();
        // se o aparelho já está numa porta tão boa quanto a melhor, fica onde está
        if (current is not null && score(current) >= score(best) - 0.01) best = current;
        return new UsbAdvice(use, best, why, current is not null && current.Id == best.Id);
    }

    private static double ControllerScore(UsbPortInfo p) =>
        (p.BehindHub ? -10 : 0) + (p.Panel == UsbPanel.Back ? 3 : p.Panel == UsbPanel.Front ? -2 : 0)
        + p.Source switch { UsbSource.Chipset => 2, UsbSource.Cpu => 2, UsbSource.ExtraChip => 0, _ => 1 }
        + (p.Use == UsbUse.Controller ? 0.5 : 0);

    private static double BluetoothScore(UsbPortInfo p) =>
        (p.BehindHub ? -10 : 0) + (p.Kind == UsbPortKind.Usb2 ? 4 : 0) + (p.Panel == UsbPanel.Back ? 2 : p.Panel == UsbPanel.Front ? 1 : 0)
        + (p.TypeC ? -3 : 0) + (p.Use == UsbUse.BluetoothAdapter ? 0.5 : 0);

    private static double CaptureScore(UsbPortInfo p, List<UsbPortInfo> all)
    {
        int heavyNeighbours = all.Count(o => o.Id != p.Id && o.ControllerId == p.ControllerId && o.Use is UsbUse.Webcam or UsbUse.CaptureCard or UsbUse.Storage);
        return (p.BehindHub ? -10 : 0) + p.Kind switch { UsbPortKind.Usb3Plus => 6, UsbPortKind.Usb3 => 4, _ => -6 }
            + (p.Source == UsbSource.Cpu ? 2 : p.Source == UsbSource.Chipset ? 1 : 0) - heavyNeighbours * 2 + (p.Panel == UsbPanel.Back ? 1 : 0)
            + (p.Use == UsbUse.CaptureCard ? 0.5 : 0);
    }

    public static string UseText(UsbUse use) => use switch
    {
        UsbUse.Controller => "Controle no cabo",
        UsbUse.BluetoothAdapter => "Adaptador Bluetooth",
        UsbUse.CaptureCard => "Placa de captura",
        UsbUse.Webcam => "Webcam",
        UsbUse.Keyboard => "Teclado",
        UsbUse.Mouse => "Mouse",
        UsbUse.Audio => "Áudio",
        UsbUse.Storage => "Armazenamento",
        UsbUse.Hub => "Hub USB",
        _ => "Outro",
    };

    private static string Between(string text, string key, int length)
    {
        int i = text.IndexOf(key, StringComparison.Ordinal);
        return i < 0 || i + key.Length + length > text.Length ? "" : text.Substring(i + key.Length, length);
    }
}
