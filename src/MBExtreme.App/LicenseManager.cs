using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using MBExtreme.Core.Licensing;

namespace MBExtreme.UI;

/// <summary>
/// Licença do aplicativo: guarda só as duas últimas licenças (protegidas pelo Windows), confere a assinatura, renova em segundo plano
/// e libera ou bloqueia as páginas do plano Pro. Enquanto a aplicação estiver desligada nas configurações (beta aberto), nada é bloqueado.
/// </summary>
internal sealed class LicenseManager
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromHours(6) };
    private readonly string _deviceId = DeviceIdentity.Current();
    private List<string> _tokens = new();

    public LicenseManager() => _timer.Tick += async (_, _) => await RefreshAsync(silent: true);

    public LicenseStatus Status { get; private set; } = new(LicenseState.None, null, "Nenhuma licença neste PC.", null);

    public LicenseConfig Config => LicenseConfig.From(AppServices.Settings);

    public string DeviceId => _deviceId;

    /// <summary>Licença assinada mais recente (usada pelo técnico para provar a conta no acesso remoto).</summary>
    public string? CurrentToken => _tokens.FirstOrDefault();

    /// <summary>Mensagem do último contato com o servidor (renovação, erro de rede...).</summary>
    public string? LastNotice { get; private set; }

    public event EventHandler? Changed;

    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "license.dat");

    public bool IsAllowed(string pageKey) => FeaturePolicy.IsAllowed(pageKey, Status, Config.Enforce, Trial.Active);

    /// <summary>Teste grátis do Pro deste PC (7 dias desde o primeiro uso).</summary>
    public TrialState Trial => _trial ??= TrialClock.Current(AppServices.Settings, MBExtreme.Core.AppInfo.TrialDays);
    private TrialState? _trial;

    /// <summary>Recalcula os dias do teste (o relógio anda).</summary>
    public void RefreshTrial() => _trial = TrialClock.Current(AppServices.Settings, MBExtreme.Core.AppInfo.TrialDays);

    /// <summary>Pro de verdade (assinatura) ou durante o teste grátis.</summary>
    public bool HasProFeatures => !Config.Enforce || LicenseExpiry.IsPro(Status) || Trial.Active;

    /// <summary>Texto curto para o menu lateral.</summary>
    public string Summary => Status.IsValid
        ? (FeaturePolicy.Rank(Status.Plan) > 0 ? (Status.Payload?.Status == "trialing" ? "teste grátis do plano " : "Plano ") + FeaturePolicy.Title(Status.Plan) : "Plano gratuito")
        : Config.Enforce ? "Sem licença" : "Beta aberto";

    private DeviceInfo? _deviceInfo;

    /// <summary>Resumo do PC para a lista "Seus computadores" do site (sem MAC nem serial).</summary>
    public DeviceInfo Device => _deviceInfo ??= DeviceFacts.Collect($"{MBExtreme.Core.AppInfo.Version} ({MBExtreme.Core.Update.AppUpdates.CurrentBuild()})");


    public void Start()
    {
        _tokens = LoadTokens();
        Reevaluate();
        _timer.Start();
        if (Config.Configured && (Status.ShouldRefresh || Status.State == LicenseState.Expired))
            _ = RefreshAsync(silent: true);
    }

    // ---------------------------------------------------------------- avaliação

    private void Reevaluate()
    {
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? lastSeen = AppServices.Settings.LicenseLastSeenUtcTicks is > 0 and var t ? new DateTimeOffset(t, TimeSpan.Zero) : null;

        // usa a licença mais recente que vale; se nenhuma vale, mostra o estado da mais recente
        LicenseStatus? best = null;
        foreach (string token in _tokens)
        {
            var status = LicenseVerifier.Evaluate(token, Config.PublicKey, _deviceId, now, lastSeen);
            best ??= status;
            if (status.IsValid) { best = status; break; }
        }

        Status = best ?? LicenseVerifier.Evaluate(null, Config.PublicKey, _deviceId, now);

        if (lastSeen is null || now > lastSeen)
        {
            AppServices.Settings.LicenseLastSeenUtcTicks = now.UtcTicks;
            AppServices.Settings.Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- armazenamento (só as duas últimas)

    private static List<string> LoadTokens()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<string>();
            var stored = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath)) ?? new List<string>();
            return stored.Select(SecretStore.Unprotect).Where(t => !string.IsNullOrEmpty(t)).Select(t => t!).Take(LicenseHistory.Keep).ToList();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new List<string>();
        }
    }

    private void SaveTokens()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            if (_tokens.Count == 0) { if (File.Exists(FilePath)) File.Delete(FilePath); return; }
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_tokens.Select(SecretStore.Protect).ToList()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            LastNotice = "Não foi possível guardar a licença neste PC: " + ex.Message;
        }
    }

    private void Accept(string token)
    {
        _tokens = LicenseHistory.Push(_tokens, token).ToList();
        SaveTokens();
        Reevaluate();
    }

    // ---------------------------------------------------------------- servidor

    private LicenseClient? Client() => Config.Configured ? new LicenseClient(Http, Config.ServerUrl) : null;

    /// <summary>Abre o navegador para o usuário entrar e aprovar este PC. <paramref name="onCode"/> recebe o código para mostrar na tela.</summary>
    public async Task<(bool Ok, string Message)> LoginAsync(Action<DeviceCode> onCode, CancellationToken token)
    {
        var client = Client();
        if (client is null) return (false, "O servidor de licenças não está configurado.");
        try
        {
            var result = await LicenseFlow.LoginAsync(client, _deviceId, DeviceIdentity.FriendlyName(), code =>
            {
                onCode(code);
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(code.VerificationUri) { UseShellExecute = true }); }
                catch (System.ComponentModel.Win32Exception) { /* o usuário também vê o endereço na tela */ }
            }, token, info: Device);

            if (result.State != PollState.Approved || result.License is null) return (false, result.Message ?? "O login não foi concluído.");

            var check = LicenseVerifier.Evaluate(result.License, Config.PublicKey, _deviceId, DateTimeOffset.UtcNow);
            if (check.State is LicenseState.Invalid or LicenseState.WrongDevice)
                return (false, "A licença recebida não é autêntica para este programa. Confira a chave pública do servidor.");

            Accept(result.License);
            LastNotice = null;
            return (true, $"Ativado: {Summary}.");
        }
        catch (LicenseNetworkException ex)
        {
            return (false, ex.Message);
        }
        catch (OperationCanceledException)
        {
            return (false, "Login cancelado.");
        }
    }

    /// <summary>Pede uma licença nova ao servidor. <paramref name="silent"/>: não mostra erro de rede (uso offline é normal).</summary>
    public async Task<(bool Ok, string Message)> RefreshAsync(bool silent = false)
    {
        var client = Client();
        if (client is null || _tokens.Count == 0) return (false, "Nada para renovar.");
        try
        {
            var result = await client.RefreshAsync(_tokens[0], _deviceId, CancellationToken.None, Device);
            switch (result.State)
            {
                case RefreshState.Renewed when result.License is not null:
                    Accept(result.License);
                    LastNotice = null;
                    return (true, $"Licença renovada: {Summary}.");
                case RefreshState.Revoked:
                    _tokens.Clear();
                    SaveTokens();
                    Reevaluate();
                    LastNotice = result.Message;
                    return (false, result.Message ?? "Este PC não está mais ativado.");
                default:
                    LastNotice = result.Message;
                    return (false, result.Message ?? "Não foi possível renovar agora.");
            }
        }
        catch (LicenseNetworkException ex)
        {
            LastNotice = silent ? "Sem conexão com o servidor; usando a licença guardada." : ex.Message;
            Changed?.Invoke(this, EventArgs.Empty);
            return (false, ex.Message);
        }
    }

    /// <summary>Libera a vaga deste PC no servidor (se houver internet) e apaga a licença local.</summary>
    public async Task<string> SignOutAsync()
    {
        string notice = "Licença removida deste PC.";
        var client = Client();
        if (client is not null && _tokens.Count > 0)
        {
            try { await client.DeactivateAsync(_tokens[0], _deviceId, CancellationToken.None); }
            catch (LicenseNetworkException) { notice += " Sem internet: remova este PC também em Minha conta, no site."; }
        }

        _tokens.Clear();
        SaveTokens();
        Reevaluate();
        return notice;
    }

    public void Shutdown() => _timer.Stop();
}
