namespace MBExtreme.Core.Tweaks;

/// <summary>
/// Ajuste feito por comando (fsutil, powercfg, bcdedit, PowerShell...). O estado é lido e escrito por funções,
/// o valor original é guardado antes da primeira alteração e restaurado ao desfazer.
/// </summary>
public sealed class CommandTweak : Tweak
{
    private const string Unknown = "<desconhecido>";
    private readonly Func<string?> _read;
    private readonly string _applied;
    private readonly Action<string> _write;
    private readonly string _fallback;
    private readonly Action? _beforeFirstApply;

    /// <param name="read">Estado atual como texto (nulo = não foi possível ler).</param>
    /// <param name="applied">Estado quando o ajuste está aplicado.</param>
    /// <param name="write">Grava um estado.</param>
    /// <param name="fallback">Estado a restaurar quando não há backup (ex.: o padrão do Windows).</param>
    /// <param name="beforeFirstApply">Executado antes de a primeira alteração guardar o estado (ex.: exportar o BCD).</param>
    public CommandTweak(TweakStore store, string id, TweakCategory category, TweakRisk risk, string title, string description,
        Func<string?> read, string applied, Action<string> write, string fallback, Action? beforeFirstApply = null)
        : base(store, id, category, risk, title, description)
    {
        _read = read;
        _applied = applied;
        _write = write;
        _fallback = fallback;
        _beforeFirstApply = beforeFirstApply;
    }

    private string StateKey => Id + "|state";

    public override bool? IsApplied()
    {
        string? current = _read();
        return current is null ? null : string.Equals(current, _applied, StringComparison.OrdinalIgnoreCase);
    }

    public override void Apply()
    {
        if (!Store.Has(StateKey))
        {
            _beforeFirstApply?.Invoke();
            Store.Set(StateKey, _read() ?? Unknown);
        }

        _write(_applied);
    }

    public override void Revert()
    {
        string? saved = Store.Get(StateKey);
        string target = saved is null or Unknown ? _fallback : saved;
        _write(target);
        Store.Remove(StateKey);
    }
}

/// <summary>Leitura da saída de programas do Windows sem depender do idioma (só números e nomes de opção em inglês).</summary>
public static class CommandParsers
{
    /// <summary>"NTFS DisableDeleteNotify = 0 (Disabled)" → "0". Pega o primeiro valor depois do nome.</summary>
    public static string? FsutilValue(string output, string name)
    {
        foreach (string line in output.Split('\n'))
        {
            int at = line.IndexOf(name, StringComparison.OrdinalIgnoreCase);
            if (at < 0) continue;
            int eq = line.IndexOf('=', at);
            if (eq < 0) continue;
            string rest = line[(eq + 1)..].Trim();
            int end = 0;
            while (end < rest.Length && char.IsDigit(rest[end])) end++;
            if (end > 0) return rest[..end];
        }
        return null;
    }

    /// <summary>Linha do bcdedit "nome   valor" → valor; nulo se o nome não aparece.</summary>
    public static string? BcdValue(string output, string name)
    {
        foreach (string raw in output.Split('\n'))
        {
            string line = raw.Trim();
            if (!line.StartsWith(name, StringComparison.OrdinalIgnoreCase)) continue;
            if (line.Length > name.Length && !char.IsWhiteSpace(line[name.Length])) continue;
            string value = line[name.Length..].Trim();
            return value.Length == 0 ? null : value;
        }
        return null;
    }
}
