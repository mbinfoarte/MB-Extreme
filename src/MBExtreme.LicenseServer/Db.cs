using Microsoft.Data.Sqlite;

namespace MBExtreme.LicenseServer;

public sealed record User(long Id, string Email, string PasswordHash);

public sealed record Subscription(long UserId, string Plan, string Status, DateTimeOffset PeriodEnd, int MaxDevices, string? Provider, string? ProviderRef);

public sealed record Device(long Id, long UserId, string DeviceId, string Name, DateTimeOffset ActivatedAt, DateTimeOffset LastSeen);

public sealed record LoginCode(string Code, string UserCode, string DeviceId, string DeviceName, long? UserId, string State, DateTimeOffset ExpiresAt, DateTimeOffset LastPoll);

/// <summary>Acesso ao banco SQLite (contas, assinaturas, dispositivos, códigos de login e sessões).</summary>
public sealed class Db
{
    private readonly string _connectionString;

    public Db(string path)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared }.ToString();
        using var c = Open();
        Exec(c, """
            CREATE TABLE IF NOT EXISTS users (id INTEGER PRIMARY KEY, email TEXT NOT NULL UNIQUE, password_hash TEXT NOT NULL, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS subscriptions (user_id INTEGER PRIMARY KEY, plan TEXT NOT NULL, status TEXT NOT NULL, period_end TEXT NOT NULL, max_devices INTEGER NOT NULL, provider TEXT, provider_ref TEXT);
            CREATE TABLE IF NOT EXISTS devices (id INTEGER PRIMARY KEY, user_id INTEGER NOT NULL, device_id TEXT NOT NULL, name TEXT NOT NULL, activated_at TEXT NOT NULL, last_seen TEXT NOT NULL, UNIQUE(user_id, device_id));
            CREATE TABLE IF NOT EXISTS device_codes (code TEXT PRIMARY KEY, user_code TEXT NOT NULL UNIQUE, device_id TEXT NOT NULL, device_name TEXT NOT NULL, user_id INTEGER, state TEXT NOT NULL, expires_at TEXT NOT NULL, last_poll TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS sessions (token TEXT PRIMARY KEY, user_id INTEGER NOT NULL, expires_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS webhook_events (id TEXT PRIMARY KEY, received_at TEXT NOT NULL);
            """);
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    private static void Exec(SqliteConnection c, string sql, params (string, object?)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private T? Query<T>(string sql, Func<SqliteDataReader, T> map, params (string, object?)[] args)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        return r.Read() ? map(r) : default;
    }

    private List<T> QueryAll<T>(string sql, Func<SqliteDataReader, T> map, params (string, object?)[] args)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    private void Run(string sql, params (string, object?)[] args)
    {
        using var c = Open();
        Exec(c, sql, args);
    }

    private static string T(DateTimeOffset d) => d.UtcDateTime.ToString("O");

    private static DateTimeOffset D(string s) => DateTimeOffset.Parse(s, null, System.Globalization.DateTimeStyles.RoundtripKind);

    // ---- usuários

    public long? CreateUser(string email, string passwordHash)
    {
        try
        {
            using var c = Open();
            Exec(c, "INSERT INTO users(email, password_hash, created_at) VALUES($e, $p, $t)", ("$e", email), ("$p", passwordHash), ("$t", T(DateTimeOffset.UtcNow)));
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT last_insert_rowid()";
            return (long)cmd.ExecuteScalar()!;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return null; // e-mail já cadastrado
        }
    }

    public User? UserByEmail(string email) =>
        Query("SELECT id, email, password_hash FROM users WHERE email = $e", r => new User(r.GetInt64(0), r.GetString(1), r.GetString(2)), ("$e", email));

    public User? UserById(long id) =>
        Query("SELECT id, email, password_hash FROM users WHERE id = $i", r => new User(r.GetInt64(0), r.GetString(1), r.GetString(2)), ("$i", id));

    // ---- assinatura

    public Subscription? SubscriptionOf(long userId) =>
        Query("SELECT user_id, plan, status, period_end, max_devices, provider, provider_ref FROM subscriptions WHERE user_id = $u",
            r => new Subscription(r.GetInt64(0), r.GetString(1), r.GetString(2), D(r.GetString(3)), r.GetInt32(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6)), ("$u", userId));

    public void SetSubscription(Subscription s) =>
        Run("""
            INSERT INTO subscriptions(user_id, plan, status, period_end, max_devices, provider, provider_ref) VALUES($u, $p, $s, $e, $m, $pr, $rf)
            ON CONFLICT(user_id) DO UPDATE SET plan = $p, status = $s, period_end = $e, max_devices = $m, provider = $pr, provider_ref = $rf
            """, ("$u", s.UserId), ("$p", s.Plan), ("$s", s.Status), ("$e", T(s.PeriodEnd)), ("$m", s.MaxDevices), ("$pr", s.Provider), ("$rf", s.ProviderRef));

    public bool RememberWebhook(string id)
    {
        try
        {
            Run("INSERT INTO webhook_events(id, received_at) VALUES($i, $t)", ("$i", id), ("$t", T(DateTimeOffset.UtcNow)));
            return true;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return false; // evento repetido
        }
    }

    // ---- dispositivos

    public List<Device> DevicesOf(long userId) =>
        QueryAll("SELECT id, user_id, device_id, name, activated_at, last_seen FROM devices WHERE user_id = $u ORDER BY activated_at",
            r => new Device(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), D(r.GetString(4)), D(r.GetString(5))), ("$u", userId));

    public Device? DeviceOf(long userId, string deviceId) => DevicesOf(userId).FirstOrDefault(d => d.DeviceId == deviceId);

    public void UpsertDevice(long userId, string deviceId, string name)
    {
        string now = T(DateTimeOffset.UtcNow);
        Run("""
            INSERT INTO devices(user_id, device_id, name, activated_at, last_seen) VALUES($u, $d, $n, $t, $t)
            ON CONFLICT(user_id, device_id) DO UPDATE SET name = $n, last_seen = $t
            """, ("$u", userId), ("$d", deviceId), ("$n", name), ("$t", now));
    }

    public void TouchDevice(long userId, string deviceId) =>
        Run("UPDATE devices SET last_seen = $t WHERE user_id = $u AND device_id = $d", ("$t", T(DateTimeOffset.UtcNow)), ("$u", userId), ("$d", deviceId));

    public bool RemoveDevice(long userId, string deviceId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM devices WHERE user_id = $u AND device_id = $d";
        cmd.Parameters.AddWithValue("$u", userId);
        cmd.Parameters.AddWithValue("$d", deviceId);
        return cmd.ExecuteNonQuery() > 0;
    }

    // ---- códigos de login do aplicativo

    public void CreateDeviceCode(LoginCode code) =>
        Run("INSERT INTO device_codes(code, user_code, device_id, device_name, user_id, state, expires_at, last_poll) VALUES($c, $u, $d, $n, NULL, 'pending', $e, $p)",
            ("$c", code.Code), ("$u", code.UserCode), ("$d", code.DeviceId), ("$n", code.DeviceName), ("$e", T(code.ExpiresAt)), ("$p", T(DateTimeOffset.UtcNow.AddYears(-1))));

    public LoginCode? DeviceCodeBy(string column, string value)
    {
        if (column is not ("code" or "user_code")) throw new ArgumentException("coluna inválida");
        return Query($"SELECT code, user_code, device_id, device_name, user_id, state, expires_at, last_poll FROM device_codes WHERE {column} = $v",
            r => new LoginCode(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetInt64(4), r.GetString(5), D(r.GetString(6)), D(r.GetString(7))),
            ("$v", value));
    }

    public void SetDeviceCodeState(string code, string state, long? userId) =>
        Run("UPDATE device_codes SET state = $s, user_id = $u WHERE code = $c", ("$s", state), ("$u", userId), ("$c", code));

    public void TouchPoll(string code) => Run("UPDATE device_codes SET last_poll = $t WHERE code = $c", ("$t", T(DateTimeOffset.UtcNow)), ("$c", code));

    public void DeleteDeviceCode(string code) => Run("DELETE FROM device_codes WHERE code = $c", ("$c", code));

    public void PurgeExpired()
    {
        string now = T(DateTimeOffset.UtcNow);
        Run("DELETE FROM device_codes WHERE expires_at < $t", ("$t", now));
        Run("DELETE FROM sessions WHERE expires_at < $t", ("$t", now));
    }

    // ---- sessões do site

    public void CreateSession(string token, long userId, DateTimeOffset expires) =>
        Run("INSERT INTO sessions(token, user_id, expires_at) VALUES($t, $u, $e)", ("$t", token), ("$u", userId), ("$e", T(expires)));

    public long? SessionUser(string token)
    {
        var row = Query("SELECT user_id, expires_at FROM sessions WHERE token = $t", r => (r.GetInt64(0), D(r.GetString(1))), ("$t", token));
        return row.Item1 != 0 && row.Item2 > DateTimeOffset.UtcNow ? row.Item1 : null;
    }

    public void DeleteSession(string token) => Run("DELETE FROM sessions WHERE token = $t", ("$t", token));
}
