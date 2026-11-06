namespace MBExtreme.Core.Tweaks;

public enum TweakCategory
{
    Performance,
    Gaming,
    Network,
    Privacy,
    Peripherals,
    Boot,
    Visual,
    Storage,
}

public enum TweakRisk
{
    Low,
    Medium,
    High,
}

/// <summary>Um ajuste do Windows: lê o estado, aplica e restaura o valor original.</summary>
public abstract class Tweak
{
    protected Tweak(TweakStore store, string id, TweakCategory category, TweakRisk risk, string title, string description)
    {
        Store = store;
        Id = id;
        Category = category;
        Risk = risk;
        Title = title;
        Description = description;
    }

    protected TweakStore Store { get; }

    public string Id { get; }
    public TweakCategory Category { get; }
    public TweakRisk Risk { get; }
    public string Title { get; }
    public string Description { get; }

    /// <summary>Efeito esperado, em uma frase.</summary>
    public string Effect { get; init; } = "";

    /// <summary>Quando não usar ou o que se perde. Mostrado em destaque.</summary>
    public string Warning { get; init; } = "";

    /// <summary>Entra no botão "Marcar recomendados". Nunca vale para risco alto.</summary>
    public bool Recommended { get; init; }

    public bool RequiresRestart { get; init; }

    /// <summary>Quando definido, o ajuste só aparece se devolver true (ex.: só em PCs com placa de vídeo AMD).</summary>
    public Func<bool>? AppliesTo { get; init; }

    /// <summary>Menor build do Windows em que o ajuste existe (ex.: 22000 = Windows 11). 0 = qualquer.</summary>
    public int MinBuild { get; init; }

    /// <summary>Maior build em que o ajuste ainda vale (ex.: 21999 = só Windows 10). 0 = qualquer.</summary>
    public int MaxBuild { get; init; }

    /// <summary>Build do Windows deste PC (os testes trocam).</summary>
    public static Func<int> WindowsBuild { get; set; } = () => Environment.OSVersion.Version.Build;

    /// <summary>O ajuste existe nesta versão do Windows?</summary>
    public bool IsCompatible
    {
        get
        {
            int build = WindowsBuild();
            return (MinBuild == 0 || build >= MinBuild) && (MaxBuild == 0 || build <= MaxBuild);
        }
    }

    /// <summary>Em que versões do Windows o ajuste vale, em palavras (vazio = todas).</summary>
    public string CompatibilityText => (MinBuild, MaxBuild) switch
    {
        (0, 0) => "",
        ( >= 26100, 0) => "Só no Windows 11 24H2 ou mais novo.",
        ( >= 22621, 0) => "Só no Windows 11 22H2 ou mais novo.",
        ( >= 22000, 0) => "Só no Windows 11.",
        (0, <= 21999) => "Só no Windows 10.",
        _ => $"Só nas builds {MinBuild} a {MaxBuild}.",
    };

    public bool IsAvailable
    {
        get
        {
            if (!IsCompatible) return false;
            try { return AppliesTo?.Invoke() ?? true; }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
        }
    }

    /// <summary>true = aplicado, false = não aplicado, null = não foi possível saber.</summary>
    public abstract bool? IsApplied();

    public abstract void Apply();

    public abstract void Revert();

    /// <summary>Existe valor original guardado, portanto dá para restaurar.</summary>
    public bool HasBackup => Store.HasPrefix(Id + "|");
}
