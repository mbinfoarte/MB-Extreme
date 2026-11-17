namespace MBExtreme.Core.Sensors;

/// <summary>Regras puras da leitura de sensores: o que é leitura válida e como explicar a falta dela.</summary>
public static class SensorRules
{
    /// <summary>Zero, negativo ou acima de 125 °C é leitura quebrada do sensor, não temperatura real.</summary>
    public static bool IsValidTemperature(double celsius) => celsius > 1 && celsius < 125;

    /// <summary>Kelvin em décimos (como o Windows entrega nas zonas térmicas) para Celsius; nulo se fora do plausível.</summary>
    public static double? FromTenthsOfKelvin(double tenths)
    {
        double celsius = tenths / 10.0 - 273.15;
        return IsValidTemperature(celsius) ? Math.Round(celsius, 1) : null;
    }

    /// <summary>Kelvin inteiro (contador de desempenho do Windows) para Celsius.</summary>
    public static double? FromKelvin(double kelvin)
    {
        double celsius = kelvin - 273.15;
        return IsValidTemperature(celsius) ? Math.Round(celsius, 1) : null;
    }

    /// <summary>Texto para o usuário explicando por que a temperatura da CPU não apareceu.</summary>
    public static string WhyCpuTemperatureIsMissing(bool isAdministrator, string? openError, bool cpuFound, int cpuSensors)
    {
        if (openError is not null) return "A leitura de sensores não abriu: " + openError;
        if (!isAdministrator) return "O MB Extreme não está como administrador, e o Windows só libera os sensores da CPU para administradores. Feche e abra como administrador.";
        if (!cpuFound) return "O processador não foi reconhecido pela biblioteca de sensores. Atualize o MB Extreme ou exporte o diagnóstico para o suporte.";
        if (cpuSensors == 0) return "O processador foi reconhecido, mas sem sensores de temperatura: falta o driver de leitura. Instale o PawnIO (botão no Início) e reinicie o programa; se o Windows bloquear drivers (Integridade de memória), desative-a ou use a leitura aproximada.";
        return "O processador não enviou temperatura válida (valor zerado). Se acabou de instalar o driver PawnIO, reinicie o computador.";
    }
}
