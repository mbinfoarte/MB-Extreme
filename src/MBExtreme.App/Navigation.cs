namespace MBExtreme.UI;

/// <summary>Um atalho dentro de um módulo (abre uma página).</summary>
internal sealed record Tile(string Key, string Title, string Description, string Icon);

/// <summary>Um módulo do menu lateral. Ao clicar, mostra os atalhos dele.</summary>
internal sealed record Module(string Key, string Title, string Subtitle, string Icon, IReadOnlyList<Tile> Tiles);

internal static class NavigationMap
{
    // Ícones: fonte "Segoe MDL2 Assets", que já vem no Windows 10 e 11.
    public static IReadOnlyList<Module> Modules { get; } = new Module[]
    {
        new("opt", "Otimização", "Deixe o computador mais rápido e leve.", "", new Tile[]
        {
            new("cleanup", "Limpeza", "Procura lixo, temporários, cache e libera memória.", ""),
            new("tweaks-perf", "Desempenho", "Prioridade de CPU, energia e serviços.", ""),
            new("tweaks-game", "Jogos e GPU", "Ajustes para jogar melhor.", ""),
            new("memory", "Memória", "Limpe a RAM e proteja aplicativos.", ""),
            new("storage", "Armazenamento", "SSD, HD e saúde dos discos.", ""),
        }),
        new("net", "Rede", "Conexão, ping e DNS.", "", new Tile[]
        {
            new("dns", "DNS", "Teste e escolha o servidor mais rápido.", ""),
            new("net-tweaks", "Otimizações de rede", "Ajustes do Windows para a rede.", ""),
            new("netanalyzer", "Analisador de rede", "Ping, TCPing e MTR.", ""),
        }),
        new("win", "Windows", "Privacidade, aplicativos e inicialização.", "", new Tile[]
        {
            new("privacy", "Privacidade", "Anúncios, histórico, Copilot e SmartScreen.", ""),
            new("debloat", "Aplicativos", "Remova apps do Windows que você não usa.", ""),
            new("boot", "Inicialização e boot", "Como o Windows liga e desliga.", ""),
            new("periph", "Periféricos", "Mouse e teclado.", ""),
        }),
        new("tools", "Ferramentas", "Hardware, drivers e histórico.", "", new Tile[]
        {
            new("perf", "Hardware e benchmarks", "Informações do sistema e testes.", ""),
            new("tools-drivers", "Drivers e programas", "Atualizações por fontes oficiais.", ""),
            new("actions", "Central de ações", "Histórico e desfazer.", ""),
        }),
    };

    public static Module? ModuleOf(string pageKey) =>
        Modules.FirstOrDefault(m => m.Key == pageKey || m.Tiles.Any(t => t.Key == pageKey));

    public static Tile? TileOf(string pageKey) =>
        Modules.SelectMany(m => m.Tiles).FirstOrDefault(t => t.Key == pageKey);
}
