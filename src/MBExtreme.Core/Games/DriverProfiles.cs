namespace MBExtreme.Core.Games;

/// <summary>Valor de uma configuração de driver. Specific = definido no perfil do próprio jogo (senão vem do perfil global).</summary>
public sealed record DriverValue(uint Value, bool Specific);

/// <summary>Acesso aos perfis de driver por aplicativo (hoje: NVIDIA, pela interface NVAPI DRS).</summary>
public interface IDriverProfileStore
{
    bool Available { get; }
    DriverValue? Read(string exeName, uint settingId);

    /// <summary>Grava o valor no perfil do jogo (cria o perfil se preciso). Devolve o texto do erro, ou null se deu certo.</summary>
    string? Write(string exeName, uint settingId, uint value);

    /// <summary>Remove o valor do perfil do jogo, voltando ao padrão do driver. Devolve o erro, ou null.</summary>
    string? Remove(string exeName, uint settingId);
}

/// <summary>Configurações de driver NVIDIA conhecidas (identificadores do cabeçalho público NvApiDriverSettings.h).</summary>
public static class DriverSettingIds
{
    public const uint PowerMode = 0x1057EB71;       // Modo de gerenciamento de energia
    public const uint PreRenderLimit = 0x007BA09E;  // Quadros pré-renderizados (baixa latência)
}

public static class DriverPowerModes
{
    public static string Name(uint value) => value switch
    {
        0 => "Adaptativo", 1 => "Preferir desempenho máximo", 2 => "Controlado pelo driver", 3 => "Desempenho consistente",
        4 => "Preferir menor consumo", 5 => "Ideal", _ => "Valor " + value,
    };
}

/// <summary>O que a otimização quer deixar no driver e na escolha de GPU de um jogo. Null = não mexer.</summary>
public sealed record DriverPlan(uint? PowerMode, uint? PreRenderLimit, GpuPreferenceMode? GpuPreference)
{
    public bool IsEmpty => PowerMode is null && PreRenderLimit is null && GpuPreference is null;

    public static DriverPlan For(GameProfile profile, GpuMaker maker) => profile.Key switch
    {
        "competitive" => new DriverPlan(maker == GpuMaker.Nvidia ? 1u : null, maker == GpuMaker.Nvidia ? 1u : null, GpuPreferenceMode.HighPerformance),
        "openworld" or "racing" => new DriverPlan(maker == GpuMaker.Nvidia ? 1u : null, null, GpuPreferenceMode.HighPerformance),
        "app" => new DriverPlan(maker == GpuMaker.Nvidia ? 1u : null, null, GpuPreferenceMode.HighPerformance),
        _ => new DriverPlan(null, null, null),
    };
}
