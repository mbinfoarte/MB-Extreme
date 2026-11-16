using System.Text.Json;

namespace MBExtreme.Core.Notices;

/// <summary>Tipos de aviso: a pessoa liga ou desliga cada um.</summary>
public enum NoticeKind { Health, Driver, Temperature, Trial, Backup, Update, News }

/// <summary>Um aviso do programa (aparece no sino e, se ligado, como aviso do Windows).</summary>
public sealed record Notice(string Id, NoticeKind Kind, string Title, string Text, DateTime At, bool Read = false, string? PageKey = null);

/// <summary>
/// Central de avisos: guarda os últimos 50, não repete o mesmo aviso (mesmo Id) e respeita os tipos desligados.
/// </summary>
public sealed class NoticeCenter
{
    public const int Keep = 50;
    private readonly List<Notice> _items = new();
    private readonly object _gate = new();
    private readonly string? _file;
    private readonly Func<NoticeKind, bool> _enabled;

    public NoticeCenter(string? file, Func<NoticeKind, bool> enabled)
    {
        _file = file;
        _enabled = enabled;
        Load();
    }

    /// <summary>Aviso novo (já filtrado pelas preferências). Chamado na thread de quem avisou.</summary>
    public event Action<Notice>? Added;
    public event Action? Changed;

    public static string Label(NoticeKind kind) => kind switch
    {
        NoticeKind.Health => "Saúde do PC",
        NoticeKind.Driver => "Drivers novos",
        NoticeKind.Temperature => "Temperatura alta",
        NoticeKind.Trial => "Teste grátis e plano",
        NoticeKind.Backup => "Backup das configurações",
        NoticeKind.Update => "Versão nova do MB",
        _ => "Novidades",
    };

    public IReadOnlyList<Notice> Items { get { lock (_gate) return _items.ToList(); } }

    public int Unread { get { lock (_gate) return _items.Count(n => !n.Read); } }

    /// <summary>Acrescenta (se o tipo estiver ligado e o Id ainda não existir). Devolve true quando entrou.</summary>
    public bool Add(Notice notice)
    {
        if (!_enabled(notice.Kind)) return false;
        lock (_gate)
        {
            if (_items.Any(n => n.Id == notice.Id)) return false;
            _items.Insert(0, notice);
            if (_items.Count > Keep) _items.RemoveRange(Keep, _items.Count - Keep);
        }
        Save();
        Added?.Invoke(notice);
        Changed?.Invoke();
        return true;
    }

    public void MarkAllRead()
    {
        lock (_gate)
        {
            for (int i = 0; i < _items.Count; i++) _items[i] = _items[i] with { Read = true };
        }
        Save();
        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_gate) _items.Clear();
        Save();
        Changed?.Invoke();
    }

    private void Load()
    {
        if (_file is null) return;
        try
        {
            if (!File.Exists(_file)) return;
            var list = JsonSerializer.Deserialize<List<Notice>>(File.ReadAllText(_file));
            if (list is not null) lock (_gate) _items.AddRange(list.Where(n => !string.IsNullOrWhiteSpace(n.Id)).Take(Keep));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }

    private void Save()
    {
        if (_file is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            List<Notice> copy;
            lock (_gate) copy = _items.ToList();
            File.WriteAllText(_file, JsonSerializer.Serialize(copy));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
