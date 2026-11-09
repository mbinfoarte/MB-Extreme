using Microsoft.Data.Sqlite;

namespace MBExtreme.LicenseServer;

public sealed record User(long Id, string Email, string PasswordHash, string Name = "");

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
            CREATE TABLE IF NOT EXISTS users (id INTEGER PRIMARY KEY, email TEXT NOT NULL UNIQUE, password_hash TEXT NOT NULL, created_at TEXT NOT NULL, name TEXT NOT NULL DEFAULT '');
            CREATE TABLE IF NOT EXISTS subscriptions (user_id INTEGER PRIMARY KEY, plan TEXT NOT NULL, status TEXT NOT NULL, period_end TEXT NOT NULL, max_devices INTEGER NOT NULL, provider TEXT, provider_ref TEXT);
            CREATE TABLE IF NOT EXISTS devices (id INTEGER PRIMARY KEY, user_id INTEGER NOT NULL, device_id TEXT NOT NULL, name TEXT NOT NULL, activated_at TEXT NOT NULL, last_seen TEXT NOT NULL, UNIQUE(user_id, device_id));
            CREATE TABLE IF NOT EXISTS device_codes (code TEXT PRIMARY KEY, user_code TEXT NOT NULL UNIQUE, device_id TEXT NOT NULL, device_name TEXT NOT NULL, user_id INTEGER, state TEXT NOT NULL, expires_at TEXT NOT NULL, last_poll TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS sessions (token TEXT PRIMARY KEY, user_id INTEGER NOT NULL, expires_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS webhook_events (id TEXT PRIMARY KEY, received_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS trials (device_hash TEXT PRIMARY KEY, user_id INTEGER NOT NULL, started_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS activation_keys (key_hash TEXT PRIMARY KEY, plan TEXT NOT NULL, days INTEGER NOT NULL, max_devices INTEGER NOT NULL, note TEXT NOT NULL DEFAULT '', created_at TEXT NOT NULL, redeemed_by INTEGER, redeemed_at TEXT);
            CREATE TABLE IF NOT EXISTS remote_devices (id TEXT PRIMARY KEY, secret_hash TEXT NOT NULL, device_hash TEXT NOT NULL UNIQUE, created_at TEXT NOT NULL, last_seen TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS affiliates (user_id INTEGER PRIMARY KEY, code TEXT NOT NULL UNIQUE, pix_key TEXT NOT NULL DEFAULT '', created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS referrals (user_id INTEGER PRIMARY KEY, affiliate_user_id INTEGER NOT NULL, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS commissions (id INTEGER PRIMARY KEY, affiliate_user_id INTEGER NOT NULL, referred_user_id INTEGER NOT NULL, payment_ref TEXT NOT NULL UNIQUE, amount_paid_cents INTEGER NOT NULL, rate REAL NOT NULL, commission_cents INTEGER NOT NULL, status TEXT NOT NULL DEFAULT 'approved', payout_id INTEGER, created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS payouts (id INTEGER PRIMARY KEY, affiliate_user_id INTEGER NOT NULL, amount_cents INTEGER NOT NULL, pix_key TEXT NOT NULL, requested_at TEXT NOT NULL, paid_at TEXT, note TEXT NOT NULL DEFAULT '');
            CREATE TABLE IF NOT EXISTS suggestions (id INTEGER PRIMARY KEY, kind TEXT NOT NULL, text TEXT NOT NULL, contact TEXT NOT NULL DEFAULT '', app_version TEXT NOT NULL DEFAULT '', windows TEXT NOT NULL DEFAULT '', report TEXT NOT NULL DEFAULT '', ip_hash TEXT NOT NULL DEFAULT '', created_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS remote_audit (id INTEGER PRIMARY KEY, client_id TEXT NOT NULL, technician TEXT NOT NULL, requested_at TEXT NOT NULL, accepted INTEGER NOT NULL DEFAULT 0, ended_at TEXT, bytes INTEGER NOT NULL DEFAULT 0, result TEXT NOT NULL DEFAULT '');
            """);
        try { Exec(c, "ALTER TABLE users ADD COLUMN name TEXT NOT NULL DEFAULT ''"); } // bancos criados antes da coluna de nome
        catch (SqliteException) { }
        try { Exec(c, "ALTER TABLE users ADD COLUMN google_sub TEXT"); } // bancos criados antes do login com Google
        catch (SqliteException) { }
        Exec(c, "CREATE UNIQUE INDEX IF NOT EXISTS ix_users_google ON users(google_sub) WHERE google_sub IS NOT NULL");
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

    public long? CreateUser(string email, string passwordHash, string name = "")
    {
        try
        {
            using var c = Open();
            Exec(c, "INSERT INTO users(email, password_hash, created_at, name) VALUES($e, $p, $t, $n)", ("$e", email), ("$p", passwordHash), ("$t", T(DateTimeOffset.UtcNow)), ("$n", name));
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
        Query("SELECT id, email, password_hash, name FROM users WHERE email = $e", r => new User(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3)), ("$e", email));

    public User? UserById(long id) =>
        Query("SELECT id, email, password_hash, name FROM users WHERE id = $i", r => new User(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3)), ("$i", id));

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

    // ---- acesso remoto

    public sealed record RemoteDevice(string Id, string SecretHash, string DeviceHash);

    public sealed record RemoteAuditRow(long Id, string ClientId, string Technician, DateTimeOffset RequestedAt, bool Accepted, DateTimeOffset? EndedAt, long Bytes, string Result);

    public RemoteDevice? RemoteDeviceById(string id) =>
        Query("SELECT id, secret_hash, device_hash FROM remote_devices WHERE id = $i", r => new RemoteDevice(r.GetString(0), r.GetString(1), r.GetString(2)), ("$i", id));

    public bool RemoteIdExists(string id) => RemoteDeviceById(id) is not null;

    /// <summary>Cria (ou troca) o ID do computador. Um computador tem um ID de cada vez.</summary>
    public bool RegisterRemoteDevice(string id, string secretHash, string deviceHash)
    {
        try
        {
            string now = T(DateTimeOffset.UtcNow);
            using var c = Open();
            Exec(c, "DELETE FROM remote_devices WHERE device_hash = $d", ("$d", deviceHash));
            Exec(c, "INSERT INTO remote_devices(id, secret_hash, device_hash, created_at, last_seen) VALUES($i, $s, $d, $t, $t)", ("$i", id), ("$s", secretHash), ("$d", deviceHash), ("$t", now));
            return true;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return false; // ID já usado: o chamador sorteia outro
        }
    }

    public void TouchRemoteDevice(string id) => Run("UPDATE remote_devices SET last_seen = $t WHERE id = $i", ("$t", T(DateTimeOffset.UtcNow)), ("$i", id));

    public long StartRemoteAudit(string clientId, string technician)
    {
        using var c = Open();
        Exec(c, "INSERT INTO remote_audit(client_id, technician, requested_at) VALUES($c, $t, $r)", ("$c", clientId), ("$t", technician), ("$r", T(DateTimeOffset.UtcNow)));
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT last_insert_rowid()";
        return (long)cmd.ExecuteScalar()!;
    }

    public void FinishRemoteAudit(long id, bool accepted, long bytes, string result) =>
        Run("UPDATE remote_audit SET accepted = $a, ended_at = $e, bytes = $b, result = $r WHERE id = $i",
            ("$a", accepted ? 1 : 0), ("$e", T(DateTimeOffset.UtcNow)), ("$b", bytes), ("$r", result), ("$i", id));

    public List<RemoteAuditRow> RemoteAudit(int count) =>
        QueryAll("SELECT id, client_id, technician, requested_at, accepted, ended_at, bytes, result FROM remote_audit ORDER BY id DESC LIMIT $n",
            r => new RemoteAuditRow(r.GetInt64(0), r.GetString(1), r.GetString(2), D(r.GetString(3)), r.GetInt32(4) == 1, r.IsDBNull(5) ? null : D(r.GetString(5)), r.GetInt64(6), r.GetString(7)), ("$n", count));

    // ---- teste grátis e chaves de ativação

    /// <summary>Marca o computador como já tendo usado o teste. Falso se ele já tinha usado (um teste por computador).</summary>
    public bool TryMarkTrial(string deviceHash, long userId)
    {
        try
        {
            Run("INSERT INTO trials(device_hash, user_id, started_at) VALUES($d, $u, $t)", ("$d", deviceHash), ("$u", userId), ("$t", T(DateTimeOffset.UtcNow)));
            return true;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return false;
        }
    }

    public void AddActivationKey(string keyHash, string plan, int days, int maxDevices, string note) =>
        Run("INSERT INTO activation_keys(key_hash, plan, days, max_devices, note, created_at) VALUES($h, $p, $d, $m, $n, $t)",
            ("$h", keyHash), ("$p", plan), ("$d", days), ("$m", maxDevices), ("$n", note), ("$t", T(DateTimeOffset.UtcNow)));

    public sealed record ActivationKey(string Plan, int Days, int MaxDevices, bool Redeemed);

    public ActivationKey? ActivationKeyBy(string keyHash) =>
        Query("SELECT plan, days, max_devices, redeemed_by FROM activation_keys WHERE key_hash = $h",
            r => new ActivationKey(r.GetString(0), r.GetInt32(1), r.GetInt32(2), !r.IsDBNull(3)), ("$h", keyHash));

    /// <summary>Resgata a chave de uma vez só (duas pessoas não resgatam a mesma).</summary>
    public bool RedeemActivationKey(string keyHash, long userId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE activation_keys SET redeemed_by = $u, redeemed_at = $t WHERE key_hash = $h AND redeemed_by IS NULL";
        cmd.Parameters.AddWithValue("$u", userId);
        cmd.Parameters.AddWithValue("$t", T(DateTimeOffset.UtcNow));
        cmd.Parameters.AddWithValue("$h", keyHash);
        return cmd.ExecuteNonQuery() > 0;
    }

    public User? UserByProviderRef(string fragment) =>
        Query("SELECT u.id, u.email, u.password_hash, u.name FROM users u JOIN subscriptions s ON s.user_id = u.id WHERE s.provider_ref LIKE $f LIMIT 1",
            r => new User(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3)), ("$f", "%" + fragment + "%"));

    // ---- login com Google

    public User? UserByGoogleSub(string sub) =>
        Query("SELECT id, email, password_hash, name FROM users WHERE google_sub = $g", r => new User(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3)), ("$g", sub));

    /// <summary>Conta criada pelo Google: a senha fica "inutilizável" (hash aleatório que ninguém conhece).</summary>
    public long? CreateGoogleUser(string email, string googleSub, string name, string unusablePasswordHash)
    {
        try
        {
            using var c = Open();
            Exec(c, "INSERT INTO users(email, password_hash, created_at, name, google_sub) VALUES($e, $p, $t, $n, $g)",
                ("$e", email), ("$p", unusablePasswordHash), ("$t", T(DateTimeOffset.UtcNow)), ("$n", name), ("$g", googleSub));
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT last_insert_rowid()";
            return (long)cmd.ExecuteScalar()!;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return null;
        }
    }

    public void LinkGoogle(long userId, string googleSub) =>
        Run("UPDATE users SET google_sub = $g WHERE id = $u AND google_sub IS NULL", ("$g", googleSub), ("$u", userId));

    // ---- afiliados

    public sealed record Affiliate(long UserId, string Code, string PixKey, DateTimeOffset CreatedAt);

    public sealed record Commission(long Id, long AffiliateUserId, long ReferredUserId, string PaymentRef, long AmountPaidCents, double Rate, long CommissionCents, string Status, long? PayoutId, DateTimeOffset CreatedAt);

    public sealed record Payout(long Id, long AffiliateUserId, long AmountCents, string PixKey, DateTimeOffset RequestedAt, DateTimeOffset? PaidAt, string Note);

    public sealed record AffiliateStats(int Signups, int PaidCustomers, long PendingCents, long AvailableCents, long RequestedCents, long PaidCents);

    private static Affiliate ReadAffiliate(SqliteDataReader r) => new(r.GetInt64(0), r.GetString(1), r.GetString(2), D(r.GetString(3)));

    public Affiliate? AffiliateOf(long userId) =>
        Query("SELECT user_id, code, pix_key, created_at FROM affiliates WHERE user_id = $u", ReadAffiliate, ("$u", userId));

    public Affiliate? AffiliateByCode(string code) =>
        Query("SELECT user_id, code, pix_key, created_at FROM affiliates WHERE code = $c", ReadAffiliate, ("$c", code));

    /// <summary>Cadastra o usuário como afiliado, com um código aleatório livre.</summary>
    public Affiliate CreateAffiliate(long userId)
    {
        if (AffiliateOf(userId) is { } existing) return existing;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            string code = AffiliateProgram.NewCode();
            try
            {
                Run("INSERT INTO affiliates(user_id, code, created_at) VALUES($u, $c, $t)", ("$u", userId), ("$c", code), ("$t", T(DateTimeOffset.UtcNow)));
                return AffiliateOf(userId)!;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
            {
                if (AffiliateOf(userId) is { } raced) return raced; // outra requisição já cadastrou
            }
        }
        throw new InvalidOperationException("Não foi possível gerar um código de afiliado.");
    }

    /// <summary>Troca o código do link (um nome fácil de lembrar). Falso se já existe.</summary>
    public bool SetAffiliateCode(long userId, string code)
    {
        try
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "UPDATE affiliates SET code = $c WHERE user_id = $u";
            cmd.Parameters.AddWithValue("$c", code);
            cmd.Parameters.AddWithValue("$u", userId);
            return cmd.ExecuteNonQuery() > 0;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return false;
        }
    }

    public void SetPixKey(long userId, string pixKey) => Run("UPDATE affiliates SET pix_key = $p WHERE user_id = $u", ("$p", pixKey), ("$u", userId));

    /// <summary>Guarda quem indicou esta conta (só a primeira indicação vale).</summary>
    public bool AddReferral(long userId, long affiliateUserId)
    {
        if (userId == affiliateUserId) return false;
        try
        {
            Run("INSERT INTO referrals(user_id, affiliate_user_id, created_at) VALUES($u, $a, $t)", ("$u", userId), ("$a", affiliateUserId), ("$t", T(DateTimeOffset.UtcNow)));
            return true;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return false;
        }
    }

    public long? ReferrerOf(long userId) =>
        Query("SELECT affiliate_user_id FROM referrals WHERE user_id = $u", r => (long?)r.GetInt64(0), ("$u", userId));

    /// <summary>Quantas comissões já existem deste cliente indicado (é o número do pagamento dele).</summary>
    public int CommissionCountFor(long referredUserId) =>
        Query("SELECT COUNT(*) FROM commissions WHERE referred_user_id = $u", r => r.GetInt32(0), ("$u", referredUserId));

    /// <summary>Registra a comissão de um pagamento. Falso se esse pagamento já foi registrado.</summary>
    public bool AddCommission(long affiliateUserId, long referredUserId, string paymentRef, long amountPaidCents, double rate, long commissionCents)
    {
        try
        {
            Run("INSERT INTO commissions(affiliate_user_id, referred_user_id, payment_ref, amount_paid_cents, rate, commission_cents, created_at) VALUES($a, $r, $p, $m, $x, $c, $t)",
                ("$a", affiliateUserId), ("$r", referredUserId), ("$p", paymentRef), ("$m", amountPaidCents), ("$x", rate), ("$c", commissionCents), ("$t", T(DateTimeOffset.UtcNow)));
            return true;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return false;
        }
    }

    /// <summary>Estorno do pagamento: a comissão deixa de valer (se ainda não foi paga; se já foi paga, fica como "estornada" para acerto).</summary>
    public bool ReverseCommission(string paymentRef)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE commissions SET status = 'reversed' WHERE payment_ref = $p AND status <> 'reversed'";
        cmd.Parameters.AddWithValue("$p", paymentRef);
        return cmd.ExecuteNonQuery() > 0;
    }

    private static Commission ReadCommission(SqliteDataReader r) =>
        new(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetString(3), r.GetInt64(4), r.GetDouble(5), r.GetInt64(6), r.GetString(7), r.IsDBNull(8) ? null : r.GetInt64(8), D(r.GetString(9)));

    public List<Commission> CommissionsOf(long affiliateUserId, int count = 50) =>
        QueryAll("SELECT id, affiliate_user_id, referred_user_id, payment_ref, amount_paid_cents, rate, commission_cents, status, payout_id, created_at FROM commissions WHERE affiliate_user_id = $a ORDER BY id DESC LIMIT $n",
            ReadCommission, ("$a", affiliateUserId), ("$n", count));

    public AffiliateStats StatsOf(long affiliateUserId, AffiliateProgram program, DateTimeOffset now)
    {
        int signups = Query("SELECT COUNT(*) FROM referrals WHERE affiliate_user_id = $a", r => r.GetInt32(0), ("$a", affiliateUserId));
        var all = QueryAll("SELECT id, affiliate_user_id, referred_user_id, payment_ref, amount_paid_cents, rate, commission_cents, status, payout_id, created_at FROM commissions WHERE affiliate_user_id = $a AND status <> 'reversed'",
            ReadCommission, ("$a", affiliateUserId));
        long pending = 0, available = 0, requested = 0, paid = 0;
        var confirmed = new HashSet<long>();
        var payouts = QueryAll("SELECT id, affiliate_user_id, amount_cents, pix_key, requested_at, paid_at, note FROM payouts WHERE affiliate_user_id = $a",
            r => new Payout(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetString(3), D(r.GetString(4)), r.IsDBNull(5) ? null : D(r.GetString(5)), r.GetString(6)), ("$a", affiliateUserId))
            .ToDictionary(p => p.Id);
        foreach (var commission in all)
        {
            bool released = program.Available(commission.CreatedAt, now);
            if (released) confirmed.Add(commission.ReferredUserId);
            if (commission.PayoutId is long pid)
            {
                if (payouts.TryGetValue(pid, out var payout) && payout.PaidAt is not null) paid += commission.CommissionCents;
                else requested += commission.CommissionCents;
            }
            else if (released) available += commission.CommissionCents;
            else pending += commission.CommissionCents;
        }
        return new AffiliateStats(signups, confirmed.Count, pending, available, requested, paid);
    }

    /// <summary>Pede o saque de tudo que está disponível. Devolve o pedido, ou nulo se o saldo não chega ao mínimo.</summary>
    public Payout? RequestPayout(long affiliateUserId, string pixKey, AffiliateProgram program, DateTimeOffset now)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        var eligible = new List<long>();
        long total = 0;
        using (var q = c.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = "SELECT id, commission_cents, created_at FROM commissions WHERE affiliate_user_id = $a AND status = 'approved' AND payout_id IS NULL";
            q.Parameters.AddWithValue("$a", affiliateUserId);
            using var r = q.ExecuteReader();
            while (r.Read())
                if (program.Available(D(r.GetString(2)), now)) { eligible.Add(r.GetInt64(0)); total += r.GetInt64(1); }
        }
        if (total < program.MinPayoutCents || eligible.Count == 0) return null;

        long payoutId;
        using (var ins = c.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO payouts(affiliate_user_id, amount_cents, pix_key, requested_at) VALUES($a, $m, $p, $t); SELECT last_insert_rowid();";
            ins.Parameters.AddWithValue("$a", affiliateUserId);
            ins.Parameters.AddWithValue("$m", total);
            ins.Parameters.AddWithValue("$p", pixKey);
            ins.Parameters.AddWithValue("$t", T(now));
            payoutId = (long)ins.ExecuteScalar()!;
        }
        foreach (long id in eligible)
        {
            using var up = c.CreateCommand();
            up.Transaction = tx;
            up.CommandText = "UPDATE commissions SET payout_id = $p WHERE id = $i";
            up.Parameters.AddWithValue("$p", payoutId);
            up.Parameters.AddWithValue("$i", id);
            up.ExecuteNonQuery();
        }
        tx.Commit();
        return new Payout(payoutId, affiliateUserId, total, pixKey, now, null, "");
    }

    public List<Payout> PayoutsOf(long? affiliateUserId, bool onlyOpen)
    {
        string where = (affiliateUserId is null ? "1=1" : "affiliate_user_id = $a") + (onlyOpen ? " AND paid_at IS NULL" : "");
        var args = affiliateUserId is null ? Array.Empty<(string, object?)>() : new (string, object?)[] { ("$a", affiliateUserId) };
        return QueryAll($"SELECT id, affiliate_user_id, amount_cents, pix_key, requested_at, paid_at, note FROM payouts WHERE {where} ORDER BY id DESC LIMIT 200",
            r => new Payout(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetString(3), D(r.GetString(4)), r.IsDBNull(5) ? null : D(r.GetString(5)), r.GetString(6)), args);
    }

    public bool MarkPayoutPaid(long payoutId, string note)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE payouts SET paid_at = $t, note = $n WHERE id = $i AND paid_at IS NULL";
        cmd.Parameters.AddWithValue("$t", T(DateTimeOffset.UtcNow));
        cmd.Parameters.AddWithValue("$n", note);
        cmd.Parameters.AddWithValue("$i", payoutId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public sealed record SuggestionRow(long Id, string Kind, string Text, string Contact, string AppVersion, string Windows, bool HasReport, DateTimeOffset CreatedAt);

    /// <summary>Guarda uma sugestão enviada pelo programa (textos cortados no tamanho máximo).</summary>
    public void AddSuggestion(string kind, string text, string contact, string appVersion, string windows, string report, string ipHash)
    {
        static string Cut(string? s, int max) => (s ?? "").Length > max ? s![..max] : s ?? "";
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO suggestions (kind, text, contact, app_version, windows, report, ip_hash, created_at) VALUES ($k, $t, $c, $v, $w, $r, $i, $at)";
        cmd.Parameters.AddWithValue("$k", Cut(kind, 20));
        cmd.Parameters.AddWithValue("$t", Cut(text, 4000));
        cmd.Parameters.AddWithValue("$c", Cut(contact, 200));
        cmd.Parameters.AddWithValue("$v", Cut(appVersion, 40));
        cmd.Parameters.AddWithValue("$w", Cut(windows, 120));
        cmd.Parameters.AddWithValue("$r", Cut(report, 20000));
        cmd.Parameters.AddWithValue("$i", ipHash);
        cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public List<SuggestionRow> Suggestions(int limit) => QueryAll(
        "SELECT id, kind, text, contact, app_version, windows, length(report) > 0, created_at FROM suggestions ORDER BY id DESC LIMIT $n",
        r => new SuggestionRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetInt64(6) == 1, DateTimeOffset.Parse(r.GetString(7))),
        ("$n", limit));

    public string? SuggestionReport(long id) => Query("SELECT report FROM suggestions WHERE id = $id", r => r.GetString(0), ("$id", id));
}
