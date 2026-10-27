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

    /// <summary>Câmeras do estúdio (até três). Substitui as opções de uma câmera só, que ficam apenas para migrar.</summary>
    public List<StudioCameraSetting> StudioCameras { get; set; } = new();

    // ---- cenas (cada cena guarda as suas fontes)
    public List<StudioSceneSetting> StudioScenes { get; set; } = new();

    public int StudioSceneIndex { get; set; }

    /// <summary>Modo estúdio: escolher uma cena só mostra na prévia; ela entra no ar com "Transição".</summary>
    public bool StudioSceneStudioMode { get; set; }

    /// <summary>Trocar de cena sem reiniciar (todas as cenas ficam montadas; pesa um pouco mais).</summary>
    public bool StudioScenesLive { get; set; } = true;

    /// <summary>0 = cortar, 1 = esmaecer.</summary>
    public int StudioSceneTransition { get; set; }

    /// <summary>Mostra a imagem "No ar" (uma cópia pequena do vídeo final) na tela do estúdio.</summary>
    public bool StudioProgramView { get; set; } = true;

    // ---- Pix (QR estático "apoie a live")
    public bool StudioPixOn { get; set; }

    public string StudioPixKey { get; set; } = "";

    public string StudioPixName { get; set; } = "";

    public string StudioPixCity { get; set; } = "";

    /// <summary>Valor fixo em reais; 0 = quem paga escolhe.</summary>
    public decimal StudioPixAmount { get; set; }

    public string StudioPixNote { get; set; } = "Apoie a live";

    public int StudioPixCorner { get; set; } = 2;

    public int StudioPixPercent { get; set; } = 14;

    /// <summary>De quantos em quantos minutos o QR aparece; 0 = fica sempre na tela.</summary>
    public int StudioPixEveryMinutes { get; set; } = 5;

    public int StudioPixShowSeconds { get; set; } = 20;

    // ---- clipes automáticos de lances (pelo som do jogo)
    public bool StudioAutoClip { get; set; }

    /// <summary>0 = baixa, 1 = média, 2 = alta.</summary>
    public int StudioAutoClipSensitivity { get; set; } = 1;

    /// <summary>Quantos segundos esperar depois do lance antes de salvar (para o clipe pegar a comemoração).</summary>
    public int StudioAutoClipAfterSeconds { get; set; } = 6;

    /// <summary>Mostra um aviso pequeno por cima do jogo quando um clipe é salvo (funciona com jogo em janela ou sem bordas).</summary>
    public bool StudioClipToast { get; set; } = true;

    // ---- identidade do canal e camadas (entrada, faixa com o nome, logo)
    public string StudioChannelName { get; set; } = "";

    public string StudioPresenter { get; set; } = "";

    public string StudioTagline { get; set; } = "";

    /// <summary>Logo do canal já copiado para a pasta do programa (PNG).</summary>
    public string? StudioChannelLogo { get; set; }

    /// <summary>0 amarelo, 1 branco, 2 azul, 3 vermelho, 4 verde, 5 roxo.</summary>
    public int StudioChannelTheme { get; set; }

    public bool StudioIntroOn { get; set; }

    public int StudioIntroStart { get; set; } = 1;

    public int StudioIntroSeconds { get; set; } = 8;

    public bool StudioLowerOn { get; set; }

    public int StudioLowerStart { get; set; } = 15;

    /// <summary>De quantos em quantos minutos a faixa volta; 0 = aparece só uma vez.</summary>
    public int StudioLowerEveryMinutes { get; set; } = 10;

    public int StudioLowerSeconds { get; set; } = 8;

    public int StudioLowerCorner { get; set; } = 2;

    public bool StudioLogoOn { get; set; }

    public int StudioLogoCorner { get; set; } = 1;

    public int StudioLogoPercent { get; set; } = 8;

    // ---- filtros da imagem principal
    public double StudioFilterBrightness { get; set; }

    public double StudioFilterContrast { get; set; } = 1;

    public double StudioFilterSaturation { get; set; } = 1;

    public double StudioFilterGamma { get; set; } = 1;

    public double StudioFilterSharpness { get; set; }

    public int StudioFilterDenoise { get; set; }

    public int StudioFilterVignette { get; set; }

    /// <summary>Tratamento da voz do microfone: 0 = natural, 1 = reduzir ruído, 2 = voz de estúdio.</summary>
    public int StudioVoiceEffect { get; set; }

    // tratamento profissional do áudio (ao vivo)
    public bool StudioAudHighPass { get; set; }
    public bool StudioAudGate { get; set; }
    public double StudioAudGateDb { get; set; } = -45;
    public bool StudioAudComp { get; set; }
    public double StudioAudCompDb { get; set; } = -18;
    public double StudioAudCompRatio { get; set; } = 3;
    public double StudioAudMakeupDb { get; set; }
    public bool StudioAudLimiter { get; set; } = true;
    public double StudioAudCeilingDb { get; set; } = -1;

    // ---- Pix com o QR do próprio banco
    public bool StudioPixUseOwn { get; set; }

    public string? StudioPixOwnImage { get; set; }

    // ---- vídeo vertical (Shorts, Reels, TikTok)
    public bool StudioVerticalOn { get; set; }

    public int StudioVerticalFormat { get; set; }

    public bool StudioVerticalSplit { get; set; } = true;

    public int StudioVerticalTopPercent { get; set; } = 55;

    public bool StudioVerticalDivider { get; set; } = true;

    /// <summary>0 = tela, 1 = câmera, 2 = arquivo.</summary>
    public int StudioVerticalTopKind { get; set; }

    public int StudioVerticalTopFit { get; set; } = 1;

    public int StudioVerticalTopFocusX { get; set; } = 50;

    public int StudioVerticalTopFocusY { get; set; } = 50;

    public int StudioVerticalTopZoom { get; set; } = 100;

    public string? StudioVerticalTopSource { get; set; }

    public int StudioVerticalBottomKind { get; set; } = 1;

    public int StudioVerticalBottomFit { get; set; }

    public int StudioVerticalBottomFocusX { get; set; } = 50;

    public int StudioVerticalBottomFocusY { get; set; } = 50;

    public int StudioVerticalBottomZoom { get; set; } = 100;

    public string? StudioVerticalBottomSource { get; set; }

    /// <summary>Gravar também uma cópia vertical, ao mesmo tempo que o vídeo normal.</summary>
    public bool StudioVerticalCopy { get; set; }

    // ---- criador de vídeos curtos (Shorts, Reels)
    public int ShortsFormat { get; set; }

    /// <summary>0 = inteira com fundo desfocado, 1 = preencher, 2 = dividida (jogo em cima e recorte embaixo).</summary>
    public int ShortsMode { get; set; }

    public int ShortsFocus { get; set; } = 50;

    /// <summary>Canto do vídeo que aparece aproximado embaixo: 0 = sup. esquerdo, 1 = sup. direito, 2 = inf. esquerdo, 3 = inf. direito.</summary>
    public int ShortsCorner { get; set; } = 3;

    public int ShortsFps { get; set; } = 30;

    public int ShortsMbps { get; set; } = 12;

    /// <summary>Chave gratuita do SteamGridDB (steamgriddb.com), para achar capas de jogos que não são da Steam.</summary>
    public string SteamGridDbKey { get; set; } = "";

    // ---- legendas automáticas ao vivo
    public bool StudioCaptionOn { get; set; }

    /// <summary>0 = microfone, 1 = som do PC.</summary>
    public int StudioCaptionSource { get; set; }

    /// <summary>0 = pequena, 1 = média, 2 = grande.</summary>
    public int StudioCaptionSizeIndex { get; set; } = 1;

    public int StudioCaptionPosition { get; set; }

    public int StudioCaptionColor { get; set; }

    public bool StudioCaptionUpper { get; set; } = true;

    // ---- ranking anônimo (só com consentimento)
    public bool RankingOptIn { get; set; }

    /// <summary>Código aleatório da instalação para o ranking. Nasce quando a pessoa aceita e é trocado ao apagar os dados.</summary>
    public string RankingInstallId { get; set; } = "";

    // ---- chat
    public bool StudioChatAuto { get; set; } = true;

    // ---- estatísticas da transmissão (espectadores, visualizações...)
    public bool StudioStatsOn { get; set; } = true;

    /// <summary>Chave de API do YouTube (do Google Cloud do próprio usuário), protegida pelo Windows. Nunca em texto puro.</summary>
    public string? StudioYouTubeKeyProtected { get; set; }

    public string StudioTwitchClientId { get; set; } = "";

    // ---- envio de vídeos para o YouTube (projeto do Google Cloud da própria pessoa)
    public string StudioYtClientId { get; set; } = "";

    public string? StudioYtSecretProtected { get; set; }

    /// <summary>Token de renovação do Google, protegido pelo Windows (só este usuário lê).</summary>
    public string? StudioYtRefreshProtected { get; set; }

    /// <summary>0 = privado, 1 = não listado, 2 = público.</summary>
    public int StudioYtPrivacy { get; set; }

    /// <summary>Segredo do aplicativo da Twitch do próprio usuário, protegido pelo Windows.</summary>
    public string? StudioTwitchSecretProtected { get; set; }

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

/// <summary>Uma câmera do estúdio guardada nas configurações.</summary>
public sealed class StudioCameraSetting
{
    public string? Name { get; set; }

    /// <summary>0 = superior esquerdo, 1 = superior direito, 2 = inferior esquerdo, 3 = inferior direito.</summary>
    public int Corner { get; set; } = 3;

    public int Percent { get; set; } = 22;

    public bool Frame { get; set; } = true;

    /// <summary>0 original, 1 natural, 2 vívida, 3 cinema, 4 nítida (os mesmos ajustes da imagem principal).</summary>
    public int Preset { get; set; }

    public bool Mirror { get; set; }

    /// <summary>Ligada: aparece no vídeo. Desligada: fica na lista, mas não é aberta.</summary>
    public bool On { get; set; } = true;
}

/// <summary>O que pode aparecer numa cena do estúdio.</summary>
public enum StudioSceneItemKind
{
    /// <summary>A tela ou a janela capturada (o fundo da cena).</summary>
    Capture,
    Camera,
    Pix,
    Intro,
    LowerThird,
    Logo,
}

/// <summary>Uma fonte dentro de uma cena: o tipo, qual câmera (quando é câmera), se está oculta (olho) e se está travada (cadeado).</summary>
public sealed class StudioSceneItem
{
    public StudioSceneItemKind Kind { get; set; }

    /// <summary>Nome da câmera (só para <see cref="StudioSceneItemKind.Camera"/>).</summary>
    public string? Camera { get; set; }

    public bool Hidden { get; set; }

    public bool Locked { get; set; }

    /// <summary>Só para a captura: 0 = a escolha de baixo da prévia, 1 = uma tela, 2 = uma janela (recorte da tela, bom para jogos), 3 = uma janela pelo conteúdo (GDI).</summary>
    public int CaptureMode { get; set; }

    /// <summary>Tela escolhida (CaptureMode 1).</summary>
    public int Display { get; set; }

    /// <summary>Programa da janela escolhida (CaptureMode 2 e 3): é por ele que a janela é achada de novo.</summary>
    public string? WindowProcess { get; set; }

    /// <summary>Título da janela quando foi escolhida (ajuda a achar a certa quando o programa tem várias).</summary>
    public string? WindowTitle { get; set; }
}

/// <summary>Uma cena guardada: o nome e as fontes dela, na ordem da lista.</summary>
public sealed class StudioSceneSetting
{
    public string Name { get; set; } = "Cena";

    public List<StudioSceneItem> Items { get; set; } = new();
}
