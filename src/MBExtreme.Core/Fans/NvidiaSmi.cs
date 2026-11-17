using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;

namespace MBExtreme.Core.Fans;

public sealed record GpuLimits(int Index, string Name, double? PowerLimitW, double? PowerDefaultW, double? PowerMinW, double? PowerMaxW, double? MaxGraphicsClockMhz, double? TemperatureC);

/// <summary>
/// Limites de potência e de clock da placa NVIDIA pelo nvidia-smi (instalado com o driver). Só REDUZ: reduz calor e consumo, não faz overclock.
/// O driver desfaz tudo ao reiniciar o PC; "Restaurar padrão" desfaz na hora.
/// </summary>
public static class NvidiaSmi
{
    public static string? Locate()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static double? Number(string text)
    {
        text = text.Trim();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;   // "[N/A]" e "[Not Supported]" viram nulo
    }

    public const string QueryArguments = "--query-gpu=index,name,power.limit,power.default_limit,power.min_limit,power.max_limit,clocks.max.graphics,temperature.gpu";

    /// <summary>Lê a saída CSV (--format=csv,noheader,nounits). Nomes de placa podem ter vírgula? Não: o nome fica entre o índice e os números.</summary>
    public static IReadOnlyList<GpuLimits> ParseLimits(string csv)
    {
        var list = new List<GpuLimits>();
        foreach (string raw in csv.Replace("\r", "").Split('\n'))
        {
            var parts = raw.Split(',').Select(p => p.Trim()).ToList();
            if (parts.Count < 8 || !int.TryParse(parts[0], out int index)) continue;
            // se o nome contiver vírgula, junta as partes do meio
            string name = string.Join(", ", parts.Skip(1).Take(parts.Count - 7));
            var numbers = parts.Skip(parts.Count - 6).ToList();
            list.Add(new GpuLimits(index, name, Number(numbers[0]), Number(numbers[1]), Number(numbers[2]), Number(numbers[3]), Number(numbers[4]), Number(numbers[5])));
        }
        return list;
    }

    /// <summary>Clocks gráficos suportados (MHz), sem repetir e em ordem crescente.</summary>
    public static IReadOnlyList<int> ParseClocks(string csv) =>
        csv.Replace("\r", "").Split('\n').Select(l => Number(l)).Where(v => v is > 100 and < 6000).Select(v => (int)v!.Value).Distinct().OrderBy(v => v).ToList();

    /// <summary>Potência permitida: de mínimo até o padrão da placa (nunca acima, para não virar overclock).</summary>
    public static (double Min, double Max)? PowerRange(GpuLimits gpu) =>
        gpu.PowerMinW is { } min && gpu.PowerDefaultW is { } def && def > min ? (Math.Ceiling(min), Math.Floor(def)) : null;

    public static string[] PowerArguments(int gpuIndex, double watts) => new[] { "-i", gpuIndex.ToString(), "-pl", Math.Round(watts).ToString(CultureInfo.InvariantCulture) };

    public static string[] ClockArguments(int gpuIndex, int minMhz, int maxMhz)
    {
        if (minMhz < 100 || maxMhz < minMhz || maxMhz > 6000) throw new ArgumentException("Faixa de clock inválida.");
        return new[] { "-i", gpuIndex.ToString(), "-lgc", $"{minMhz},{maxMhz}" };
    }

    public static string[] ResetClockArguments(int gpuIndex) => new[] { "-i", gpuIndex.ToString(), "-rgc" };

    public static (int ExitCode, string Output) Run(string exe, IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info) ?? throw new InvalidOperationException("Não foi possível abrir o nvidia-smi.");
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(15000))
        {
            try { p.Kill(true); } catch (InvalidOperationException) { }
            return (-1, "O nvidia-smi não respondeu.");
        }
        return (p.ExitCode, (o.Result + "\n" + e.Result).Trim());
    }

    public static IReadOnlyList<GpuLimits> Query(string exe)
    {
        var (code, output) = Run(exe, new[] { QueryArguments, "--format=csv,noheader,nounits" });
        return code == 0 ? ParseLimits(output) : Array.Empty<GpuLimits>();
    }

    public static IReadOnlyList<int> SupportedClocks(string exe, int gpuIndex)
    {
        var (code, output) = Run(exe, new[] { "-i", gpuIndex.ToString(), "--query-supported-clocks=gr", "--format=csv,noheader,nounits" });
        return code == 0 ? ParseClocks(output) : Array.Empty<int>();
    }
}
