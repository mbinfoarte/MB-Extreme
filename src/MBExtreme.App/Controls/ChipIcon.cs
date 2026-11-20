using System.Globalization;
using System.Windows.Data;

namespace MBExtreme.UI.Controls;

/// <summary>
/// Ícone dos botões de filtro e de abas pequenas (estilo ChipToggle): escolhe um glifo fino da Segoe MDL2 pelo texto do botão
/// ("Velocidade", "Jogos e servidores", "DNS", "Histórico"...). Texto desconhecido ou conteúdo que não é texto: sem ícone.
/// </summary>
public sealed class ChipIconConverter : IValueConverter
{
    // do mais específico para o mais geral: a primeira palavra encontrada no texto decide
    private static readonly (string Word, string Glyph)[] Map =
    {
        ("velocidade", ""), ("jogos e servidores", ""), ("servidor", ""), ("dns", ""), ("roteador", ""),
        ("rota", ""), ("histórico", ""), ("recentes", ""), ("favorit", ""), ("perfil", ""),
        ("otimizad", ""), ("todos", ""), ("todas", ""), ("instalad", ""), ("atualiz", ""),
        ("aplicativ", ""), ("programa", ""), ("jogo", ""), ("game", ""), ("desempenho", ""),
        ("performance", ""), ("privacidade", ""), ("segurança", ""), ("rede", ""), ("internet", ""),
        ("wi-fi", ""), ("energia", ""), ("bateria", ""), ("disco", ""), ("armazen", ""),
        ("memória", ""), ("processador", ""), ("cpu", ""), ("gpu", ""), ("placa", ""),
        ("vídeo", ""), ("áudio", ""), ("som", ""), ("microfone", ""), ("câmera", ""),
        ("teclado", ""), ("mouse", ""), ("controle", ""), ("luz", ""), ("led", ""), ("rgb", ""),
        ("vibra", ""), ("gatilho", ""), ("analógico", ""), ("calibr", ""), ("teste", ""),
        ("visual", ""), ("tema", ""), ("papel", ""), ("anime", ""), ("natureza", ""), ("carro", ""),
        ("espaço", ""), ("abstrat", ""), ("4k", ""), ("1080", ""), ("1440", ""), ("720", ""),
        ("leve", ""), ("equilibrad", ""), ("extrem", ""), ("competitiv", ""), ("seguro", ""),
        ("risco", ""), ("emula", ""), ("xbox", ""), ("ps4", ""), ("ps5", ""), ("modo", ""),
        ("c:", ""), ("d:", ""), ("e:", ""), ("f:", ""), ("mensal", ""), ("anual", ""),
        ("brasil", ""), ("mundo", ""), ("américa", ""), ("europa", ""),
    };

    public static string For(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string t = text.ToLower(CultureInfo.GetCultureInfo("pt-BR"));
        foreach (var (word, glyph) in Map)
            if (t.Contains(word, StringComparison.Ordinal)) return glyph;
        return "";
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => For(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
