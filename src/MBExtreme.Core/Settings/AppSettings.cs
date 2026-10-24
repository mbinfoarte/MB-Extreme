using System.Text.Json;

namespace MBExtreme.Core.Settings;

/// <summary>Configurações do usuário, guardadas em %AppData%\MBExtreme\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>Nomes de processos que a limpeza de RAM nunca toca.</summary>
    public List<string> MemoryAllowlist { get; set; } = new();

    /// <summary>Programas (sem .exe) que contam como jogo no Modo Jogo.</summary>
    public List<string> GameProcesses { get; set; } = new();

    /// <summary>Programas (sem .exe) que o Modo Jogo pede para fechar quando um jogo abre.</summary>
    public List<string> BoostCloseApps { get; set; } = new();

    /// <summary>Liga o Modo Jogo sozinho quando um programa da lista de jogos abre.</summary>
    public bool BoostAuto { get; set; }

    public bool BoostSwitchPlan { get; set; } = true;

    public bool BoostCleanMemory { get; set; }

    // ---- Licenciamento (ver docs/LICENCIAMENTO.md)
    /// <summary>Quando verdadeiro, as páginas do plano Pro exigem licença válida. Desligado no beta aberto.</summary>
    public bool LicenseEnforce { get; set; }

    public string LicenseServerUrl { get; set; } = "";

    /// <summary>Chave pública do servidor (SubjectPublicKeyInfo em base64), usada só para conferir a assinatura das licenças.</summary>
    public string LicensePublicKey { get; set; } = "";

    /// <summary>Último momento em que o app rodou (detecta relógio voltado para trás).</summary>
    public long LicenseLastSeenUtcTicks { get; set; }

    // ---- Desligamento programado pelo MB Extreme (para o app lembrar depois de fechar)
    public long ScheduledShutdownUtcTicks { get; set; }

    public bool ScheduledShutdownRestart { get; set; }

    // ---- Limpeza automática de memória
    public bool MemoryAutoClean { get; set; }

    /// <summary>Uso da memória (%) a partir do qual a limpeza automática roda.</summary>
    public int MemoryAutoCleanThreshold { get; set; } = 85;

    // ---- Estúdio de gravação e transmissão
    public int StudioFps { get; set; } = 60;

    /// <summary>0 = tamanho original, 1 = 1080p, 2 = 1440p, 3 = 2160p, 4 = 720p.</summary>
    public int StudioResolution { get; set; }

    /// <summary>Taxa de vídeo em Mbps; 0 = automática.</summary>
    public int StudioBitrateMbps { get; set; }

    public int StudioReplaySeconds { get; set; } = 60;

    public bool StudioReplayBeep { get; set; } = true;

    public bool StudioMp4 { get; set; }

    public bool StudioSystemAudio { get; set; } = true;

    public bool StudioMicAudio { get; set; }

    public double StudioSystemVolume { get; set; } = 1.0;

    public double StudioMicVolume { get; set; } = 1.0;

    public string? StudioMicId { get; set; }

    public string? StudioOutputId { get; set; }

    /// <summary>0 = Twitch, 1 = YouTube, 2 = servidor próprio.</summary>
    public int StudioStreamService { get; set; }

    public string StudioCustomUrl { get; set; } = "rtmp://";

    public bool StudioRememberKey { get; set; }

    /// <summary>Chave de transmissão protegida pelo Windows (DPAPI, só este usuário neste PC). Nunca em texto puro.</summary>
    public string? StudioProtectedKey { get; set; }

    /// <summary>0 = rápido, 1 = equilibrado, 2 = qualidade (esforço do codificador).</summary>
    public int StudioEncoderQuality { get; set; } = 1;

    /// <summary>Qualidade do áudio em kbps (AAC).</summary>
    public int StudioAudioKbps { get; set; } = 160;

    /// <summary>Limite de banda da transmissão em Mbps; 0 = o padrão da plataforma.</summary>
    public int StudioStreamCapMbps { get; set; }

    public bool StudioCameraOn { get; set; }

    public string? StudioCameraName { get; set; }

    /// <summary>0 = superior esquerdo, 1 = superior direito, 2 = inferior esquerdo, 3 = inferior direito.</summary>
    public int StudioCameraCorner { get; set; } = 3;

    public int StudioCameraPercent { get; set; } = 22;

    public bool StudioCameraFrame { get; set; } = true;

    /// <summary>Canal da Twitch ou código da transmissão do YouTube, para abrir o chat.</summary>
    public string StudioChatId { get; set; } = "";

    /// <summary>Atalhos do estúdio ("CtrlShift+F9"); vazio = desligado.</summary>
    public string StudioHotkeySave { get; set; } = "CtrlShift+F9";

    public string StudioHotkeyRecord { get; set; } = "CtrlShift+F10";

    public string StudioHotkeyStream { get; set; } = "CtrlShift+F11";

    /// <summary>ID de suporte remoto deste PC (9 dígitos) e o segredo que autentica a conexão, protegido pelo Windows.</summary>
    public string? RemoteId { get; set; }

    public string? RemoteProtectedSecret { get; set; }

    /// <summary>Ficar disponível para suporte sempre que o MB Extreme estiver aberto (cada acesso ainda exige o seu aceite).</summary>
    public bool RemoteAutoListen { get; set; } = true;

    /// <summary>Exigir também a senha da sessão (mais seguro). Desligado: basta o ID, o aceite e a conferência do código de segurança.</summary>
    public bool RemoteRequirePassword { get; set; }

    public bool RemoteShareAudio { get; set; } = true;

    public List<RemoteRecentEntry> RemoteRecent { get; set; } = new();

    /// <summary>Caminho do OpenRGB.exe (iluminação RGB). Vazio: procura nos lugares comuns.</summary>
    public string OpenRgbPath { get; set; } = "";

    /// <summary>Alerta de temperatura no Monitor em tempo real (°C). 0 = desligado.</summary>
    public int TemperatureAlertC { get; set; }

    /// <summary>Enviar resumos anônimos das sessões de jogo (jogo, faixa de hardware e resultado). Desligado por padrão.</summary>
    public bool ShareAnonymousStats { get; set; }

    /// <summary>Meta de FPS usada no relatório de gargalos.</summary>
    public int BottleneckTargetFps { get; set; } = 60;

    private static string FolderPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme");

    private static string FilePath => Path.Combine(FolderPath, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Arquivo corrompido ou inacessível: começar com os padrões.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sem permissão para gravar: as configurações valem só nesta sessão.
        }
    }
}

public sealed class RemoteRecentEntry
{
    public string Id { get; set; } = "";
    public DateTime LastUsed { get; set; }
}
