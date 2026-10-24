using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using MBExtreme.Core.Windows;

namespace MBExtreme.Core.Storage;

public sealed record ManagedVolume(string Label, string FileSystem, long Size, long Free, string Health);

public sealed record ManagedPartition(
    int DiskNumber, int Number, char? Letter, long Offset, long Size, string Type,
    bool IsSystem, bool IsBoot, bool IsHidden, ManagedVolume? Volume, bool HasPageFile = false, bool IsActive = false)
{
    public string Name => Letter is { } l ? $"{l}:" : "(sem letra)";

    /// <summary>Como o Gerenciamento de Disco do Windows escreve: "Nome (C:)"; sem nome, "Disco Local (C:)".</summary>
    public string Title => Letter is null
        ? TypeText
        : $"{(string.IsNullOrWhiteSpace(Volume?.Label) ? "Disco Local" : Volume!.Label)} ({Letter}:)";

    public bool IsEfi => Type.Equals("System", StringComparison.OrdinalIgnoreCase);

    public bool IsRecovery => Type.Equals("Recovery", StringComparison.OrdinalIgnoreCase);

    public bool IsReserved => Type.Equals("Reserved", StringComparison.OrdinalIgnoreCase);

    public string TypeText => Type switch
    {
        "System" => "Partição do Sistema EFI",
        "Reserved" => "Partição Reservada (MSR)",
        "Recovery" => "Partição de Recuperação",
        "Logical" => "Partição Lógica",
        "Extended" => "Partição Estendida",
        "Basic" or "IFS" or "FAT32" or "Primary" => "Partição Primária",
        _ => Type.Length == 0 ? "Partição" : Type,
    };

    /// <summary>Texto da coluna "Status" do Gerenciamento de Disco, por exemplo "Íntegro (Inicialização, Arquivo de Paginação, Despejo de Memória, Partição Primária)".</summary>
    public string StatusText
    {
        get
        {
            var parts = new List<string>();
            if (IsBoot) parts.Add("Inicialização");
            if (IsActive && !IsEfi) parts.Add("Ativa");
            if (HasPageFile) parts.Add("Arquivo de Paginação");
            if (IsBoot) parts.Add("Despejo de Memória");
            parts.Add(TypeText);
            return DiskText.Health(Volume?.Health) + " (" + string.Join(", ", parts) + ")";
        }
    }

    /// <summary>"42% livre", como na lista de volumes do Windows.</summary>
    public string FreePercentText => Volume is { Size: > 0 } v ? $"{100.0 * v.Free / v.Size:0}% livre" : "";
}

public sealed record ManagedDisk(
    int Number, string Name, long Size, string Style, string Bus, string Health,
    bool IsSystem, bool IsBoot, bool IsOffline, bool IsReadOnly, IReadOnlyList<ManagedPartition> Partitions)
{
    public bool IsRaw => Style.Equals("RAW", StringComparison.OrdinalIgnoreCase);

    public string Title => $"Disco {Number} · {Name}";

    /// <summary>"Básico" ou "Não inicializado" (o Windows também diz "Dinâmico", que este programa não gerencia).</summary>
    public string KindText => IsRaw ? "Não inicializado" : "Básico";

    public string StateText => IsOffline ? "Offline" : IsReadOnly ? "Somente leitura" : "Online";

    /// <summary>A linha que fica sob o nome do disco: "Básico · GPT · 931,51 GB · Online".</summary>
    public string SummaryText => string.Join(" · ", new[] { KindText, IsRaw ? "" : Style.ToUpperInvariant(), DiskText.Size(Size), StateText, Bus }.Where(t => t.Length > 0));
}

/// <summary>Textos no mesmo formato do Gerenciamento de Disco do Windows (tamanhos binários com duas casas, saúde em português).</summary>
public static class DiskText
{
    public static string Size(long bytes)
    {
        const double kb = 1024, mb = kb * 1024, gb = mb * 1024, tb = gb * 1024;
        var c = CultureInfo.GetCultureInfo("pt-BR");
        return bytes switch
        {
            >= (long)tb => (bytes / tb).ToString("0.00", c) + " TB",
            >= (long)gb => (bytes / gb).ToString("0.00", c) + " GB",
            >= (long)mb => (bytes / mb).ToString("0", c) + " MB",
            >= (long)kb => (bytes / kb).ToString("0", c) + " KB",
            _ => bytes + " B",
        };
    }

    public static string Health(string? health) => (health ?? "").Trim().ToLowerInvariant() switch
    {
        "healthy" or "" => "Íntegro",
        "warning" => "Aviso",
        "unhealthy" => "Não íntegro",
        _ => health!,
    };
}

/// <summary>Um trecho do disco: uma partição ou um espaço sem partição.</summary>
public sealed record DiskSegment(ManagedPartition? Partition, long Offset, long Size)
{
    public bool IsFree => Partition is null;
}

public enum FileSystemKind { Ntfs, ExFat, Fat32 }

/// <summary>Um comando de PowerShell do gerenciador de discos, com texto para mostrar antes de executar.</summary>
public sealed record DiskCommand(string Title, string Script, string Summary)
{
    public override string ToString() => Script;
}

/// <summary>Lê discos e partições e monta os trechos (partições e espaço livre) na ordem em que aparecem no disco.</summary>
public static class DiskInventory
{
    private const long MinFreeSegment = 8L * 1024 * 1024;

    public const string Script =
        "$vols=@{}; Get-Volume | Where-Object { $_.DriveLetter } | ForEach-Object { $vols[[string]$_.DriveLetter]=$_ }; " +
        "$pf=@(Get-CimInstance Win32_PageFileUsage -ErrorAction SilentlyContinue | ForEach-Object { $_.Name.Substring(0,1).ToUpper() }); " +
        "$out=@(Get-Disk | ForEach-Object { $d=$_; [pscustomobject]@{ Number=$d.Number; Name=[string]$d.FriendlyName; Size=[int64]$d.Size; Style=[string]$d.PartitionStyle; Bus=[string]$d.BusType; " +
        "Health=[string]$d.HealthStatus; IsSystem=[bool]$d.IsSystem; IsBoot=[bool]$d.IsBoot; IsOffline=[bool]$d.IsOffline; IsReadOnly=[bool]$d.IsReadOnly; " +
        "Partitions=@(Get-Partition -DiskNumber $d.Number -ErrorAction SilentlyContinue | ForEach-Object { $p=$_; $l=''; if($p.DriveLetter){ $l=[string]$p.DriveLetter }; $v=$null; " +
        "if($l -and $vols.ContainsKey($l)){ $x=$vols[$l]; $v=[pscustomobject]@{ Label=[string]$x.FileSystemLabel; FileSystem=[string]$x.FileSystem; Size=[int64]$x.Size; Free=[int64]$x.SizeRemaining; Health=[string]$x.HealthStatus } }; " +
        "[pscustomobject]@{ Number=$p.PartitionNumber; Letter=$l; Offset=[int64]$p.Offset; Size=[int64]$p.Size; Type=[string]$p.Type; IsSystem=[bool]$p.IsSystem; IsBoot=[bool]$p.IsBoot; IsHidden=[bool]$p.IsHidden; IsActive=[bool]$p.IsActive; HasPageFile=($pf -contains $l.ToUpper()); Volume=$v } }) } }); " +
        "ConvertTo-Json -InputObject $out -Depth 6 -Compress";

    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<ManagedDisk> Load()
    {
        var result = Shell.PowerShellUtf8(Script);
        if (result.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? "Não foi possível ler os discos." : result.Error.Trim());
        return Parse(result.Output);
    }

    public static IReadOnlyList<ManagedDisk> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<ManagedDisk>();
        using var doc = JsonDocument.Parse(json);
        var disks = new List<ManagedDisk>();
        foreach (var d in Items(doc.RootElement))
        {
            int number = Int(d, "Number");
            var partitions = new List<ManagedPartition>();
            if (d.TryGetProperty("Partitions", out var parts))
            {
                foreach (var p in Items(parts))
                {
                    string letterText = Str(p, "Letter");
                    ManagedVolume? volume = null;
                    if (p.TryGetProperty("Volume", out var v) && v.ValueKind == JsonValueKind.Object)
                        volume = new ManagedVolume(Str(v, "Label"), Str(v, "FileSystem"), Long(v, "Size"), Long(v, "Free"), Str(v, "Health"));
                    partitions.Add(new ManagedPartition(number, Int(p, "Number"), letterText.Length > 0 ? char.ToUpperInvariant(letterText[0]) : null,
                        Long(p, "Offset"), Long(p, "Size"), Str(p, "Type"), Bool(p, "IsSystem"), Bool(p, "IsBoot"), Bool(p, "IsHidden"), volume, Bool(p, "HasPageFile"), Bool(p, "IsActive")));
                }
            }

            disks.Add(new ManagedDisk(number, Str(d, "Name"), Long(d, "Size"), Str(d, "Style"), Str(d, "Bus"), Str(d, "Health"),
                Bool(d, "IsSystem"), Bool(d, "IsBoot"), Bool(d, "IsOffline"), Bool(d, "IsReadOnly"), partitions.OrderBy(p => p.Offset).ToList()));
        }

        return disks.OrderBy(d => d.Number).ToList();
    }

    /// <summary>Partições e espaços sem partição, em ordem de posição no disco.</summary>
    public static IReadOnlyList<DiskSegment> Segments(ManagedDisk disk)
    {
        var segments = new List<DiskSegment>();
        long cursor = 0;
        foreach (var p in disk.Partitions.OrderBy(p => p.Offset))
        {
            if (p.Offset - cursor >= MinFreeSegment) segments.Add(new DiskSegment(null, cursor, p.Offset - cursor));
            segments.Add(new DiskSegment(p, p.Offset, p.Size));
            cursor = Math.Max(cursor, p.Offset + p.Size);
        }

        if (disk.Size - cursor >= MinFreeSegment) segments.Add(new DiskSegment(null, cursor, disk.Size - cursor));
        return segments;
    }

    private static IEnumerable<JsonElement> Items(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array) return element.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object);
        return element.ValueKind == JsonValueKind.Object ? new[] { element } : Array.Empty<JsonElement>();
    }

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static long Long(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) ? n : 0;

    private static int Int(JsonElement e, string name) => (int)Math.Clamp(Long(e, name), int.MinValue, int.MaxValue);

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}

/// <summary>Regras que impedem as operações perigosas: nada de formatar, apagar ou trocar a letra do disco do Windows, de partições do sistema nem do disco onde o MB Extreme está.</summary>
public static class DiskSafety
{
    private static readonly HashSet<string> ProtectedTypes = new(StringComparer.OrdinalIgnoreCase) { "System", "Reserved", "Recovery", "Extended", "Unknown" };

    public static bool IsProtectedPartition(ManagedPartition p) => p.IsSystem || p.IsBoot || ProtectedTypes.Contains(p.Type) || (p.IsHidden && p.Letter is null);

    /// <summary>Formatar, apagar ou trocar a letra. Devolve o motivo do bloqueio, ou null se pode.</summary>
    public static string? CanChangeData(ManagedDisk disk, ManagedPartition partition, string systemDrive, string appDrive)
    {
        if (disk.IsOffline) return "O disco está offline.";
        if (disk.IsReadOnly) return "O disco está somente leitura.";
        if (disk.IsSystem || disk.IsBoot) return "Este é o disco do Windows. Mexer nele poderia impedir o computador de ligar; use o Gerenciamento de Disco do Windows se tiver certeza.";
        if (IsProtectedPartition(partition)) return "Esta partição é do sistema (inicialização ou recuperação) e não pode ser alterada por aqui.";
        if (partition.Letter is { } letter)
        {
            if (SameLetter(letter, systemDrive)) return "Esta é a unidade onde o Windows está instalado.";
            if (SameLetter(letter, appDrive)) return "O MB Extreme está rodando desta unidade. Abra-o de outro disco para alterá-la.";
        }

        return null;
    }

    /// <summary>Reduzir ou estender: a partição do Windows pode, a de sistema/recuperação não.</summary>
    public static string? CanResize(ManagedDisk disk, ManagedPartition partition)
    {
        if (disk.IsOffline) return "O disco está offline.";
        if (disk.IsReadOnly) return "O disco está somente leitura.";
        if (partition.Letter is null) return "A partição precisa ter uma letra para ser redimensionada.";
        if (ProtectedTypes.Contains(partition.Type) || (partition.IsSystem && !partition.IsBoot)) return "Esta partição é do sistema (inicialização ou recuperação) e não pode ser redimensionada por aqui.";
        string fs = partition.Volume?.FileSystem ?? "";
        if (fs.Length > 0 && !fs.Equals("NTFS", StringComparison.OrdinalIgnoreCase) && !fs.Equals("ReFS", StringComparison.OrdinalIgnoreCase))
            return $"O Windows só redimensiona partições NTFS (esta é {fs}). Faça uma cópia, formate em NTFS e restaure.";
        return null;
    }

    public static string? CanInitialize(ManagedDisk disk)
    {
        if (!disk.IsRaw) return "O disco já foi inicializado.";
        if (disk.IsSystem || disk.IsBoot) return "Este é o disco do Windows.";
        if (disk.IsOffline) return "O disco está offline. Coloque-o online no Gerenciamento de Disco do Windows.";
        if (disk.IsReadOnly) return "O disco está somente leitura.";
        return null;
    }

    public static string? CanCreate(ManagedDisk disk, DiskSegment segment)
    {
        if (!segment.IsFree) return "Este trecho já tem uma partição.";
        if (disk.IsRaw) return "Inicialize o disco antes de criar partições.";
        if (disk.IsOffline) return "O disco está offline.";
        if (disk.IsReadOnly) return "O disco está somente leitura.";
        if (segment.Size < 64L * 1024 * 1024) return "Espaço livre pequeno demais (menos de 64 MB).";
        return null;
    }

    private static bool SameLetter(char letter, string drive) => drive.Length > 0 && char.ToUpperInvariant(drive[0]) == char.ToUpperInvariant(letter);
}

/// <summary>Monta os comandos do PowerShell para cada operação. Tudo que vem de campos digitados passa por validação e nunca entra solto no comando.</summary>
public static class DiskOps
{
    private static readonly Regex LabelPattern = new(@"^[\p{L}\p{N} _.\-]*$", RegexOptions.Compiled);
    public const long Mb = 1024L * 1024;
    public const long Gb = 1024L * Mb;

    public static string FileSystemName(FileSystemKind fs) => fs switch { FileSystemKind.Ntfs => "NTFS", FileSystemKind.ExFat => "exFAT", _ => "FAT32" };

    public static int MaxLabelLength(FileSystemKind fs) => fs switch { FileSystemKind.Ntfs => 32, FileSystemKind.ExFat => 15, _ => 11 };

    /// <summary>Letras livres para escolher (D a Z, sem as que estão em uso).</summary>
    public static IReadOnlyList<char> FreeLetters(IEnumerable<char> used)
    {
        var taken = used.Select(char.ToUpperInvariant).ToHashSet();
        return Enumerable.Range('D', 'Z' - 'D' + 1).Select(i => (char)i).Where(c => !taken.Contains(c)).ToList();
    }

    public static string? ValidateLabel(string label, FileSystemKind fs)
    {
        if (label.Length > MaxLabelLength(fs)) return $"O nome pode ter até {MaxLabelLength(fs)} caracteres em {FileSystemName(fs)}.";
        if (!LabelPattern.IsMatch(label)) return "Use só letras, números, espaço, hífen, ponto e sublinhado no nome.";
        return null;
    }

    public static string? ValidateFormat(FileSystemKind fs, long sizeBytes)
    {
        if (fs == FileSystemKind.Fat32 && sizeBytes > 32 * Gb) return "O Windows só formata em FAT32 partições de até 32 GB. Use NTFS ou exFAT.";
        return null;
    }

    private static char Letter(char letter)
    {
        char l = char.ToUpperInvariant(letter);
        if (l is < 'A' or > 'Z') throw new ArgumentException("Letra de unidade inválida.", nameof(letter));
        return l;
    }

    private static string Label(string label, FileSystemKind fs)
    {
        string? problem = ValidateLabel(label, fs);
        if (problem is not null) throw new ArgumentException(problem, nameof(label));
        return label;
    }

    private static string Size(long bytes)
    {
        if (bytes <= 0) throw new ArgumentException("Tamanho inválido.", nameof(bytes));
        return bytes.ToString(CultureInfo.InvariantCulture);
    }

    public static string FormatBytes(long bytes) => DiskText.Size(bytes);

    public static DiskCommand Format(char letter, FileSystemKind fs, string label, bool full)
    {
        char l = Letter(letter);
        string text = Label(label, fs);
        return new DiskCommand($"Formatar {l}:",
            $"Format-Volume -DriveLetter {l} -FileSystem {FileSystemName(fs)} -NewFileSystemLabel '{text}' -Full:${(full ? "true" : "false")} -Confirm:$false | Out-Null; 'Formatado.'",
            $"Apaga tudo em {l}: e cria um sistema de arquivos {FileSystemName(fs)} novo ({(full ? "formatação completa, demora" : "formatação rápida")}).");
    }

    public static DiskCommand Rename(char letter, string label)
    {
        char l = Letter(letter);
        string text = Label(label, FileSystemKind.Ntfs);
        return new DiskCommand($"Renomear {l}:", $"Set-Volume -DriveLetter {l} -NewFileSystemLabel '{text}'; 'Nome alterado.'", $"Troca o nome da unidade {l}: para \"{text}\". Não mexe nos arquivos.");
    }

    public static DiskCommand ChangeLetter(int disk, int partition, char current, char next)
    {
        char to = Letter(next);
        if (to is 'A' or 'B' or 'C') throw new ArgumentException("Escolha uma letra de D a Z.", nameof(next));
        return new DiskCommand($"Trocar a letra de {char.ToUpperInvariant(current)}: para {to}:",
            $"Set-Partition -DiskNumber {disk} -PartitionNumber {partition} -NewDriveLetter {to}; 'Letra alterada.'",
            $"A unidade {char.ToUpperInvariant(current)}: passa a ser {to}:. Programas e atalhos que apontam para a letra antiga deixam de achar os arquivos.");
    }

    public static DiskCommand Shrink(int disk, int partition, char letter, long newSizeBytes) =>
        new($"Reduzir {Letter(letter)}:", $"Resize-Partition -DiskNumber {disk} -PartitionNumber {partition} -Size {Size(newSizeBytes)}; 'Partição reduzida.'",
            $"Reduz {Letter(letter)}: para {FormatBytes(newSizeBytes)}. O espaço liberado fica sem partição, pronto para criar outra. Os arquivos são mantidos.");

    public static DiskCommand Extend(int disk, int partition, char letter, long newSizeBytes) =>
        new($"Estender {Letter(letter)}:", $"Resize-Partition -DiskNumber {disk} -PartitionNumber {partition} -Size {Size(newSizeBytes)}; 'Partição estendida.'",
            $"Aumenta {Letter(letter)}: para {FormatBytes(newSizeBytes)} usando o espaço livre logo depois dela. Os arquivos são mantidos.");

    public static DiskCommand Delete(int disk, int partition, char? letter) =>
        new($"Excluir a partição {(letter is { } l ? Letter(l) + ":" : partition.ToString())}",
            $"Remove-Partition -DiskNumber {disk} -PartitionNumber {partition} -Confirm:$false; 'Partição excluída.'",
            "Remove a partição e tudo o que há nela. O espaço fica sem partição.");

    public static DiskCommand Initialize(int disk, bool gpt) =>
        new($"Inicializar o disco {disk}", $"Initialize-Disk -Number {disk} -PartitionStyle {(gpt ? "GPT" : "MBR")}; 'Disco inicializado.'",
            $"Prepara o disco {disk} com tabela {(gpt ? "GPT (recomendado para discos novos e maiores que 2 TB)" : "MBR (PCs antigos)")}. O disco precisa estar vazio.");

    /// <summary>Cria uma partição (tamanho 0 = todo o espaço livre daquele trecho) e já formata.</summary>
    public static DiskCommand CreatePartition(int disk, long offset, long sizeBytes, char letter, FileSystemKind fs, string label)
    {
        char l = Letter(letter);
        if (l is 'A' or 'B' or 'C') throw new ArgumentException("Escolha uma letra de D a Z.", nameof(letter));
        string text = Label(label, fs);
        string? problem = ValidateFormat(fs, sizeBytes);
        if (problem is not null) throw new ArgumentException(problem, nameof(fs));
        string size = sizeBytes <= 0 ? "-UseMaximumSize" : "-Size " + Size(sizeBytes);
        return new DiskCommand($"Criar a partição {l}:",
            $"New-Partition -DiskNumber {disk} {size} -DriveLetter {l} | Format-Volume -FileSystem {FileSystemName(fs)} -NewFileSystemLabel '{text}' -Confirm:$false | Out-Null; 'Partição criada.'",
            $"Cria {l}: com {(sizeBytes <= 0 ? "todo o espaço livre" : FormatBytes(sizeBytes))} e formata em {FileSystemName(fs)}.");
    }

    /// <summary>Consulta o menor e o maior tamanho que o Windows aceita para a partição (saída: duas linhas, mínimo e máximo em bytes).</summary>
    public static string SupportedSizesScript(int disk, int partition) =>
        $"$s=Get-PartitionSupportedSize -DiskNumber {disk} -PartitionNumber {partition}; $s.SizeMin; $s.SizeMax";

    public static bool TryParseSupportedSizes(string output, out long min, out long max)
    {
        min = max = 0;
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length >= 2 && long.TryParse(lines[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out min)
                                  && long.TryParse(lines[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out max);
    }

    [SupportedOSPlatform("windows")]
    public static (bool Ok, string Output) Run(DiskCommand command) => RunScript(command.Script);

    /// <summary>Roda um script do PowerShell parando no primeiro erro. Devolve se deu certo e o texto (saída e erros).</summary>
    [SupportedOSPlatform("windows")]
    public static (bool Ok, string Output) RunScript(string script)
    {
        var result = Shell.PowerShellUtf8("$ErrorActionPreference='Stop'; " + script);
        string text = (result.Output + Environment.NewLine + result.Error).Trim();
        return (result.ExitCode == 0, text);
    }

    /// <summary>Junta comandos em sequência (o segundo só roda se o primeiro deu certo, pois o script para no erro).</summary>
    public static DiskCommand Chain(string title, params DiskCommand[] steps) =>
        new(title, string.Join("; ", steps.Select(s => s.Script)), string.Join(" ", steps.Select((s, i) => $"{i + 1}) {s.Summary}")));
}
