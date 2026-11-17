using System.Net.Http;

namespace MBExtreme.Core.Network;

/// <summary>Ping parado e ping com a internet ocupada (download e upload). Se o ping sobe muito, o roteador acumula fila e o jogo sofre quando alguém baixa algo.</summary>
public sealed record BufferbloatResult(double IdleMs, double? DownloadLoadedMs, double? UploadLoadedMs, string Grade, string Advice);

public static class Bufferbloat
{
    /// <summary>Nota pelo aumento do ping sob carga: A até 5 ms, B até 30, C até 60, D até 200, F acima.</summary>
    public static string Grade(double increaseMs) => increaseMs switch { < 5 => "A", < 30 => "B", < 60 => "C", < 200 => "D", _ => "F" };

    public static string Advice(string grade) => grade switch
    {
        "A" or "B" => "Sua conexão aguenta carga sem atrapalhar o jogo.",
        "C" => "O ping sobe com a internet ocupada. Se alguém baixa arquivos ou assiste vídeo enquanto você joga, vai sentir. Ative SQM/QoS (\"Smart Queue\", \"Gerenciamento de banda\") no roteador, se ele tiver.",
        _ => "O ping dispara quando a internet fica ocupada (bufferbloat). A correção é no roteador: ative SQM/QoS com o limite em cerca de 90% da velocidade contratada, ou limite download/upload dos outros aparelhos enquanto joga.",
    };

    public static BufferbloatResult Evaluate(double idle, double? down, double? up)
    {
        double worst = Math.Max(down ?? idle, up ?? idle);
        string grade = Grade(Math.Max(0, worst - idle));
        return new BufferbloatResult(idle, down, up, grade, Advice(grade));
    }

    private static async Task<double?> AveragePingAsync(string host, int count, CancellationToken token)
    {
        var s = new HopStats();
        for (int i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            s.Add(await NetAnalyzer.PingAsync(host, 2000));
            await Task.Delay(200, token);
        }
        return s.Received == 0 ? null : s.Average;
    }

    private static async Task<double?> UnderLoadAsync(string host, Func<CancellationToken, Task> load, CancellationToken token)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var running = Task.Run(async () =>
        {
            try { await load(stop.Token); }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException) { }
        }, CancellationToken.None);
        await Task.Delay(1500, token); // deixa a carga encher a fila
        double? loaded = await AveragePingAsync(host, 10, token);
        stop.Cancel();
        await running;
        return loaded;
    }

    /// <summary>Usa o teste de velocidade público da Cloudflare só como carga de download e upload. Devolve null se não houver internet.</summary>
    public static async Task<BufferbloatResult?> RunAsync(string pingHost, HttpClient http, IProgress<string>? progress, CancellationToken token)
    {
        progress?.Report("Medindo o ping parado...");
        double? idle = await AveragePingAsync(pingHost, 10, token);
        if (idle is null) return null;

        progress?.Report("Medindo o ping durante um download...");
        double? down = await UnderLoadAsync(pingHost, async t =>
        {
            using var response = await http.GetAsync("https://speed.cloudflare.com/__down?bytes=200000000", HttpCompletionOption.ResponseHeadersRead, t);
            await using var stream = await response.Content.ReadAsStreamAsync(t);
            var buffer = new byte[81920];
            while (await stream.ReadAsync(buffer, t) > 0) { }
        }, token);

        progress?.Report("Medindo o ping durante um upload...");
        double? up = await UnderLoadAsync(pingHost, async t =>
        {
            var data = new byte[1024 * 1024];
            while (!t.IsCancellationRequested)
            {
                using var content = new ByteArrayContent(data);
                using var response = await http.PostAsync("https://speed.cloudflare.com/__up", content, t);
            }
        }, token);

        return Evaluate(idle.Value, down, up);
    }
}
