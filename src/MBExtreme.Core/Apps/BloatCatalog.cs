using System.Text.RegularExpressions;

namespace MBExtreme.Core.Apps;

public enum AppRemovalLevel
{
    /// <summary>Pode remover sem efeito colateral relevante.</summary>
    Safe,

    /// <summary>Remover pode tirar algo que você usa.</summary>
    Caution,

    /// <summary>Quebra recursos do Windows ou de jogos; difícil de reverter.</summary>
    Dangerous,
}

public sealed record BloatApp(string Id, string Title, string PackagePattern, AppRemovalLevel Level, string Note);

/// <summary>Aplicativos que acompanham o Windows e que algumas pessoas preferem remover.</summary>
public static class BloatCatalog
{
    public static IReadOnlyList<BloatApp> All { get; } = new BloatApp[]
    {
        new("cortana", "Cortana", "*Microsoft.Windows.Cortana*", AppRemovalLevel.Safe, "Assistente descontinuado."),
        new("officehub", "Hub do Office", "*Microsoft.MicrosoftOfficeHub*", AppRemovalLevel.Safe, "Atalho para o Office."),
        new("copilot", "Copilot", "*Copilot*", AppRemovalLevel.Safe, "Assistente de IA da Microsoft."),
        new("getstarted", "Dicas (Get Started)", "*Microsoft.Getstarted*", AppRemovalLevel.Safe, "Tutorial inicial do Windows."),
        new("feedbackhub", "Hub de Comentários", "*Microsoft.WindowsFeedbackHub*", AppRemovalLevel.Safe, "Envio de feedback à Microsoft."),
        new("quickassist", "Assistência Rápida", "*QuickAssist*", AppRemovalLevel.Safe, "Acesso remoto para suporte. Se você usa suporte remoto, mantenha."),
        new("people", "Pessoas", "*Microsoft.People*", AppRemovalLevel.Safe, "Contatos."),
        new("messaging", "Mensagens", "*Microsoft.Messaging*", AppRemovalLevel.Safe, "Mensagens do Windows."),
        new("3dbuilder", "3D Builder", "*Microsoft.3DBuilder*", AppRemovalLevel.Safe, "Modelagem 3D simples."),
        new("solitaire", "Solitaire e jogos casuais", "*Microsoft.MicrosoftSolitaireCollection*", AppRemovalLevel.Safe, "Jogos incluídos."),
        new("weather", "Clima", "*Microsoft.BingWeather*", AppRemovalLevel.Safe, "Previsão do tempo."),
        new("news", "Notícias", "*Microsoft.BingNews*", AppRemovalLevel.Safe, "Notícias da Microsoft."),
        new("maps", "Mapas", "*Microsoft.WindowsMaps*", AppRemovalLevel.Safe, "Mapas do Windows."),
        new("music", "Groove Música", "*Microsoft.ZuneMusic*", AppRemovalLevel.Safe, "Player de música antigo."),
        new("todos", "Microsoft To Do", "*Microsoft.Todos*", AppRemovalLevel.Safe, "Lista de tarefas."),
        new("yourphone", "Vincular ao Celular", "*Microsoft.YourPhone*", AppRemovalLevel.Caution, "Se você usa o celular com o PC, mantenha."),
        new("alarms", "Alarmes e Relógio", "*Microsoft.WindowsAlarms*", AppRemovalLevel.Caution, "Alarmes e temporizadores."),
        new("photos", "Fotos", "*Microsoft.Windows.Photos*", AppRemovalLevel.Caution, "Visualizador de imagens padrão do Windows."),
        new("camera", "Câmera", "*Microsoft.WindowsCamera*", AppRemovalLevel.Caution, "Aplicativo de câmera."),
        new("calendar-mail", "Email e Calendário", "*microsoft.windowscommunicationsapps*", AppRemovalLevel.Caution, "Se você usa o Email do Windows, mantenha."),
        new("outlook", "Outlook (novo)", "*Microsoft.OutlookForWindows*", AppRemovalLevel.Caution, "Cliente de email."),
        new("calculator", "Calculadora", "*Microsoft.WindowsCalculator*", AppRemovalLevel.Caution, "Você provavelmente usa. Mantenha, a menos que tenha outra."),
        new("onedrive", "OneDrive", "*OneDrive*", AppRemovalLevel.Caution, "Sincronização de arquivos. Se seus documentos estão no OneDrive, mantenha."),
        new("xbox", "Xbox e Game Bar", "*xbox*", AppRemovalLevel.Dangerous, "Jogos do Game Pass, Game Bar e login de jogos da Microsoft deixam de funcionar."),
        new("store", "Microsoft Store", "*Microsoft.WindowsStore*", AppRemovalLevel.Dangerous, "Sem a Loja você não instala nem atualiza apps da Loja, e vários recursos dependem dela. Difícil de reverter."),
    };

    /// <summary>Converte o padrão com curinga (*) em expressão regular, sem diferenciar maiúsculas.</summary>
    public static bool Matches(string pattern, string packageName) =>
        Regex.IsMatch(packageName, "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase);
}
