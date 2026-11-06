namespace MBExtreme.Core.Ui;

/// <summary>Regras para dar nome aos controles para o Narrador (testáveis sem a interface).</summary>
public static class AccessibleText
{
    /// <summary>Texto de verdade: não vazio e não feito só de ícones (caracteres da área privada, como os da fonte Segoe MDL2 Assets).</summary>
    public static bool IsReadable(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.Any(c => !char.IsWhiteSpace(c) && (c < '' || c > ''));

    /// <summary>O primeiro texto legível entre os candidatos (o que aparece, depois a dica, depois a etiqueta), sem espaços nas pontas.</summary>
    public static string? Pick(params string?[] candidates) => candidates.FirstOrDefault(IsReadable)?.Trim();

    public static readonly double[] Scales = { 0.9, 1.0, 1.1, 1.25, 1.5 };

    /// <summary>Próxima escala para cima ou para baixo (Ctrl + e Ctrl −); fora da lista, parte do 100%.</summary>
    public static double Step(double current, int direction)
    {
        int index = Array.FindIndex(Scales, s => Math.Abs(s - current) < 0.01);
        if (index < 0) index = 1;
        return Scales[Math.Clamp(index + direction, 0, Scales.Length - 1)];
    }
}
