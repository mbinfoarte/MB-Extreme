using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MBExtreme.Core.Settings;

/// <summary>Um backup guardado na conta (sem o conteúdo).</summary>
public sealed record CloudBackupInfo(string Id, string DeviceName, long? CreatedMs, int SizeBytes, string AppVersion, bool Auto);

/// <summary>
/// Backup das configurações do MB na conta: escolhas do programa, perfis de controle, Modo Jogo, contador, ventoinhas e
/// predefinições do estúdio. Nunca leva chaves, senhas, tokens, a licença nem caminhos/estado deste PC; ajustes do Windows
/// também ficam de fora (cada PC aplica os seus pelo Desfazer).
/// </summary>
public static partial class CloudBackup
{
    public const int Version = 1;
    public const int MaxBytes = 700_000;

    [GeneratedRegex("(Key|Secret|Token|Protected|Password|License|Licence|DeviceId|Cookie|Session)", RegexOptions.IgnoreCase)]
    private static partial Regex SecretName();

    /// <summary>Coisas deste PC (posição de janela, datas de avisos, caminhos, estado de jogo aberto) que não devem ir para outro.</summary>
    private static readonly HashSet<string> MachineOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        "WidgetX", "WidgetY", "LicenseLastSeenUtcTicks", "LoginPromptSnoozeUtc", "RatingFirstUseUtc", "RatingSnoozeUtc", "RatingDone",
        "BetaNoticeHiddenBuild", "DismissedVideoId", "ProcessProfileStartup", "ProcessProfileTweaks", "ProcessProfile", "CleanupFreedTotalBytes",
        "BiosLog", "StudioPixOwnImage", "PcMode", "LastBackupUtc", "LastBackupHash",
    };

    /// <summary>Esta configuração pode ir para a nuvem?</summary>
    public static bool Allowed(string name) =>
        !SecretName().IsMatch(name) && !MachineOnly.Contains(name)
        && !name.EndsWith("Path", StringComparison.Ordinal) && !name.EndsWith("Folder", StringComparison.Ordinal) && !name.EndsWith("Ticks", StringComparison.Ordinal);

    /// <summary>Monta o backup a partir das configurações e dos arquivos da pasta do MB.</summary>
    public static string Build(AppSettings settings, string folder, string appVersion)
    {
        var all = JsonSerializer.SerializeToNode(settings)!.AsObject();
        var filtered = new JsonObject();
        foreach (var (key, value) in all)
            if (Allowed(key)) filtered[key] = value?.DeepClone();
        var files = new JsonObject();
        string fans = Path.Combine(folder, "fan-profiles.json");
        if (File.Exists(fans) && TryParse(File.ReadAllText(fans)) is { } f) files["fan-profiles.json"] = f;
        var studio = new JsonObject();
        if (Directory.Exists(StudioProfiles.DefaultFolder))
            foreach (string file in Directory.EnumerateFiles(StudioProfiles.DefaultFolder, "*.json").Take(30))
                if (TryParse(File.ReadAllText(file)) is { } node && node is JsonObject obj) studio[Path.GetFileName(file)] = Clean(obj);
        var root = new JsonObject
        {
            ["v"] = Version, ["app"] = appVersion, ["createdUtc"] = DateTime.UtcNow.ToString("o"),
            ["settings"] = filtered, ["files"] = files, ["studioProfiles"] = studio,
        };
        return root.ToJsonString();
    }

    /// <summary>Tira de um objeto qualquer chave com cara de segredo (as predefinições do estúdio já não guardam, mas por garantia).</summary>
    private static JsonObject Clean(JsonObject obj)
    {
        var copy = new JsonObject();
        foreach (var (k, v) in obj) if (Allowed(k)) copy[k] = v?.DeepClone();
        return copy;
    }

    private static JsonNode? TryParse(string json)
    {
        try { return JsonNode.Parse(json); }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Aplica o backup: junta as configurações permitidas por cima das atuais (o que não veio, ou é deste PC, fica como está)
    /// e grava as predefinições e perfis. Devolve as configurações novas para gravar e reiniciar o programa.
    /// </summary>
    public static AppSettings Restore(string backupJson, AppSettings current, string folder)
    {
        var root = JsonNode.Parse(backupJson)?.AsObject() ?? throw new InvalidDataException("Backup vazio.");
        if ((int?)root["v"] is not { } v || v > Version) throw new InvalidDataException("Este backup é de uma versão mais nova do MB. Atualize o programa.");
        var now = JsonSerializer.SerializeToNode(current)!.AsObject();
        if (root["settings"] is JsonObject saved)
            foreach (var (key, value) in saved)
                if (Allowed(key) && now.ContainsKey(key)) now[key] = value?.DeepClone();
        var merged = now.Deserialize<AppSettings>() ?? current;
        if (root["files"] is JsonObject files && files["fan-profiles.json"] is { } fans)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "fan-profiles.json"), fans.ToJsonString());
        }
        if (root["studioProfiles"] is JsonObject studio && studio.Count > 0)
        {
            Directory.CreateDirectory(StudioProfiles.DefaultFolder);
            foreach (var (name, node) in studio)
            {
                string safe = Path.GetFileName(name);
                if (node is JsonObject obj && safe.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    File.WriteAllText(Path.Combine(StudioProfiles.DefaultFolder, safe), Clean(obj).ToJsonString());
            }
        }
        return merged;
    }
}
