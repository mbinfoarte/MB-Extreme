using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MBExtreme.Core.Rgb;

public sealed record RgbDevice(int Index, string Name, string Type, IReadOnlyList<string> Modes, string? ActiveMode);

/// <summary>
/// Controle de iluminação RGB/ARGB (placa-mãe, memória, placa de vídeo, coolers, fitas, ventoinhas) pelo OpenRGB, programa livre e gratuito
/// que fala com centenas de dispositivos. O MB Extreme não inclui o OpenRGB: você baixa do site oficial, e daqui usamos a linha de comando dele.
/// </summary>
public static class OpenRgb
{
    public const string OfficialPage = "https://openrgb.org/";

    private static readonly Regex DeviceStart = new(@"^(\d+):\s*(.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex ModeToken = new(@"\[\s*'?([^\]']+?)'?\s*\]|'([^']+)'|(\S+)", RegexOptions.Compiled);
    private static readonly Regex Hex = new("^[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    /// <summary>Lê a saída de <c>OpenRGB --list-devices</c>. Tolerante a diferenças de versão: só usa índice, nome, tipo e modos.</summary>
    public static IReadOnlyList<RgbDevice> ParseDevices(string output)
    {
        var devices = new List<RgbDevice>();
        int index = -1;
        string name = "", type = "";
        var modes = new List<string>();
        string? active = null;

        void Flush()
        {
            if (index >= 0) devices.Add(new RgbDevice(index, name, type, modes.ToList(), active));
            modes.Clear();
            active = null;
        }

        foreach (string raw in output.Replace("\r", "").Split('\n'))
        {
            if (raw.Length == 0) continue;
            var start = raw.StartsWith(' ') || raw.StartsWith('\t') ? null : DeviceStart.Match(raw);
            if (start is { Success: true })
            {
                Flush();
                index = int.Parse(start.Groups[1].Value);
                name = start.Groups[2].Value;
                type = "";
                continue;
            }

            string line = raw.Trim();
            if (index < 0) continue;
            if (line.StartsWith("Type:", StringComparison.OrdinalIgnoreCase)) type = line[5..].Trim();
            else if (line.StartsWith("Modes:", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match m in ModeToken.Matches(line[6..]))
                {
                    string mode = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value).Trim();
                    if (mode.Length == 0) continue;
                    modes.Add(mode);
                    if (m.Groups[1].Success) active = mode;
                }
            }
        }

        Flush();
        return devices;
    }

    public static bool IsValidHex(string? hex) => hex is not null && Hex.IsMatch(hex);

    public static string ToHex(byte r, byte g, byte b) => $"{r:X2}{g:X2}{b:X2}";

    /// <summary>Escala a cor para um brilho de 0 a 100%.</summary>
    public static string WithBrightness(string hex, int percent)
    {
        if (!IsValidHex(hex)) throw new ArgumentException("Cor inválida.", nameof(hex));
        double f = Math.Clamp(percent, 0, 100) / 100.0;
        byte C(int at) => (byte)Math.Round(Convert.ToInt32(hex.Substring(at, 2), 16) * f);
        return ToHex(C(0), C(2), C(4));
    }

    /// <summary>Escolhe o modo estático do dispositivo (a luz fixa em uma cor), em qualquer idioma/variação de nome.</summary>
    public static string? StaticMode(RgbDevice device) =>
        device.Modes.FirstOrDefault(m => m.Equals("Static", StringComparison.OrdinalIgnoreCase))
        ?? device.Modes.FirstOrDefault(m => m.Equals("Direct", StringComparison.OrdinalIgnoreCase))
        ?? device.Modes.FirstOrDefault(m => m.Contains("Static", StringComparison.OrdinalIgnoreCase) || m.Contains("Direct", StringComparison.OrdinalIgnoreCase));

    /// <summary>Argumentos para pôr uma cor (e opcionalmente um modo) em vários dispositivos de uma vez.</summary>
    public static IReadOnlyList<string> ColorArguments(IEnumerable<(RgbDevice Device, string Hex, string? Mode)> items)
    {
        var args = new List<string>();
        foreach (var (device, hex, mode) in items)
        {
            if (!IsValidHex(hex)) throw new ArgumentException("Cor inválida: " + hex);
            string? chosen = mode ?? StaticMode(device);
            args.Add("--device");
            args.Add(device.Index.ToString());
            if (chosen is not null)
            {
                if (!device.Modes.Contains(chosen, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException($"O dispositivo {device.Name} não tem o modo {chosen}.");
                args.Add("--mode");
                args.Add(chosen);
            }
            args.Add("--color");
            args.Add(hex.ToUpperInvariant());
        }
        return args;
    }

    /// <summary>Gradiente de temperatura: azul (frio) → verde → amarelo → vermelho (quente).</summary>
    public static string TemperatureColor(double celsius)
    {
        (double t, byte r, byte g, byte b)[] stops = { (30, 0, 120, 255), (45, 0, 220, 120), (60, 255, 200, 0), (75, 255, 100, 0), (85, 255, 0, 0) };
        if (celsius <= stops[0].t) return ToHex(stops[0].r, stops[0].g, stops[0].b);
        if (celsius >= stops[^1].t) return ToHex(stops[^1].r, stops[^1].g, stops[^1].b);
        for (int i = 1; i < stops.Length; i++)
        {
            if (celsius > stops[i].t) continue;
            var a = stops[i - 1];
            var b = stops[i];
            double f = (celsius - a.t) / (b.t - a.t);
            byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * f);
            return ToHex(L(a.r, b.r), L(a.g, b.g), L(a.b, b.b));
        }
        return ToHex(stops[^1].r, stops[^1].g, stops[^1].b);
    }

    public static string? Locate(string? configuredPath)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredPath)) candidates.Add(configuredPath);
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData })
        {
            string root = Environment.GetFolderPath(folder);
            if (root.Length == 0) continue;
            candidates.Add(Path.Combine(root, "OpenRGB", "OpenRGB.exe"));
            candidates.Add(Path.Combine(root, "Programs", "OpenRGB", "OpenRGB.exe"));
        }
        string? onPath = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Where(p => p.Length > 0)
            .Select(p => Path.Combine(p, "OpenRGB.exe")).FirstOrDefault(File.Exists);
        if (onPath is not null) candidates.Add(onPath);
        return candidates.FirstOrDefault(c => c.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(c));
    }

    public static (int ExitCode, string Output) Run(string exe, IEnumerable<string> arguments, TimeSpan? timeout = null)
    {
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
        };
        foreach (string a in arguments) info.ArgumentList.Add(a);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Não foi possível abrir o OpenRGB.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit((int)(timeout ?? TimeSpan.FromSeconds(25)).TotalMilliseconds))
        {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            return (-1, "O OpenRGB não respondeu a tempo.");
        }
        return (process.ExitCode, (output.Result + "\n" + error.Result).Trim());
    }

    public static IReadOnlyList<RgbDevice> List(string exe)
    {
        var (code, output) = Run(exe, new[] { "--list-devices" });
        if (code != 0 && !output.Contains(':')) throw new InvalidOperationException("O OpenRGB não listou dispositivos: " + output);
        return ParseDevices(output);
    }
}
