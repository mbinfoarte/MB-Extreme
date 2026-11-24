using System.Globalization;
using System.Text;

namespace MBExtreme.Core.Sensors;

public sealed record SensorAlert(string Component, string Sensor, double Value);

/// <summary>Ferramentas de monitoramento: registro contínuo em CSV (para comparar antes e depois, achar o que esquenta) e alertas de temperatura.</summary>
public static class SensorTools
{
    /// <summary>Temperaturas de processador, placa de vídeo, placa-mãe e discos acima do limite.</summary>
    public static IReadOnlyList<SensorAlert> HotSensors(IEnumerable<SensorGroup> groups, double limitC)
    {
        var list = new List<SensorAlert>();
        foreach (var g in groups)
            foreach (var s in g.Sensors.Where(s => s.Kind == "Temperature" && s.Value is not null && s.Value >= limitC))
            {
                // limites de "distância até o limite" e sensores de borda são reportados por alguns chips; ignora valores absurdos
                if (s.Value > 150) continue;
                list.Add(new SensorAlert(g.Name, s.Name, s.Value!.Value));
            }
        return list.OrderByDescending(a => a.Value).ToList();
    }

    /// <summary>Cabeçalho e linhas de um registro CSV (ponto e vírgula, decimais com ponto para abrir em planilhas).</summary>
    public sealed class CsvLog
    {
        private readonly List<(string Id, string Title)> _columns;
        private readonly StringBuilder _text = new();

        public CsvLog(IEnumerable<SensorGroup> groups)
        {
            _columns = groups.SelectMany(g => g.Sensors.Select(s => (s.Id, $"{g.Name} - {s.Name} ({s.Kind})"))).ToList();
            _text.Append("Hora");
            foreach (var (_, title) in _columns) _text.Append(';').Append(Escape(title));
            _text.AppendLine();
        }

        public int Columns => _columns.Count;

        public string Header => _text.ToString();

        /// <summary>Linha do instante: valor de cada coluna (vazio se o sensor sumiu).</summary>
        public string Row(DateTime time, IEnumerable<SensorGroup> groups)
        {
            var values = groups.SelectMany(g => g.Sensors).GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First().Value);
            var line = new StringBuilder(time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            foreach (var (id, _) in _columns)
            {
                line.Append(';');
                if (values.TryGetValue(id, out var v) && v is { } number) line.Append(number.ToString("0.###", CultureInfo.InvariantCulture));
            }
            return line.ToString();
        }

        private static string Escape(string text) => text.Contains(';') || text.Contains('"') ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }
}
