using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MBExtreme.Core.Settings;

/// <summary>Um perfil salvo do Estúdio: nome, quando foi salvo e o que ele guarda.</summary>
public sealed record StudioProfileInfo(string Name, DateTime SavedAt, string Path);

/// <summary>
/// Perfis do Estúdio (como as "coleções de cenas" e perfis do OBS): guarda tudo o que foi configurado — vídeo, live, gravação, câmeras,
/// cenas, áudio, sobreposições, Pix, legendas e vertical — com um nome, para voltar a ele num clique.
/// Chaves de transmissão, senhas e logins nunca entram no perfil (ficam só nas configurações, protegidas pelo Windows).
/// </summary>
public static class StudioProfiles
{
    public const int MaxNameLength = 40;

    /// <summary>Configurações que não fazem parte de um perfil: segredos, o teste da placa e o tamanho dos painéis da tela.</summary>
    private static readonly HashSet<string> Skipped = new(StringComparer.Ordinal)
    {
        "StudioEncoderCache", "StudioEncoderStamp", "StudioEncoderBestChosen",
        "StudioRememberKey", "StudioControlsWidth", "StudioPreviewHeight", "StudioPreviewHeightStacked", "StudioLayoutMode",
        "StudioTwitchClientId", "StudioYtClientId", "StudioChatId",
    };

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Propriedades do Estúdio que entram no perfil.</summary>
    public static IReadOnlyList<PropertyInfo> Properties { get; } = typeof(AppSettings)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.Name.StartsWith("Studio", StringComparison.Ordinal) && p.CanRead && p.CanWrite && !IsSecret(p.Name) && !Skipped.Contains(p.Name))
        .OrderBy(p => p.Name, StringComparer.Ordinal)
        .ToList();

    private static bool IsSecret(string name) =>
        name.Contains("Key", StringComparison.Ordinal) && !name.Equals("StudioPixKey", StringComparison.Ordinal)   // a chave Pix é pública (vai no QR)
        || name.Contains("Protected", StringComparison.Ordinal) || name.Contains("Secret", StringComparison.Ordinal)
        || name.Contains("Refresh", StringComparison.Ordinal) || name.Contains("Token", StringComparison.Ordinal) || name.Contains("Password", StringComparison.Ordinal);

    /// <summary>Cópia do que está configurado agora (o texto do perfil).</summary>
    public static JsonObject Capture(AppSettings settings)
    {
        var root = new JsonObject();
        foreach (var p in Properties)
            root[p.Name] = JsonSerializer.SerializeToNode(p.GetValue(settings), p.PropertyType, Json);
        return root;
    }

    /// <summary>Aplica um perfil: só o que está nele muda; o que não existe mais ou veio estragado é ignorado. Devolve quantos itens foram aplicados.</summary>
    public static int Apply(AppSettings settings, JsonObject profile)
    {
        int applied = 0;
        foreach (var p in Properties)
        {
            if (!profile.TryGetPropertyValue(p.Name, out var node)) continue;
            try
            {
                object? value = node is null ? null : node.Deserialize(p.PropertyType, Json);
                if (value is null && p.PropertyType.IsValueType) continue;
                p.SetValue(settings, value);
                applied++;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException or ArgumentException) { }
        }
        return applied;
    }

    /// <summary>Nome válido para arquivo: sem caracteres proibidos, sem espaços nas pontas, até <see cref="MaxNameLength"/> letras.</summary>
    public static string? CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var bad = System.IO.Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }).ToHashSet();
        string clean = new string(name.Trim().Where(c => !bad.Contains(c) && !char.IsControl(c)).ToArray()).Trim().TrimEnd('.');
        if (clean.Length > MaxNameLength) clean = clean[..MaxNameLength].Trim();
        return clean.Length == 0 ? null : clean;
    }

    public static string DefaultFolder =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "perfis-estudio");

    public static IReadOnlyList<StudioProfileInfo> List(string? folder = null)
    {
        folder ??= DefaultFolder;
        if (!Directory.Exists(folder)) return Array.Empty<StudioProfileInfo>();
        return Directory.GetFiles(folder, "*.json")
            .Select(f => new StudioProfileInfo(System.IO.Path.GetFileNameWithoutExtension(f), File.GetLastWriteTime(f), f))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Salva (ou substitui) o perfil com este nome. Devolve o nome usado, ou null se o nome não serve.</summary>
    public static string? Save(AppSettings settings, string name, string? folder = null)
    {
        if (CleanName(name) is not { } clean) return null;
        folder ??= DefaultFolder;
        Directory.CreateDirectory(folder);
        File.WriteAllText(System.IO.Path.Combine(folder, clean + ".json"), Capture(settings).ToJsonString(Json));
        return clean;
    }

    /// <summary>Carrega o perfil nas configurações (sem salvar o arquivo de configurações: quem chama decide). Devolve quantos itens foram aplicados, ou -1 se o perfil não abriu.</summary>
    public static int Load(AppSettings settings, string name, string? folder = null)
    {
        if (CleanName(name) is not { } clean) return -1;
        string path = System.IO.Path.Combine(folder ?? DefaultFolder, clean + ".json");
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject profile) return -1;
            return Apply(settings, profile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return -1; }
    }

    public static bool Delete(string name, string? folder = null)
    {
        if (CleanName(name) is not { } clean) return false;
        string path = System.IO.Path.Combine(folder ?? DefaultFolder, clean + ".json");
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
