namespace MBExtreme.Core.Fans;

/// <summary>O que o motor de ventoinhas precisa do hardware (implementado pelo serviço real e por um simulado nos testes).</summary>
public interface IFanBackend
{
    IReadOnlyList<FanChannel> Channels();
    IReadOnlyList<TemperatureSource> Temperatures();
    double? Temperature(string sensorId);
    bool SetPercent(string controlId, double percent);
    void SetAuto(string controlId);
    void RestoreAll();
}

public sealed record FanStepResult(string ChannelId, string Mode, double? AppliedPercent, string Note);

/// <summary>
/// Aplica os perfis (automático, manual ou curva por temperatura) a cada ventoinha, a cada passo (2 s).
/// Regras de segurança: temperatura alta força velocidade alta, bomba nunca abaixo de 60%, falta de leitura vai a 100%.
/// </summary>
public sealed class FanEngine
{
    private readonly IFanBackend _backend;
    private readonly Func<IReadOnlyList<FanChannelConfig>> _configs;
    private readonly Dictionary<string, double> _last = new();
    private readonly Dictionary<string, double> _lastTemp = new();
    private readonly HashSet<string> _driven = new();

    public FanEngine(IFanBackend backend, Func<IReadOnlyList<FanChannelConfig>> configs)
    {
        _backend = backend;
        _configs = configs;
    }

    /// <summary>Maior temperatura de CPU ou GPU: serve de referência de segurança para qualquer ventoinha.</summary>
    public static double? HottestCoreTemperature(IEnumerable<TemperatureSource> sources)
    {
        var values = sources.Where(s => s.Value is not null && (s.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase)
            || s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)))
            .Select(s => s.Value!.Value).ToList();
        return values.Count == 0 ? null : values.Max();
    }

    public IReadOnlyList<FanStepResult> Step()
    {
        var results = new List<FanStepResult>();
        var channels = _backend.Channels().Where(c => c.Controllable).ToDictionary(c => c.ControlId);
        var temps = _backend.Temperatures();
        double? safetyTemp = HottestCoreTemperature(temps);

        foreach (var config in _configs())
        {
            if (!channels.TryGetValue(config.ChannelId, out var channel)) continue;

            if (config.Mode == "auto")
            {
                if (_driven.Remove(config.ChannelId)) _backend.SetAuto(config.ChannelId);   // devolve ao firmware uma vez
                results.Add(new FanStepResult(config.ChannelId, "auto", null, "Controle automático (BIOS)."));
                continue;
            }

            double refTemp = safetyTemp ?? 0;
            double wanted;
            string note;
            if (config.Mode == "manual")
            {
                wanted = config.ManualPercent;
                note = "Manual.";
            }
            else
            {
                double? source = config.SourceSensorId is { } id ? temps.FirstOrDefault(t => t.Id == id)?.Value ?? _backend.Temperature(id) : safetyTemp;
                if (source is null)
                {
                    wanted = FanSafety.FailSafe;
                    note = "Sem leitura da temperatura: ventoinha em 100% por segurança.";
                }
                else
                {
                    wanted = new FanCurve(config.Curve).Evaluate(source.Value);
                    refTemp = Math.Max(refTemp, source.Value);
                    note = $"Curva a {source.Value:0} °C.";
                }
            }

            double target = FanSafety.Clamp(wanted, config.Kind, refTemp);
            double current = _last.TryGetValue(config.ChannelId, out var l) ? l : channel.Percent ?? target;
            double lastTemp = _lastTemp.TryGetValue(config.ChannelId, out var lt) ? lt : refTemp;
            // manual é o que o usuário pediu: vale na hora (a segurança já foi aplicada em target). Só a curva suaviza.
            double next = config.Mode == "manual" ? target : FanSafety.Smooth(current, target, refTemp, lastTemp);
            if (next < target) next = target;   // nunca abaixo do alvo seguro
            if (next > current || !_lastTemp.ContainsKey(config.ChannelId)) _lastTemp[config.ChannelId] = refTemp;   // temperatura em que a velocidade subiu pela última vez
            if (_backend.SetPercent(config.ChannelId, next))
            {
                _last[config.ChannelId] = next;
                _driven.Add(config.ChannelId);
                results.Add(new FanStepResult(config.ChannelId, config.Mode, next, note));
            }
            else results.Add(new FanStepResult(config.ChannelId, config.Mode, null, "O hardware não aceitou o comando."));
        }

        return results;
    }

    /// <summary>Para o controle e devolve tudo ao firmware.</summary>
    public void Stop()
    {
        _backend.RestoreAll();
        _driven.Clear();
        _last.Clear();
        _lastTemp.Clear();
    }
}
