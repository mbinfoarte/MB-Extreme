using System.Diagnostics;
using System.Text;

namespace MBExtreme.Core.Repair;

/// <summary>Executa as tarefas de reparo mostrando a saída ao vivo e permitindo cancelar.</summary>
public sealed class RepairRunner
{
    private const int MaxCaptured = 200_000;

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MBExtreme", "logs");

    public async Task<RepairResult> RunAsync(RepairTask task, IProgress<RepairUpdate> progress, CancellationToken token)
    {
        var results = new List<StepResult>();
        using var log = OpenLog(task);
        bool cancelled = false;

        void Write(string message, double percent, bool isLog)
        {
            progress.Report(new RepairUpdate(message, percent, isLog));
            if (isLog) log?.WriteLine(message);
        }

        for (int i = 0; i < task.Steps.Count; i++)
        {
            var step = task.Steps[i];
            double Overall(double stepPercent) => 100.0 * (i + stepPercent / 100.0) / task.Steps.Count;

            if (token.IsCancellationRequested) { cancelled = true; break; }
            Write($"▶ {step.Title}", Overall(0), true);
            Write($"  {step.CommandLine}", Overall(0), true);

            StepResult result;
            try
            {
                result = await RunStepAsync(step, Overall, Write, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                Write("  Interrompido.", Overall(0), true);
                break;
            }
            catch (Exception ex)
            {
                result = new StepResult(step.Title, -1, false, "Não foi possível executar: " + ex.Message);
            }

            results.Add(result);
            Write($"  {(result.Ok ? "✔" : "✖")} {result.Verdict}", Overall(100), true);

            if (!result.Ok && !step.ContinueOnError && step.Kind is StepKind.Generic)
            {
                Write("  Etapa falhou; os próximos comandos desta tarefa não foram executados.", Overall(100), true);
                break;
            }
        }

        bool success = !cancelled && results.Count == task.Steps.Count;
        for (int k = 0; success && k < results.Count; k++)
            if (!results[k].Ok && !task.Steps[k].ContinueOnError) success = false;
        string summary = RepairInterpreter.Summarize(task, results, cancelled);
        Write(summary, 100, true);
        return new RepairResult(success, cancelled, success && task.NeedsRestart, summary, results);
    }

    private static async Task<StepResult> RunStepAsync(RepairStep step, Func<double, double> overall,
        Action<string, double, bool> write, CancellationToken token)
    {
        var info = new ProcessStartInfo(step.File, step.Arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = step.Input is not null,
        };

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"Não foi possível iniciar {step.File}.");
        using var registration = token.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        });

        if (step.Input is not null)
        {
            try
            {
                await process.StandardInput.WriteLineAsync(step.Input).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            catch (IOException) { }
        }

        var captured = new StringBuilder();
        var gate = new object();
        double lastPercent = 0;

        void OnLine(string raw)
        {
            string line = RepairOutput.Clean(raw);
            if (line.Length == 0) return;
            lock (gate)
            {
                if (captured.Length < MaxCaptured) captured.AppendLine(line);
                if (RepairOutput.TryPercent(line) is { } percent)
                {
                    lastPercent = percent;
                    write(line, overall(percent), false);
                }
                else write(line, overall(lastPercent), true);
            }
        }

        await Task.WhenAll(Pump(process.StandardOutput.BaseStream, OnLine), Pump(process.StandardError.BaseStream, OnLine))
            .ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);
        token.ThrowIfCancellationRequested();

        var (ok, verdict) = RepairInterpreter.Interpret(step.Kind, process.ExitCode, captured.ToString());
        return new StepResult(step.Title, process.ExitCode, ok, verdict);
    }

    private static async Task Pump(Stream stream, Action<string> onLine)
    {
        var decoder = new OutputDecoder();
        var splitter = new LineSplitter();
        var buffer = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            foreach (string line in splitter.Feed(decoder.Decode(buffer.AsSpan(0, read))))
                onLine(line);
        if (splitter.Flush() is { } rest) onLine(rest);
    }

    private static StreamWriter? OpenLog(RepairTask task)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            string path = Path.Combine(LogDirectory, $"reparo-{task.Id}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            return new StreamWriter(path, false, new UTF8Encoding(true)) { AutoFlush = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
