using System.Net;
using System.Text;
using System.Text.Json;

namespace MBExtreme.Core.Gamepads;

/// <summary>Medida de um analógico: drift com ele solto e erro de circularidade girando.</summary>
public sealed record StickMeasure(double DriftPeak, double DriftOffset, double CircularityError)
{
    public string Verdict => DriftMeter.Verdict(DriftPeak);
}

/// <summary>Um atendimento: o controle, o cliente, como estava antes e como ficou.</summary>
public sealed record PadRepair(
    DateTime When,
    string Model,
    string? Serial,
    string? Board,
    string? Color,
    string? Client,
    string? Contact,
    StickMeasure? LeftBefore,
    StickMeasure? RightBefore,
    StickMeasure? LeftAfter,
    StickMeasure? RightAfter,
    bool Saved,
    string? Notes = null);

/// <summary>Histórico de atendimentos e cópias de segurança da calibração, em %AppData%\MBExtreme\controles.</summary>
public sealed class PadRepairLog
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _folder;

    public PadRepairLog(string? folder = null)
    {
        _folder = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "controles");
    }

    private string HistoryFile => Path.Combine(_folder, "atendimentos.json");

    private static string SafeName(string? serial) =>
        string.Concat((serial ?? "sem-serie").Select(c => char.IsLetterOrDigit(c) ? c : '-'));

    public IReadOnlyList<PadRepair> Load()
    {
        try
        {
            return File.Exists(HistoryFile) ? JsonSerializer.Deserialize<List<PadRepair>>(File.ReadAllText(HistoryFile)) ?? new() : new List<PadRepair>();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new List<PadRepair>();
        }
    }

    public void Add(PadRepair repair)
    {
        var list = Load().ToList();
        list.Add(repair);
        Directory.CreateDirectory(_folder);
        File.WriteAllText(HistoryFile, JsonSerializer.Serialize(list, Json));
    }

    public IReadOnlyList<PadRepair> For(string? serial) => Load().Where(r => r.Serial == serial).OrderByDescending(r => r.When).ToList();

    // ---- cópia de segurança dos valores de calibração (DualSense), feita antes da primeira mudança

    private string BackupFile(string? serial) => Path.Combine(_folder, "backup-" + SafeName(serial) + ".json");

    /// <summary>Guarda a calibração original. Não sobrescreve: a primeira cópia é a de fábrica (ou a de antes do primeiro atendimento).</summary>
    public bool SaveBackup(string? serial, IReadOnlyList<ushort> values)
    {
        string file = BackupFile(serial);
        if (File.Exists(file)) return false;
        Directory.CreateDirectory(_folder);
        File.WriteAllText(file, JsonSerializer.Serialize(new { serial, when = DateTime.Now, values }, Json));
        return true;
    }

    public ushort[]? LoadBackup(string? serial)
    {
        try
        {
            string file = BackupFile(serial);
            if (!File.Exists(file)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var values = doc.RootElement.GetProperty("values").EnumerateArray().Select(v => (ushort)v.GetInt32()).ToArray();
            return values.Length == 12 ? values : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    // ---- relatório para o cliente (página HTML para imprimir ou mandar)

    public static string ReportHtml(PadRepair r, string shopName = "MB InfoArt")
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "—");
        static string Pct(double v) => $"{v * 100:0.0}%";
        string Row(string name, StickMeasure? before, StickMeasure? after) =>
            $"<tr><td>{name}</td><td>{(before is null ? "—" : $"{Pct(before.DriftPeak)} ({E(before.Verdict)})")}</td><td>{(after is null ? "—" : $"{Pct(after.DriftPeak)} ({E(after.Verdict)})")}</td>" +
            $"<td>{(before is null || before.CircularityError <= 0 ? "—" : $"{before.CircularityError:0.0}%")}</td><td>{(after is null || after.CircularityError <= 0 ? "—" : $"{after.CircularityError:0.0}%")}</td></tr>";
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\"><title>Laudo do controle</title><style>");
        sb.Append("body{font-family:Segoe UI,Arial,sans-serif;margin:32px;color:#111}h1{margin:0;font-size:22px}.brand{color:#c99a00;font-weight:800}");
        sb.Append("table{border-collapse:collapse;width:100%;margin:16px 0}td,th{border:1px solid #ccc;padding:8px;text-align:left}th{background:#f5f1e0}");
        sb.Append(".muted{color:#666;font-size:13px}.ok{color:#0a7a3e;font-weight:700}.box{border:1px solid #ddd;border-radius:8px;padding:12px 16px;margin-top:12px}");
        sb.Append("</style></head><body>");
        sb.Append($"<h1><span class=\"brand\">{E(shopName)}</span> · Laudo de calibração do controle</h1>");
        sb.Append($"<p class=\"muted\">{r.When:dd/MM/yyyy HH:mm} · feito com o MB Extreme</p>");
        sb.Append("<div class=\"box\">");
        sb.Append($"<b>Cliente:</b> {E(r.Client)}{(string.IsNullOrWhiteSpace(r.Contact) ? "" : $" · {E(r.Contact)}")}<br>");
        sb.Append($"<b>Controle:</b> {E(r.Model)} · <b>Série:</b> {E(r.Serial)} · <b>Placa:</b> {E(r.Board)} · <b>Cor:</b> {E(r.Color)}");
        sb.Append("</div>");
        sb.Append("<table><tr><th>Analógico</th><th>Drift antes</th><th>Drift depois</th><th>Circularidade antes</th><th>Circularidade depois</th></tr>");
        sb.Append(Row("Esquerdo", r.LeftBefore, r.LeftAfter));
        sb.Append(Row("Direito", r.RightBefore, r.RightAfter));
        sb.Append("</table>");
        sb.Append(r.Saved ? "<p class=\"ok\">Calibração gravada na memória do controle (permanente).</p>" : "<p class=\"muted\">A calibração não foi gravada de forma permanente.</p>");
        sb.Append("<p class=\"muted\">Drift: quanto o analógico sai do centro estando solto (até 2,5% é normal; nos jogos, abaixo de 10% costuma não aparecer). ");
        sb.Append("Circularidade: o quanto a borda do movimento se afasta de um círculo perfeito (quanto menor, melhor).</p>");
        if (!string.IsNullOrWhiteSpace(r.Notes)) sb.Append($"<div class=\"box\"><b>Observações:</b> {E(r.Notes)}</div>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    /// <summary>Grava o laudo em Documentos\MB Extreme\Controles e devolve o caminho.</summary>
    public static string WriteReport(PadRepair r, string? folder = null)
    {
        folder ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MB Extreme", "Controles");
        Directory.CreateDirectory(folder);
        string name = $"laudo-{SafeName(r.Client ?? r.Serial)}-{r.When:yyyy-MM-dd-HHmm}.html";
        string file = Path.Combine(folder, name);
        File.WriteAllText(file, ReportHtml(r), Encoding.UTF8);
        return file;
    }
}
