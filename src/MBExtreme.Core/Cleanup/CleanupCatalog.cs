namespace MBExtreme.Core.Cleanup;

/// <summary>Pastas que o Windows e os navegadores recriam sozinhos. Nunca inclui cookies, senhas, histórico ou documentos.</summary>
public static class CleanupCatalog
{
    public static IReadOnlyList<CleanupCategory> Create()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string userTemp = Path.GetTempPath();
        var day = TimeSpan.FromDays(1);

        return new[]
        {
            new CleanupCategory("temp-user", "Arquivos temporários do usuário",
                "Sobras de instaladores e programas na pasta Temp.",
                new[] { userTemp }, day,
                Note: "Só arquivos com mais de 1 dia, para não atrapalhar o que está em uso."),

            new CleanupCategory("temp-windows", "Arquivos temporários do Windows",
                "Pasta Temp do sistema.",
                new[] { Path.Combine(windows, "Temp") }, day,
                Note: "Só arquivos com mais de 1 dia."),

            new CleanupCategory("prefetch", "Prefetch",
                "Lista de programas abertos recentemente que o Windows usa para abrir mais rápido.",
                new[] { Path.Combine(windows, "Prefetch") },
                Note: "O Windows recria sozinho. Os primeiros programas abertos depois da limpeza podem demorar um pouco mais."),

            new CleanupCategory("crash-dumps", "Relatórios de erro e despejos de memória",
                "Arquivos de diagnóstico de travamentos.",
                new[]
                {
                    Path.Combine(local, "CrashDumps"),
                    Path.Combine(windows, "Minidump"),
                    Path.Combine(programData, "Microsoft", "Windows", "WER", "ReportQueue"),
                    Path.Combine(programData, "Microsoft", "Windows", "WER", "ReportArchive"),
                }),

            new CleanupCategory("browser-chromium", "Cache dos navegadores (Chrome, Edge, Brave, Opera)",
                "Imagens e arquivos temporários de sites. Não apaga cookies, senhas nem histórico.",
                ChromiumCaches(local),
                Note: "Sites podem carregar um pouco mais devagar na primeira visita."),

            new CleanupCategory("browser-firefox", "Cache do Firefox",
                "Arquivos temporários de sites. Não apaga cookies, senhas nem histórico.",
                FirefoxCaches(local)),

            new CleanupCategory("thumbnails", "Cache de miniaturas",
                "Miniaturas de imagens e vídeos do Explorador de Arquivos.",
                new[] { Path.Combine(local, "Microsoft", "Windows", "Explorer") },
                Note: "As miniaturas são geradas de novo ao abrir as pastas.",
                FilePattern: "thumbcache_*.db"),

            new CleanupCategory("shader-cache", "Cache de shaders da placa de vídeo (NVIDIA, AMD e DirectX)",
                "Shaders já compilados dos jogos. Limpar resolve travadas depois de trocar o driver da placa de vídeo.",
                new[]
                {
                    Path.Combine(local, "NVIDIA", "DXCache"),
                    Path.Combine(local, "NVIDIA", "GLCache"),
                    Path.Combine(local, "AMD", "DxCache"),
                    Path.Combine(local, "D3DSCache"),
                },
                DefaultSelected: false,
                Note: "Os jogos recompilam os shaders na próxima abertura; a primeira partida pode ter travadas."),

            new CleanupCategory("windows-update", "Cache de download do Windows Update",
                "Arquivos de atualização já baixados.",
                new[] { Path.Combine(windows, "SoftwareDistribution", "Download") },
                DefaultSelected: false,
                Note: "Se houver uma atualização pendente, o Windows baixa de novo."),
        };
    }

    private static IReadOnlyList<string> ChromiumCaches(string local)
    {
        var profiles = new[]
        {
            Path.Combine(local, "Google", "Chrome", "User Data"),
            Path.Combine(local, "Microsoft", "Edge", "User Data"),
            Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"),
            Path.Combine(local, "Opera Software", "Opera Stable"),
        };

        var roots = new List<string>();
        foreach (var profile in profiles)
        {
            if (!Directory.Exists(profile)) continue;
            roots.Add(Path.Combine(profile, "Default", "Cache"));
            roots.Add(Path.Combine(profile, "Default", "Code Cache"));
            roots.Add(Path.Combine(profile, "Default", "GPUCache"));
            roots.Add(Path.Combine(profile, "Cache"));
        }

        return roots;
    }

    private static IReadOnlyList<string> FirefoxCaches(string local)
    {
        string profiles = Path.Combine(local, "Mozilla", "Firefox", "Profiles");
        if (!Directory.Exists(profiles)) return Array.Empty<string>();
        try
        {
            return Directory.EnumerateDirectories(profiles).Select(p => Path.Combine(p, "cache2")).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
