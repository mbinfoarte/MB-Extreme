using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MBExtreme.Core.Remote;

/// <summary>Um PC atendido pelo técnico (lista "Atendidos recentemente").</summary>
public sealed record RustDeskPeer(string Id, string Name, DateTime LastAccess);

/// <summary>
/// Ponte com o RustDesk oficial (código aberto, AGPL-3.0; usado sem modificação, como programa separado). O MB Extreme baixa o
/// instalador oficial assinado, confere o SHA-256, instala em silêncio e comanda pela linha de comando: ID, senha, opções e conexão.
/// A conexão usa a rede pública do RustDesk (sem servidor próprio). Telas e textos ficam no MB; a janela da sessão é a do RustDesk.
/// </summary>
public static class RustDesk
{
    public const string Version = "1.5.3";
    public const string DownloadUrl = "https://github.com/rustdesk/rustdesk/releases/download/1.5.3/rustdesk-1.5.3-x86_64.exe";
    /// <summary>SHA-256 do instalador oficial 1.5.3 (assinado por PURSLANE, a empresa do RustDesk), conferido em 20/11/2026.</summary>
    public const string Sha256 = "523ba0efbf62a21739961b62c7daaffb44d3da86139c546b364fd4e1333ed249";
    public const string SourceUrl = "https://github.com/rustdesk/rustdesk";
    public const string License = "AGPL-3.0";

    /// <summary>Onde o RustDesk fica depois de instalado.</summary>
    public static string InstalledExe(string programFiles) => Path.Combine(programFiles, "RustDesk", "rustdesk.exe");

    // ---------- comandos (sempre lista de argumentos, nunca texto solto num shell) ----------

    public static IReadOnlyList<string> InstallArgs() => new[] { "--silent-install" };
    public static IReadOnlyList<string> GetIdArgs() => new[] { "--get-id" };
    public static IReadOnlyList<string> PasswordArgs(string password) => new[] { "--password", password };
    public static IReadOnlyList<string> OptionArgs(string key, string value) => new[] { "--option", key, value };

    /// <summary>Conectar (tela) ou abrir a transferência de arquivos no ID do cliente.</summary>
    public static IReadOnlyList<string> ConnectArgs(string id, string? password, bool files = false)
    {
        var args = new List<string> { files ? "--file-transfer" : "--connect", NormalizeId(id) ?? throw new ArgumentException("ID inválido.", nameof(id)) };
        if (!string.IsNullOrWhiteSpace(password)) { args.Add("--password"); args.Add(password.Trim()); }
        return args;
    }

    /// <summary>
    /// Modo de aceitar conexões: com "acesso sem ninguém" só a senha fixa vale (ninguém precisa clicar); no modo normal vale a senha
    /// da sessão ou o clique do cliente.
    /// </summary>
    public static IReadOnlyList<(string Key, string Value)> Options(bool unattended) => new[]
    {
        ("approve-mode", unattended ? "password" : ""),
        ("verification-method", "use-permanent-password"),
    };

    // ---------- ID e senha ----------

    private static readonly Regex IdPattern = new(@"^\d{9,10}$", RegexOptions.Compiled);

    /// <summary>ID só com dígitos (aceita "482 917 305" ou "482-917-305"), ou null se não for um ID.</summary>
    public static string? NormalizeId(string? text)
    {
        string digits = new((text ?? "").Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '.').ToArray());
        return IdPattern.IsMatch(digits) ? digits : null;
    }

    /// <summary>Lê o ID na saída do "--get-id" (última linha com 9 ou 10 dígitos).</summary>
    public static string? ParseId(string output) =>
        output.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Select(NormalizeId).LastOrDefault(id => id is not null);

    /// <summary>"482917305" vira "482 917 305".</summary>
    public static string FormatId(string id)
    {
        string d = NormalizeId(id) ?? id;
        return string.Join(' ', Enumerable.Range(0, (d.Length + 2) / 3).Select(i => d.Substring(i * 3, Math.Min(3, d.Length - i * 3))));
    }

    private const string Alphabet = "abcdefghjkmnpqrstuvwxyz23456789";   // sem 0/o, 1/l/i: fácil de ditar por telefone

    /// <summary>Senha da sessão: 8 caracteres em dois blocos ("k7m4-2xqp"), gerada com gerador criptográfico.</summary>
    public static string NewSessionPassword()
    {
        Span<char> c = stackalloc char[9];
        for (int i = 0; i < 9; i++) c[i] = i == 4 ? '-' : Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(c);
    }

    /// <summary>Senha fixa do acesso sem ninguém: 8 ou mais, com letra e número.</summary>
    public static string? ValidateFixedPassword(string password)
    {
        if (password.Length < 8) return "Use pelo menos 8 caracteres.";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit)) return "Misture letras e números.";
        if (password.Any(char.IsWhiteSpace)) return "Não use espaços.";
        return null;
    }

    public static string ShareText(string id, string password) =>
        $"Acesso remoto MB Extreme\nID: {FormatId(id)}\nSenha: {password}\n\nEsses dados valem só para esta sessão.";

    /// <summary>Arquivo baixado é o instalador oficial?</summary>
    public static bool IsOfficial(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).Equals(Sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Linha de comando do processo indica uma sessão aberta neste PC (o RustDesk abre o "--cm" quando alguém conecta).</summary>
    public static bool IsSessionProcess(string? commandLine) =>
        commandLine is not null && commandLine.Contains("rustdesk", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(commandLine, @"(^|\s)--cm(\s|$)");

    // ---------- atendidos recentemente ----------

    public static string PeersFile(string appData) => Path.Combine(appData, "MBExtreme", "atendidos.json");

    public static IReadOnlyList<RustDeskPeer> LoadPeers(string file)
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<List<RustDeskPeer>>(File.ReadAllText(file)) ?? new() : new(); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return Array.Empty<RustDeskPeer>(); }
    }

    /// <summary>Coloca o PC no topo (atualiza nome e data); guarda os 30 mais recentes.</summary>
    public static IReadOnlyList<RustDeskPeer> Remember(IReadOnlyList<RustDeskPeer> list, string id, string? name, DateTime when)
    {
        string norm = NormalizeId(id) ?? throw new ArgumentException("ID inválido.", nameof(id));
        var old = list.FirstOrDefault(p => p.Id == norm);
        string label = !string.IsNullOrWhiteSpace(name) ? name.Trim() : old?.Name ?? FormatId(norm);
        return new[] { new RustDeskPeer(norm, label, when) }.Concat(list.Where(p => p.Id != norm)).Take(30).ToList();
    }

    public static void SavePeers(string file, IReadOnlyList<RustDeskPeer> list)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
    }
}
