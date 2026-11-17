using MBExtreme.Core.Benchmark;
using Xunit;

namespace MBExtreme.Core.Tests;

// Compila todos os sombreadores da janela 3D com o compilador do Windows (d3dcompiler).
// Roda no CI do Windows antes de publicar: um sombreador quebrado trava a versão em vez de travar o benchmark do usuário.
public class ShadersCompilamTests
{
    public static IEnumerable<object[]> Entradas() => new[]
    {
        new object[] { "Game", "VSTerrain", "vs_5_0" }, new object[] { "Game", "VSUnits", "vs_5_0" },
        new object[] { "Game", "PSGame", "ps_5_0" }, new object[] { "Game", "VSWater", "vs_5_0" },
        new object[] { "Game", "PSWater", "ps_5_0" }, new object[] { "Game", "VSBeams", "vs_5_0" },
        new object[] { "Game", "PSBeams", "ps_5_0" }, new object[] { "Game", "VSSparks", "vs_5_0" },
        new object[] { "Game", "PSSparks", "ps_5_0" }, new object[] { "Game", "VSSmoke", "vs_5_0" },
        new object[] { "Game", "PSSmoke", "ps_5_0" }, new object[] { "Game", "PSSky", "ps_5_0" },
        new object[] { "Geometry", "VSGeometry", "vs_5_0" }, new object[] { "Geometry", "PSGeometry", "ps_5_0" },
        new object[] { "Geometry", "PSBackdrop", "ps_5_0" }, new object[] { "Reflections", "PSReflections", "ps_5_0" },
        new object[] { "Studio", "PSStudioTile", "ps_5_0" }, new object[] { "Studio", "PSResolveTiles", "ps_5_0" },
        new object[] { "Game", "VSFull", "vs_5_0" }, new object[] { "Game", "PSOverlay", "ps_5_0" },
    };

    [Theory]
    [MemberData(nameof(Entradas))]
    public void Sombreador_compila(string arquivo, string entrada, string perfil)
    {
        string fonte = GpuWindowBench.ShaderSource(arquivo);
        Assert.Contains(entrada, fonte);
        if (!OperatingSystem.IsWindows()) return;   // o compilador HLSL só existe no Windows
        Assert.NotEmpty(Gpu3DBenchmark.CompileShader(fonte, entrada, perfil));
    }

    [Fact]
    public void Ia_compila()
    {
        string fonte = GpuWindowBench.ShaderSource("AiGemm", false);
        if (!OperatingSystem.IsWindows()) return;
        Assert.NotEmpty(Gpu3DBenchmark.CompileShader(fonte, "CSGemm", "cs_5_0"));
    }
}
