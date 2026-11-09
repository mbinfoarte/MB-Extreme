using System.Globalization;
using System.Management;

namespace MBExtreme.Core.Drivers;

/// <summary>Aparelho com problema no Gerenciador de Dispositivos.</summary>
public sealed record ProblemDevice(string Name, string Class, int Code, string DeviceId, IReadOnlyList<string> HardwareIds)
{
    public string Explanation => DeviceDrivers.Explain(Code);

    /// <summary>Sem driver instalado (código 28): o Windows não achou nenhum driver para o aparelho.</summary>
    public bool Missing => Code == 28;
}

/// <summary>Um driver instalado.</summary>
public sealed record InstalledDriver(string Device, string Class, string Manufacturer, string Version, DateTime? Date, bool Signed, string Inf)
{
    /// <summary>Driver genérico que já vem com o Windows (inf da própria Microsoft).</summary>
    public bool IsWindowsGeneric => DeviceDrivers.IsGeneric(Manufacturer, Inf);
}

/// <summary>Placa-mãe e BIOS, com a página de suporte do fabricante.</summary>
public sealed record Motherboard(string Manufacturer, string Model, string Bios, DateTime? BiosDate)
{
    public string? SupportUrl => DeviceDrivers.SupportUrl(Manufacturer, Model);
    public string Brand => DeviceDrivers.Brand(Manufacturer);
}

/// <summary>Leitura dos drivers e dos aparelhos com defeito (só lê) e as explicações em português dos códigos do Windows.</summary>
public static class DeviceDrivers
{
    /// <summary>Códigos do Gerenciador de Dispositivos (documentação da Microsoft "Device Manager error codes").</summary>
    public static string Explain(int code) => code switch
    {
        1 => "Não está configurado direito. Atualize o driver.",
        3 => "O driver está com defeito ou falta memória. Reinstale o driver.",
        10 => "Não consegue iniciar. Atualize ou reinstale o driver.",
        12 => "Não há recursos livres para ele (conflito). Tire outro aparelho ou atualize a BIOS.",
        14 => "Precisa reiniciar o PC para funcionar.",
        18 => "Reinstale o driver.",
        19 => "Configuração do registro corrompida. Desinstale e reinstale o aparelho.",
        21 => "O Windows está removendo o aparelho. Reinicie.",
        22 => "Foi desativado no Gerenciador de Dispositivos. Se não foi de propósito, clique com o botão direito e escolha Habilitar.",
        24 => "Não está presente ou não funciona direito. Confira o cabo e a conexão.",
        28 => "Sem driver instalado. Procure no Windows Update ou no site do fabricante.",
        29 => "Desativado pela BIOS (firmware). Ligue nas configurações da BIOS.",
        31 => "O Windows não conseguiu carregar o driver. Atualize o driver.",
        32 => "O serviço do driver está desativado.",
        33 => "Não deu para saber os recursos que ele precisa. Pode ser defeito no aparelho.",
        34 => "Precisa ser configurado manualmente.",
        35 => "A BIOS não tem as informações dele. Atualize a BIOS.",
        37 => "O driver retornou erro ao iniciar. Reinstale o driver.",
        38 => "Uma versão antiga do driver ainda está na memória. Reinicie o PC.",
        39 => "Driver corrompido ou ausente. Reinstale.",
        40 => "Informação do serviço no registro com problema. Reinstale o driver.",
        41 => "O driver carregou, mas o aparelho não foi encontrado. Confira a conexão.",
        42 => "Aparelho duplicado no sistema. Reinicie.",
        43 => "O aparelho avisou um problema e foi parado. Comum em USB e placa de vídeo: troque de porta, atualize o driver.",
        44 => "Um programa desligou o aparelho. Reinicie.",
        45 => "Não está conectado agora.",
        47 => "Preparado para remoção segura. Desconecte e conecte de novo.",
        48 => "Driver bloqueado por problema conhecido. Procure uma versão nova com o fabricante.",
        52 => "Assinatura digital do driver não confere. Instale o driver oficial.",
        _ => $"O Windows informou o código {code}.",
    };

    public static bool IsGeneric(string manufacturer, string inf) =>
        manufacturer.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) && !inf.StartsWith("oem", StringComparison.OrdinalIgnoreCase)
        || manufacturer.StartsWith("(Standard", StringComparison.OrdinalIgnoreCase) || manufacturer.StartsWith("(Padrão", StringComparison.OrdinalIgnoreCase);

    public static string Brand(string manufacturer)
    {
        string m = manufacturer.ToUpperInvariant();
        if (m.Contains("ASUS")) return "ASUS";
        if (m.Contains("MICRO-STAR") || m.Contains("MSI")) return "MSI";
        if (m.Contains("GIGABYTE")) return "Gigabyte";
        if (m.Contains("ASROCK")) return "ASRock";
        if (m.Contains("DELL")) return "Dell";
        if (m.Contains("HEWLETT") || m.StartsWith("HP", StringComparison.Ordinal)) return "HP";
        if (m.Contains("LENOVO")) return "Lenovo";
        if (m.Contains("ACER")) return "Acer";
        if (m.Contains("SAMSUNG")) return "Samsung";
        if (m.Contains("POSITIVO")) return "Positivo";
        if (m.Contains("BIOSTAR")) return "Biostar";
        return manufacturer.Trim();
    }

    /// <summary>Página de suporte (drivers, BIOS) do fabricante, buscando pelo modelo quando o site permite.</summary>
    public static string? SupportUrl(string manufacturer, string model)
    {
        string q = Uri.EscapeDataString(model.Trim());
        return Brand(manufacturer) switch
        {
            "ASUS" => $"https://www.asus.com/br/searchresult?searchType=support&searchKey={q}",
            "MSI" => $"https://br.msi.com/search/{q}",
            "Gigabyte" => $"https://www.gigabyte.com/br/Search?kw={q}",
            "ASRock" => "https://www.asrock.com/support/index.asp",
            "Dell" => "https://www.dell.com/support/home/pt-br",
            "HP" => "https://support.hp.com/br-pt/drivers",
            "Lenovo" => "https://pcsupport.lenovo.com/br/pt",
            "Acer" => "https://www.acer.com/br-pt/support",
            "Samsung" => "https://www.samsung.com/br/support/",
            "Positivo" => "https://www.meupositivo.com.br/drivers",
            "Biostar" => "https://www.biostar.com.tw/app/en/support/download.php",
            _ => null,
        };
    }

    public static DateTime? ParseWmiDate(string? value) =>
        value is { Length: >= 8 } && DateTime.TryParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static IEnumerable<ManagementObject> Query(string wql)
    {
        using var searcher = new ManagementObjectSearcher(wql);
        foreach (ManagementObject o in searcher.Get()) yield return o;
    }

    public static IReadOnlyList<ProblemDevice> Problems() =>
        Query("SELECT Name, PNPClass, ConfigManagerErrorCode, DeviceID, HardwareID FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0")
            .Select(o => new ProblemDevice(o["Name"] as string ?? "Aparelho desconhecido", o["PNPClass"] as string ?? "",
                Convert.ToInt32(o["ConfigManagerErrorCode"] ?? 0, CultureInfo.InvariantCulture), o["DeviceID"] as string ?? "",
                (o["HardwareID"] as string[])?.Where(h => !string.IsNullOrWhiteSpace(h)).ToList() ?? new List<string>()))
            .Where(p => p.Code is not (45 or 0))   // 45 = não conectado agora: não é problema
            .ToList();

    /// <summary>Endereço da busca oficial do Catálogo do Windows Update por um ID de hardware (reserva, quando o Windows Update não traz o driver).</summary>
    public static string CatalogUrl(string hardwareId) =>
        "https://www.catalog.update.microsoft.com/Search.aspx?q=" + Uri.EscapeDataString(hardwareId);

    public static IReadOnlyList<InstalledDriver> All() =>
        Query("SELECT DeviceName, DeviceClass, Manufacturer, DriverVersion, DriverDate, IsSigned, InfName FROM Win32_PnPSignedDriver")
            .Where(o => o["DeviceName"] is string)
            .Select(o => new InstalledDriver(o["DeviceName"] as string ?? "", o["DeviceClass"] as string ?? "", o["Manufacturer"] as string ?? "",
                o["DriverVersion"] as string ?? "", ParseWmiDate(o["DriverDate"] as string), o["IsSigned"] as bool? ?? false, o["InfName"] as string ?? ""))
            .OrderBy(d => d.Device, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public static Motherboard? Board()
    {
        var board = Query("SELECT Manufacturer, Product FROM Win32_BaseBoard").FirstOrDefault();
        if (board is null) return null;
        var bios = Query("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS").FirstOrDefault();
        return new Motherboard(board["Manufacturer"] as string ?? "", board["Product"] as string ?? "",
            bios?["SMBIOSBIOSVersion"] as string ?? "", ParseWmiDate(bios?["ReleaseDate"] as string));
    }
}
