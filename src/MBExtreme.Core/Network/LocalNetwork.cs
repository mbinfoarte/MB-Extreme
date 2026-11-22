using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Network;

/// <summary>Um aparelho encontrado na rede local (casa ou empresa).</summary>
public sealed record LanDevice(string Ip, string Mac, string Vendor, string Name, double? PingMs, bool IsRouter, bool IsThisPc)
{
    public string Kind => IsRouter ? "Roteador" : IsThisPc ? "Este PC" : LocalNetwork.KindOf(Vendor, Name);
}

/// <summary>Qualidade da ligação até o roteador (20 pings): média, oscilação e perda.</summary>
public sealed record LinkQuality(double? AverageMs, double JitterMs, double LossPercent, bool Wifi)
{
    public bool Slow => AverageMs is null || LossPercent >= 2 || AverageMs > (Wifi ? 15 : 5) || JitterMs > (Wifi ? 10 : 3);

    public string Text => AverageMs is null
        ? "O roteador não respondeu: confira o cabo ou o Wi-Fi."
        : Slow
            ? $"Lentidão na rede local: {AverageMs:0} ms até o roteador, oscilação {JitterMs:0} ms, perda {LossPercent:0}%." +
              (Wifi ? " No Wi-Fi: chegue mais perto do roteador, use 5 GHz ou cabo." : " No cabo isso não é normal: troque o cabo ou a porta do roteador.")
            : $"Rede local boa: {AverageMs:0} ms até o roteador, oscilação {JitterMs:0} ms, sem perda.";
}

/// <summary>Uma configuração avançada da placa de rede (o que o Gerenciador de Dispositivos mostra na aba Avançado).</summary>
public sealed record NicProperty(string Adapter, string DisplayName, string DisplayValue, string Keyword, string Value, IReadOnlyList<string> ValidValues);

/// <summary>Mudança recomendada numa configuração da placa de rede.</summary>
public sealed record NicChange(string Adapter, string Title, string Keyword, string From, string To, string Why);

/// <summary>
/// Rede local: varredura dos aparelhos (ping em toda a faixa + tabela ARP do Windows + fabricante pelo MAC), qualidade até o roteador
/// e ajuste da placa de rede pelas configurações avançadas do driver (só as que existem nela, com valores que o driver aceita).
/// </summary>
public static class LocalNetwork
{
    // ---------------------------------------------------------------- fabricante pelo MAC (prefixos mais comuns no Brasil)

    private static readonly (string Prefix, string Vendor)[] Ouis =
    {
        ("00:1A:11", "Google"), ("F4:F5:D8", "Google"), ("3C:5A:B4", "Google"), ("DC:A6:32", "Raspberry Pi"), ("B8:27:EB", "Raspberry Pi"),
        ("F0:18:98", "Apple"), ("A4:83:E7", "Apple"), ("3C:22:FB", "Apple"), ("BC:D0:74", "Apple"), ("AC:BC:32", "Apple"),
        ("FC:A1:3E", "Samsung"), ("8C:79:F5", "Samsung"), ("50:85:69", "Samsung"), ("A0:CC:2B", "Samsung"), ("E8:50:8B", "Samsung"),
        ("64:09:80", "Xiaomi"), ("28:6C:07", "Xiaomi"), ("78:11:DC", "Xiaomi"), ("04:CF:8C", "Xiaomi"),
        ("50:C7:BF", "TP-Link"), ("C0:06:C3", "TP-Link"), ("98:DA:C4", "TP-Link"), ("60:32:B1", "TP-Link"), ("EC:08:6B", "TP-Link"),
        ("00:E0:4C", "Realtek"), ("00:1B:21", "Intel"), ("3C:97:0E", "Intel"), ("A4:BB:6D", "Intel"), ("9C:B6:D0", "Intel"),
        ("E4:5F:01", "Raspberry Pi"), ("24:0A:C4", "Espressif (casa inteligente)"), ("84:F3:EB", "Espressif (casa inteligente)"), ("A4:CF:12", "Espressif (casa inteligente)"),
        ("C8:3A:35", "Tenda"), ("00:0C:43", "Ralink"), ("44:D9:E7", "Ubiquiti"), ("F0:9F:C2", "Ubiquiti"), ("74:DA:88", "TP-Link"),
        ("00:50:F2", "Microsoft"), ("7C:ED:8D", "Microsoft (Xbox)"), ("00:D9:D1", "Sony (PlayStation)"), ("BC:60:A7", "Sony (PlayStation)"),
        ("98:B6:E9", "Nintendo"), ("40:F4:07", "Nintendo"), ("74:C2:46", "Amazon (Alexa)"), ("FC:65:DE", "Amazon (Alexa)"),
        ("CC:2D:21", "Tenda"), ("00:1E:58", "D-Link"), ("C8:D3:A3", "D-Link"), ("E8:65:D4", "Tenda"), ("00:25:9C", "Cisco"),
        ("20:F4:78", "Xiaomi"), ("B0:A7:B9", "TP-Link"), ("D8:0D:17", "TP-Link"), ("38:D5:47", "ASUS"), ("04:D9:F5", "ASUS"),
        ("00:11:32", "Synology"), ("70:85:C2", "ASRock"), ("1C:69:7A", "Elitegroup"), ("D8:BB:C1", "Micro-Star (MSI)"), ("18:C0:4D", "Gigabyte"),
        ("54:E1:AD", "LG"), ("A8:23:FE", "LG"), ("10:2B:41", "Samsung TV"), ("8C:EA:48", "Samsung"), ("00:09:DF", "Vestel (TV)"),
    };

    /// <summary>Fabricante pelo MAC ("aa-bb-cc-..." ou "AA:BB:CC:..."). MAC aleatório (celular moderno) aparece como "celular ou notebook".</summary>
    public static string VendorOf(string mac)
    {
        string m = mac.Replace('-', ':').ToUpperInvariant();
        if (m.Length < 8) return "";
        foreach (var (prefix, vendor) in Ouis) if (m.StartsWith(prefix, StringComparison.Ordinal)) return vendor;
        // segundo dígito 2, 6, A ou E = endereço "privado" (Android e iPhone trocam o MAC em cada Wi-Fi)
        return "26AE".Contains(m[1]) ? "Celular ou notebook (MAC privado)" : "";
    }

    public static string KindOf(string vendor, string name)
    {
        string t = (vendor + " " + name).ToLowerInvariant();
        if (Regex.IsMatch(t, "apple|samsung|xiaomi|motorola|celular|iphone|android|galaxy")) return "Celular ou tablet";
        if (Regex.IsMatch(t, "playstation|xbox|nintendo")) return "Videogame";
        if (Regex.IsMatch(t, "tv|lg|vestel|roku|chromecast|google")) return "TV ou streaming";
        if (Regex.IsMatch(t, "alexa|espressif|tuya|casa inteligente|raspberry")) return "Casa inteligente";
        if (Regex.IsMatch(t, "tp-link|tenda|d-link|ubiquiti|cisco|ralink|mercusys|intelbras")) return "Rede (repetidor ou AP)";
        if (Regex.IsMatch(t, "synology|qnap|print|epson|hp|brother|canon")) return "Impressora ou servidor";
        if (Regex.IsMatch(t, "intel|realtek|asus|msi|gigabyte|asrock|desktop|notebook|pc")) return "Computador";
        return "Aparelho";
    }

    /// <summary>Linhas "  192.168.0.10     aa-bb-cc-dd-ee-ff     dinâmico" do "arp -a".</summary>
    public static IReadOnlyList<(string Ip, string Mac)> ParseArp(string text) =>
        Regex.Matches(text, @"(\d{1,3}(?:\.\d{1,3}){3})\s+([0-9a-fA-F]{2}(?:-[0-9a-fA-F]{2}){5})")
            .Select(m => (m.Groups[1].Value, m.Groups[2].Value.ToUpperInvariant()))
            .Where(x => !x.Item2.StartsWith("FF-FF", StringComparison.Ordinal) && !x.Item2.StartsWith("01-00-5E", StringComparison.Ordinal))
            .Distinct().ToList();

    /// <summary>Endereços da mesma faixa /24 (até 254), sem o próprio.</summary>
    public static IReadOnlyList<string> Range(string localIp)
    {
        if (!IPAddress.TryParse(localIp, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) return Array.Empty<string>();
        var b = ip.GetAddressBytes();
        return Enumerable.Range(1, 254).Where(i => i != b[3]).Select(i => $"{b[0]}.{b[1]}.{b[2]}.{i}").ToList();
    }

    public static LinkQuality Quality(IReadOnlyList<double?> pings, bool wifi)
    {
        var ok = pings.Where(p => p is not null).Select(p => p!.Value).ToList();
        double loss = pings.Count == 0 ? 100 : 100.0 * (pings.Count - ok.Count) / pings.Count;
        double jitter = ok.Count < 2 ? 0 : ok.Zip(ok.Skip(1), (a, b) => Math.Abs(a - b)).Average();
        return new LinkQuality(ok.Count == 0 ? null : ok.Average(), jitter, loss, wifi);
    }

    // ---------------------------------------------------------------- placa de rede

    /// <summary>
    /// Ajustes recomendados (pelas palavras-chave padrão do Windows, que não mudam com o idioma): economia de energia desligada,
    /// menos atraso (sem moderação de interrupção nem LSO), buffers no máximo e, no Wi-Fi, menos troca de antena e preferência por 5 GHz.
    /// </summary>
    public static IReadOnlyList<NicChange> Recommend(IReadOnlyList<NicProperty> props)
    {
        var rules = new List<(string Keyword, Func<NicProperty, string?> Target, string Title, string Why)>
        {
            ("*EEE", _ => "0", "Ethernet com economia de energia", "economia de energia causa travadinhas"),
            ("EEELinkAdvertisement", _ => "0", "Ethernet com economia de energia", "economia de energia causa travadinhas"),
            ("EnableGreenEthernet", _ => "0", "Green Ethernet", "economia de energia causa travadinhas"),
            ("GreenEthernet", _ => "0", "Green Ethernet", "economia de energia causa travadinhas"),
            ("PowerSavingMode", _ => "0", "Modo de economia de energia", "a placa dorme e demora a acordar"),
            ("AdvancedEEE", _ => "0", "Economia de energia avançada", "a placa dorme e demora a acordar"),
            ("*InterruptModeration", _ => "0", "Moderação de interrupção", "menos atraso nos jogos (usa um pouco mais de processador)"),
            ("*FlowControl", _ => "0", "Controle de fluxo", "evita pausas no envio"),
            ("*LsoV2IPv4", _ => "0", "Large Send Offload (IPv4)", "menos atraso em pacotes pequenos de jogo"),
            ("*LsoV2IPv6", _ => "0", "Large Send Offload (IPv6)", "menos atraso em pacotes pequenos de jogo"),
            ("*ReceiveBuffers", Max, "Buffers de recebimento", "menos perda em download alto"),
            ("*TransmitBuffers", Max, "Buffers de envio", "menos perda em upload alto"),
            ("RoamAggressiveness", Min, "Agressividade de roaming (Wi-Fi)", "o Wi-Fi para de procurar outra antena no meio do jogo"),
            ("RoamingPreferredBandType", p => p.ValidValues.Contains("2") ? "2" : null, "Banda preferida (Wi-Fi)", "prefere 5 GHz: mais rápido e menos interferência"),
            ("MIMOPowerSaveMode", p => p.ValidValues.Contains("3") ? "3" : null, "Economia MIMO (Wi-Fi)", "antenas sempre ativas"),
            ("ThroughputBoosterEnabled", _ => "1", "Throughput Booster (Wi-Fi)", "mais velocidade no Wi-Fi"),
            ("uAPSDSupport", _ => "0", "U-APSD (Wi-Fi)", "economia de energia do Wi-Fi desligada"),
        };
        var changes = new List<NicChange>();
        foreach (var p in props)
        {
            var rule = rules.FirstOrDefault(r => r.Keyword.Equals(p.Keyword, StringComparison.OrdinalIgnoreCase));
            if (rule.Keyword is null) continue;
            string? to = rule.Target(p);
            if (to is null || to == p.Value) continue;
            if (p.ValidValues.Count > 0 && !p.ValidValues.Contains(to)) continue;   // só valores que o driver aceita
            changes.Add(new NicChange(p.Adapter, rule.Title, p.Keyword, p.DisplayValue, to, rule.Why));
        }
        return changes;
    }

    private static string? Max(NicProperty p)
    {
        var numbers = p.ValidValues.Select(v => int.TryParse(v, out int n) ? n : (int?)null).OfType<int>().ToList();
        return numbers.Count == 0 ? null : numbers.Max().ToString(CultureInfo.InvariantCulture);
    }

    private static string? Min(NicProperty p)
    {
        var numbers = p.ValidValues.Select(v => int.TryParse(v, out int n) ? n : (int?)null).OfType<int>().ToList();
        return numbers.Count == 0 ? null : numbers.Min().ToString(CultureInfo.InvariantCulture);
    }

    public static IReadOnlyList<NicProperty> ParseProperties(string json)
    {
        string text = json.Trim();
        if (text.Length == 0) return Array.Empty<NicProperty>();
        using var doc = JsonDocument.Parse(text);
        var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new List<JsonElement> { doc.RootElement };
        static string Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Array => v.EnumerateArray().Select(x => x.ToString()).FirstOrDefault() ?? "",
            JsonValueKind.Null => "",
            _ => v.ToString(),
        } : "";
        static IReadOnlyList<string> List(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(x => x.ToString()).ToList() : Array.Empty<string>();
        return items.Select(e => new NicProperty(Str(e, "Name"), Str(e, "DisplayName"), Str(e, "DisplayValue"), Str(e, "RegistryKeyword"),
            Str(e, "RegistryValue"), List(e, "ValidRegistryValues"))).Where(p => p.Keyword.Length > 0).ToList();
    }

    private static readonly Regex SafeName = new(@"^[\p{L}\p{N} ()#._\-]{1,80}$");

    // ---------------------------------------------------------------- Windows

    [SupportedOSPlatform("windows")]
    public static (string? Ip, bool Wifi, string Description) LocalAddress()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            var props = nic.GetIPProperties();
            if (!props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any))) continue;
            var ip = props.UnicastAddresses.FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
            return (ip, nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211, nic.Description);
        }
        return (null, false, "");
    }

    /// <summary>Varre a faixa da rede: ping em todos (64 por vez), depois lê a tabela ARP e o nome de cada aparelho.</summary>
    [SupportedOSPlatform("windows")]
    public static async Task<IReadOnlyList<LanDevice>> ScanAsync(string localIp, string? gateway, IProgress<int>? progress, CancellationToken token)
    {
        var range = Range(localIp);
        var pings = new System.Collections.Concurrent.ConcurrentDictionary<string, double>();
        int done = 0;
        using var gate = new SemaphoreSlim(64);
        await Task.WhenAll(range.Select(async address =>
        {
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(address, 600).ConfigureAwait(false);
                if (reply.Status == IPStatus.Success) pings[address] = reply.RoundtripTime;
            }
            catch (PingException) { }
            finally
            {
                gate.Release();
                progress?.Report(Interlocked.Increment(ref done) * 100 / Math.Max(1, range.Count));
            }
        })).ConfigureAwait(false);

        var arp = ParseArp(Shell.RunOem("arp.exe", "-a").Output);
        var macs = arp.ToDictionary(a => a.Ip, a => a.Mac);
        var ips = pings.Keys.Concat(arp.Select(a => a.Ip)).Where(ip => range.Contains(ip) || ip == localIp).Append(localIp).Distinct().ToList();
        var devices = await Task.WhenAll(ips.Select(async ip =>
        {
            string name = "";
            try
            {
                var lookup = System.Net.Dns.GetHostEntryAsync(ip);
                if (await Task.WhenAny(lookup, Task.Delay(1200, token)).ConfigureAwait(false) == lookup && lookup.IsCompletedSuccessfully)
                    name = lookup.Result.HostName == ip ? "" : lookup.Result.HostName;
            }
            catch (SocketException) { }
            string mac = macs.TryGetValue(ip, out var m) ? m : "";
            return new LanDevice(ip, mac, VendorOf(mac), name, pings.TryGetValue(ip, out double ms) ? ms : null, ip == gateway, ip == localIp);
        })).ConfigureAwait(false);
        return devices.OrderByDescending(d => d.IsRouter).ThenByDescending(d => d.IsThisPc)
            .ThenBy(d => int.TryParse(d.Ip.Split('.').Last(), out int last) ? last : 999).ToList();
    }

    /// <summary>20 pings até o roteador (um a cada 100 ms).</summary>
    [SupportedOSPlatform("windows")]
    public static async Task<LinkQuality> MeasureAsync(string gateway, bool wifi, CancellationToken token)
    {
        var results = new List<double?>();
        using var ping = new Ping();
        for (int i = 0; i < 20; i++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync(gateway, 1000).ConfigureAwait(false);
                results.Add(reply.Status == IPStatus.Success ? reply.RoundtripTime : null);
            }
            catch (PingException) { results.Add(null); }
            await Task.Delay(100, token).ConfigureAwait(false);
        }
        return Quality(results, wifi);
    }

    /// <summary>Configurações avançadas das placas de rede ligadas.</summary>
    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<NicProperty> ReadProperties()
    {
        const string script = "Get-NetAdapter -Physical | Where-Object Status -eq 'Up' | Get-NetAdapterAdvancedProperty -ErrorAction SilentlyContinue | " +
                              "Select-Object Name,DisplayName,DisplayValue,RegistryKeyword,RegistryValue,ValidRegistryValues | ConvertTo-Json -Compress -Depth 3";
        var result = Shell.PowerShellUtf8(script);
        if (result.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? "Não foi possível ler a placa de rede." : result.Error.Trim());
        return ParseProperties(result.Output);
    }

    /// <summary>Aplica as mudanças (a placa reinicia por 2 a 5 segundos). Nome da placa e valores são conferidos antes.</summary>
    [SupportedOSPlatform("windows")]
    public static (int Ok, int Failed) Apply(IReadOnlyList<NicChange> changes)
    {
        int ok = 0, failed = 0;
        foreach (var c in changes)
        {
            if (!SafeName.IsMatch(c.Adapter) || !Regex.IsMatch(c.Keyword, @"^\*?[A-Za-z0-9]{1,40}$") || !Regex.IsMatch(c.To, @"^\d{1,6}$")) { failed++; continue; }
            string script = $"Set-NetAdapterAdvancedProperty -Name '{c.Adapter.Replace("'", "''")}' -RegistryKeyword '{c.Keyword}' -RegistryValue {c.To} -ErrorAction Stop";
            if (Shell.PowerShellUtf8(script).ExitCode == 0) ok++; else failed++;
        }
        return (ok, failed);
    }

    /// <summary>Volta todas as configurações avançadas das placas ligadas ao padrão do driver.</summary>
    [SupportedOSPlatform("windows")]
    public static bool ResetAll() =>
        Shell.PowerShellUtf8("Get-NetAdapter -Physical | Where-Object Status -eq 'Up' | ForEach-Object { Reset-NetAdapterAdvancedProperty -Name $_.Name -DisplayName '*' -ErrorAction SilentlyContinue }").ExitCode == 0;
}
