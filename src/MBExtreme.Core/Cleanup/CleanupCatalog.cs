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
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
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
                DefaultSelected: false,
                Note: "Não recomendado no dia a dia: o Windows usa estes arquivos para abrir os programas mais rápido e recria sozinho. Limpe só se um programa estiver abrindo com erro."),

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

            new CleanupCategory("discord-cache", "Cache do Discord",
                "Imagens, figurinhas e vídeos que o Discord guardou. Não apaga mensagens, login nem configurações.",
                DiscordCaches(roaming),
                Note: "Feche o Discord antes. As imagens são baixadas de novo quando você abre as conversas."),

            new CleanupCategory("steam-cache", "Cache e registros do Steam",
                "Páginas da loja guardadas, registros (logs) e relatórios de erro do Steam. Não apaga jogos nem saves.",
                SteamCaches(local),
                DefaultSelected: false,
                Note: "Feche o Steam antes. A loja e a biblioteca podem demorar um pouco mais na primeira abertura."),

            new CleanupCategory("recent-docs", "Lista de documentos recentes",
                "Histórico de arquivos abertos que aparece no Explorador, no Iniciar e nas listas de atalhos da barra de tarefas.",
                new[]
                {
                    Path.Combine(roaming, "Microsoft", "Windows", "Recent"),
                },
                DefaultSelected: false,
                Note: "Apaga só os atalhos da lista; os arquivos continuam onde estão. Itens fixados nas listas de atalhos também saem."),

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

    /// <summary>Discord estável, PTB e Canary: só as pastas de cache (Cache, Code Cache e GPUCache).</summary>
    public static IReadOnlyList<string> DiscordCaches(string roaming)
    {
        var roots = new List<string>();
        foreach (string app in new[] { "discord", "discordptb", "discordcanary" })
        {
            string folder = Path.Combine(roaming, app);
            if (!Directory.Exists(folder)) continue;
            roots.Add(Path.Combine(folder, "Cache"));
            roots.Add(Path.Combine(folder, "Code Cache"));
            roots.Add(Path.Combine(folder, "GPUCache"));
        }
        return roots;
    }

    /// <summary>Pasta do Steam (do registro, ou a padrão) e as pastas de cache e registros. Nunca steamapps nem userdata.</summary>
    public static IReadOnlyList<string> SteamCaches(string local, string? steamPath = null)
    {
        steamPath ??= SteamFolder();
        var roots = new List<string> { Path.Combine(local, "Steam", "htmlcache") };
        if (steamPath is not null && Directory.Exists(steamPath))
        {
            roots.Add(Path.Combine(steamPath, "appcache", "httpcache"));
            roots.Add(Path.Combine(steamPath, "logs"));
            roots.Add(Path.Combine(steamPath, "dumps"));
        }
        return roots;
    }

    private static string? SteamFolder()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key?.GetValue("SteamPath") is string path && path.Length > 0) return path.Replace('/', '\\');
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { }
        }
        string x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return x86.Length > 0 ? Path.Combine(x86, "Steam") : null;
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
