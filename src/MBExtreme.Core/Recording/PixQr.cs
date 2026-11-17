using QRCoder;

namespace MBExtreme.Core.Recording;

/// <summary>Desenha o QR do Pix como uma grade de módulos (true = escuro). A imagem em si é desenhada pelo aplicativo.</summary>
public static class PixQr
{
    public static bool[,] Modules(string payload)
    {
        if (string.IsNullOrEmpty(payload) || payload.Length > 600) throw new ArgumentException("Texto do Pix inválido.", nameof(payload));
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        var matrix = data.ModuleMatrix;
        int total = matrix.Count;

        // a biblioteca já inclui a borda branca (quiet zone); tira para devolver só o QR e deixar o desenho escolher a borda
        int margin = 0;
        while (margin < total / 2 && !Enumerable.Range(0, total).Any(i => matrix[margin][i] || matrix[i][margin])) margin++;
        int size = total - 2 * margin;
        var modules = new bool[size, size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                modules[y, x] = matrix[y + margin][x + margin];
        return modules;
    }
}
