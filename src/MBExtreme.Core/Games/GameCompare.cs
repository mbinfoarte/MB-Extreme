using System.Diagnostics;
using System.Text.Json;

namespace MBExtreme.Core.Games;

public enum RowState { Ok, Different, Info }

/// <summary>Uma linha da tabela "valor atual × recomendado" de um jogo. CanFix = o botão Otimizar muda esta linha.</summary>
public sealed record SettingRow(string Group, string Title, string Current, string Recommended, RowState State, string Note, bool CanFix);

/// <summary>Estado atual lido do PC para um jogo (null = não foi possível ler).</summary>
public sealed record GameSnapshot(
    IReadOnlyDictionary<string, bool?> TweakStates,
    IReadOnlyDictionary<string, string> TweakTitles,
    bool? HighPriority,
    GpuPreferenceMode? GpuPreference,
    bool DriverAvailable,
    DriverValue? PowerMode,
    DriverValue? PreRenderLimit,
    string? DlssVersion,
    IReadOnlyDictionary<uint, DriverValue?>? CatalogValues = null);

public static class GameCompare
{
    public static IReadOnlyList<SettingRow> Build(GameProfile profile, GpuMaker maker, GameSnapshot snap, IReadOnlyList<DriverSetting>? catalog = null, bool hasReflex = false)
    {
        var rows = new List<SettingRow>();

        foreach (string id in profile.TweakIds)
        {
            if (!snap.TweakTitles.TryGetValue(id, out string? title)) continue; // não existe ou não vale para este PC
            bool? on = snap.TweakStates.GetValueOrDefault(id);
            rows.Add(new SettingRow("Windows", title, on switch { true => "Ligado", false => "Desligado", _ => "Não foi possível ler" }, "Ligado",
                on == true ? RowState.Ok : on == false ? RowState.Different : RowState.Info, "", on == false));
        }

        rows.Add(new SettingRow("Windows", "Prioridade de CPU do jogo",
            snap.HighPriority switch { true => "Alta", false => "Normal", _ => "Não foi possível ler" }, "Alta",
            snap.HighPriority == true ? RowState.Ok : snap.HighPriority == false ? RowState.Different : RowState.Info,
            "Ligada pelo botão \"Prioridade alta de CPU\"; vale toda vez que o jogo abre.", false));

        var plan = DriverPlan.For(profile, maker, hasReflex);
        if (plan.GpuPreference is { } wantGpu && snap.GpuPreference is { } haveGpu)
            rows.Add(new SettingRow("Placa de vídeo", "Placa de vídeo usada pelo jogo", GpuPreference.Name(haveGpu), GpuPreference.Name(wantGpu),
                haveGpu == wantGpu ? RowState.Ok : RowState.Different,
                "Mesma opção de Configurações do Windows > Tela > Elementos gráficos. Importa em notebooks e PCs com vídeo integrado e dedicado.", haveGpu != wantGpu));

        if (maker == GpuMaker.Nvidia)
        {
            if (!snap.DriverAvailable)
                rows.Add(new SettingRow("Driver NVIDIA", "Configurações do driver", "Indisponível", "—", RowState.Info,
                    "Não foi possível falar com o driver da NVIDIA (driver ausente ou sem permissão). Execute como administrador.", false));
            else
            {
                if (plan.PowerMode is { } wantPower)
                    rows.Add(DriverRow("Modo de gerenciamento de energia", snap.PowerMode, wantPower, DriverPowerModes.Name,
                        "Impede a placa de baixar o clock no meio do jogo."));
                if (plan.PreRenderLimit is { } wantPre)
                    rows.Add(DriverRow("Quadros pré-renderizados (baixa latência)", snap.PreRenderLimit, wantPre, v => v == 0 ? "Padrão do driver" : v.ToString(),
                        "Valor 1 reduz o atraso entre o clique e a imagem. Se o jogo tiver NVIDIA Reflex, prefira ligar o Reflex dentro dele."));
                foreach (var item in catalog ?? Array.Empty<DriverSetting>())
                {
                    DriverValue? now = snap.CatalogValues is not null && snap.CatalogValues.TryGetValue(item.Id, out var v) ? v : null;
                    var row = DriverRow(item.Title + (item.Advanced ? " (avançado)" : ""), now, item.Value, x => x == item.Value ? item.ValueText : "Valor 0x" + x.ToString("X"), item.Why);
                    rows.Add(row);
                }
            }
        }

        rows.Add(DlssRow(maker, snap.DlssVersion));
        return rows;
    }

    private static SettingRow DriverRow(string title, DriverValue? now, uint want, Func<uint, string> name, string note)
    {
        string current = now is null ? "Não foi possível ler" : name(now.Value) + (now.Specific ? "" : " (padrão global)");
        RowState state = now is null ? RowState.Info : now.Value == want ? RowState.Ok : RowState.Different;
        return new SettingRow("Driver NVIDIA", title, current, name(want), state, note, state == RowState.Different);
    }

    private static SettingRow DlssRow(GpuMaker maker, string? version)
    {
        if (maker != GpuMaker.Nvidia) return new SettingRow("Upscaler", "DLSS", "Só para placas NVIDIA GeForce RTX", "—", RowState.Info, "", false);
        if (version is null) return new SettingRow("Upscaler", "DLSS no jogo", "Não encontrado na pasta", "—", RowState.Info, "O jogo não usa DLSS, ou carrega o componente de outra pasta.", false);
        var (label, state, note) = DlssInfo.Evaluate(version);
        return new SettingRow("Upscaler", "DLSS no jogo", label, "Versão recente", state, note, false);
    }
}

/// <summary>Descobre a versão do DLSS que o jogo traz (arquivo nvngx_dlss.dll). Só leitura: o MB Extreme não troca esse arquivo.</summary>
public static class DlssInfo
{
    public static string? Find(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath)) return null;
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 5, IgnoreInaccessible = true };
            string? dll = Directory.EnumerateFiles(installPath, "nvngx_dlss.dll", options).FirstOrDefault();
            if (dll is null) return null;
            string? v = FileVersionInfo.GetVersionInfo(dll).FileVersion;
            return string.IsNullOrWhiteSpace(v) ? "desconhecida" : v.Replace(',', '.').Replace(" ", "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>DLSS 2 e 3.x até 3.6 são antigos; 3.7 em diante melhora a imagem; a série 310 em diante é o DLSS 4.</summary>
    public static (string Label, RowState State, string Note) Evaluate(string version)
    {
        var parts = version.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], out int major) || !int.TryParse(parts[1], out int minor))
            return ("DLSS (versão " + version + ")", RowState.Info, "Não consegui comparar esta versão.");
        if (major >= 310) return ($"DLSS 4 · {version}", RowState.Ok, "Versão recente.");
        if (major == 3 && minor >= 7) return ($"DLSS 3.{minor} · {version}", RowState.Info, "Boa. A NVIDIA App pode oferecer a versão mais nova em \"Substituição do DLSS\".");
        return ($"DLSS {major} · {version}", RowState.Different,
            "Versão antiga. Em Gráficos > Programas da NVIDIA App, use \"Substituição do DLSS\" para usar a mais recente. O MB Extreme não troca o arquivo do jogo (pode acionar antitrapaça).");
    }
}

public sealed record DriverApplyResult(IReadOnlyList<string> Applied, IReadOnlyList<string> Failed);

/// <summary>
/// Aplica configurações de driver e de placa de vídeo para um jogo, guardando o valor de antes (primeira vez) para desfazer.
/// Desfazer devolve o que existia: o valor anterior, ou remove a configuração do perfil do jogo se não existia.
/// </summary>
public sealed class GameDriverService
{
    private sealed class Saved
    {
        public string Exe { get; set; } = "";
        public string ExePath { get; set; } = "";
        public Dictionary<uint, uint?> Driver { get; set; } = new();
        public GpuPreferenceMode? Gpu { get; set; }
    }

    private readonly IDriverProfileStore _driver;
    private readonly IGpuPreferenceStore _gpu;
    private readonly string _path;
    private readonly object _lock = new();

    public GameDriverService(IDriverProfileStore driver, IGpuPreferenceStore gpu, string backupPath)
    {
        _driver = driver;
        _gpu = gpu;
        _path = backupPath;
    }

    public bool HasBackup(string gameId) { lock (_lock) return Load().ContainsKey(gameId); }

    public DriverApplyResult Apply(string gameId, string exeName, string? exePath, DriverPlan plan, IReadOnlyList<DriverSetting>? extra = null)
    {
        var applied = new List<string>();
        var failed = new List<string>();
        lock (_lock)
        {
            var all = Load();
            var saved = all.TryGetValue(gameId, out var s) ? s : new Saved();
            saved.Exe = exeName;
            if (exePath is not null) saved.ExePath = exePath;

            void Driver(uint id, uint? want, string label)
            {
                if (want is not { } value) return;
                if (!_driver.Available) { failed.Add($"{label}: driver indisponível"); return; }
                var now = _driver.Read(exeName, id);
                if (now is { Specific: true } && now.Value == value) return;
                if (!saved.Driver.ContainsKey(id)) saved.Driver[id] = now is { Specific: true } ? now.Value : null;
                string? error = _driver.Write(exeName, id, value);
                if (error is null) applied.Add(label); else failed.Add($"{label}: {error}");
            }

            Driver(DriverSettingIds.PowerMode, plan.PowerMode, DriverCatalog.TitleOf(DriverSettingIds.PowerMode));
            Driver(DriverSettingIds.PreRenderLimit, plan.PreRenderLimit, DriverCatalog.TitleOf(DriverSettingIds.PreRenderLimit));
            foreach (var item in extra ?? Array.Empty<DriverSetting>()) Driver(item.Id, item.Value, item.Title);

            if (plan.GpuPreference is { } gpu && !string.IsNullOrEmpty(saved.ExePath))
            {
                try
                {
                    var now = _gpu.Get(saved.ExePath);
                    if (now != gpu)
                    {
                        saved.Gpu ??= now;
                        _gpu.Set(saved.ExePath, gpu);
                        applied.Add("Placa de vídeo do jogo");
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
                {
                    failed.Add("Placa de vídeo do jogo: " + ex.Message);
                }
            }

            if (saved.Driver.Count > 0 || saved.Gpu is not null) all[gameId] = saved;
            Save(all);
        }
        return new DriverApplyResult(applied, failed);
    }

    /// <summary>
    /// Volta só estas configurações ao que eram antes do MB Extreme (o valor guardado, ou remove do perfil do jogo se não havia nada).
    /// Usado quando o usuário escolhe "Padrão do jogo" numa opção (ex.: DLSS).
    /// </summary>
    public DriverApplyResult Reset(string gameId, string exeName, IReadOnlyList<uint> ids)
    {
        var reverted = new List<string>();
        var failed = new List<string>();
        if (ids.Count == 0) return new DriverApplyResult(reverted, failed);
        if (!_driver.Available) return new DriverApplyResult(reverted, new[] { "driver NVIDIA indisponível" });
        lock (_lock)
        {
            var all = Load();
            all.TryGetValue(gameId, out var saved);
            foreach (var id in ids.Distinct())
            {
                string? error;
                if (saved is not null && saved.Driver.TryGetValue(id, out var old))
                {
                    error = old is { } v ? _driver.Write(exeName, id, v) : _driver.Remove(exeName, id);
                    if (error is null) saved.Driver.Remove(id);
                }
                else
                {
                    // nunca mexemos nela: só remove se estiver no perfil do jogo
                    if (_driver.Read(exeName, id) is not { Specific: true }) continue;
                    error = _driver.Remove(exeName, id);
                }
                if (error is null) reverted.Add(DriverCatalog.TitleOf(id)); else failed.Add(error);
            }
            if (saved is not null)
            {
                if (saved.Driver.Count == 0 && saved.Gpu is null) all.Remove(gameId); else all[gameId] = saved;
                Save(all);
            }
        }
        return new DriverApplyResult(reverted, failed);
    }

    /// <summary>Valor atual no perfil do próprio jogo (null = o jogo não tem essa configuração).</summary>
    public uint? ReadSpecific(string exeName, uint id) => _driver.Available && _driver.Read(exeName, id) is { Specific: true } v ? v.Value : null;

    public bool DriverAvailable => _driver.Available;

    public DriverApplyResult Undo(string gameId)
    {
        var reverted = new List<string>();
        var failed = new List<string>();
        lock (_lock)
        {
            var all = Load();
            if (!all.TryGetValue(gameId, out var saved)) return new DriverApplyResult(reverted, failed);

            foreach (var (id, old) in saved.Driver.ToList())
            {
                string? error = old is { } v ? _driver.Write(saved.Exe, id, v) : _driver.Remove(saved.Exe, id);
                if (error is null) { reverted.Add(DriverCatalog.TitleOf(id)); saved.Driver.Remove(id); }
                else failed.Add(error);
            }

            if (saved.Gpu is { } gpu && !string.IsNullOrEmpty(saved.ExePath))
            {
                try
                {
                    _gpu.Set(saved.ExePath, gpu);
                    reverted.Add("Placa de vídeo do jogo");
                    saved.Gpu = null;
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
                {
                    failed.Add("Placa de vídeo do jogo: " + ex.Message);
                }
            }

            if (saved.Driver.Count == 0 && saved.Gpu is null) all.Remove(gameId);
            Save(all);
        }
        return new DriverApplyResult(reverted, failed);
    }

    private Dictionary<string, Saved> Load()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<Dictionary<string, Saved>>(File.ReadAllText(_path)) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return new(); }
    }

    private void Save(Dictionary<string, Saved> all)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        File.WriteAllText(_path, JsonSerializer.Serialize(all));
    }
}

public static class GameFiles
{
    /// <summary>Caminho completo do executável do jogo (o adicionado à mão já traz; os das lojas são procurados na pasta de instalação).</summary>
    public static string? FindExePath(InstalledGame game)
    {
        try
        {
            if (game.Launcher == GameLauncher.Manual && game.LaunchTarget is { } target && File.Exists(target)) return target;
            if (game.ExeName is { } name && Directory.Exists(game.InstallPath))
                return Directory.EnumerateFiles(game.InstallPath, name, new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true }).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }
}
