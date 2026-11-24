using MBExtreme.Core.Benchmark;
using Xunit;

namespace MBExtreme.Core.Tests;

public class RenderCpuTests
{
    [Fact]
    public void Bloco_do_ceu_e_azulado_e_o_do_chao_tem_xadrez()
    {
        // topo da imagem: céu (mais azul que vermelho)
        var (sky, skyRays) = CpuRenderBenchmark.RenderTileAt(0, 0, 8, 4, 160, 90, 1);
        int i = (1 * 8 + 4) * 4;
        Assert.True(sky.Bgra[i] > sky.Bgra[i + 2]);                                // B > R
        Assert.Equal(255, sky.Bgra[i + 3]);
        Assert.Equal(8 * 4, skyRays);                                              // céu: um raio por pixel, sem sombra

        // base da imagem: chão com casas claras e escuras
        var (floor, _) = CpuRenderBenchmark.RenderTileAt(0, 70, 160, 20, 160, 90, 1);
        var luminance = Enumerable.Range(0, floor.Bgra.Length / 4).Select(p => floor.Bgra[p * 4] + floor.Bgra[p * 4 + 1] + floor.Bgra[p * 4 + 2]).ToList();
        Assert.True(luminance.Max() - luminance.Min() > 200);
    }

    [Fact]
    public void Mesmo_bloco_sempre_da_o_mesmo_desenho_e_amostras_a_mais_usam_mais_raios()
    {
        var (a, raysA) = CpuRenderBenchmark.RenderTileAt(40, 40, 16, 16, 160, 90, 2);
        var (b, raysB) = CpuRenderBenchmark.RenderTileAt(40, 40, 16, 16, 160, 90, 2);
        Assert.Equal(a.Bgra, b.Bgra);
        Assert.Equal(raysA, raysB);
        var (_, raysOne) = CpuRenderBenchmark.RenderTileAt(40, 40, 16, 16, 160, 90, 1);
        Assert.True(raysA > raysOne * 3);                                          // 4 amostras por pixel
        Assert.True(raysA > 16 * 16 * 4);                                          // sombras e reflexos somam raios
    }

    [Fact]
    public void Execucao_completa_cobre_a_imagem_a_cada_passada_e_da_nota()
    {
        int width = 100, height = 60;
        long area = 0;
        var progress = new List<double>();
        var result = CpuRenderBenchmark.Run(2, 0.05, CancellationToken.None, t => Interlocked.Add(ref area, (long)t.Width * t.Height), progress.Add, width, height, 1);
        Assert.Equal(2, result.Threads);
        Assert.True(result.Passes >= 1);
        Assert.Equal((long)width * height * result.Passes, area);
        Assert.True(result.Rays > width * height);
        Assert.True(result.Score > 0);
        Assert.Equal(result.MegaRaysPerSecond * 100, result.Score, 0);
        Assert.Equal(result.Passes, progress.Count);
        Assert.Equal(100, progress[^1], 6);
    }

    [Fact]
    public void Blocos_das_bordas_tem_tamanho_menor_e_nada_se_repete()
    {
        var seen = new System.Collections.Concurrent.ConcurrentBag<(int, int, int, int)>();
        CpuRenderBenchmark.Run(3, 0, CancellationToken.None, t => seen.Add((t.X, t.Y, t.Width, t.Height)), null, 90, 70, 1);
        var list = seen.ToList();
        Assert.Equal(list.Count, list.Distinct().Count());
        Assert.Contains((80, 40, 10, 30), list);                                   // canto: 90 = 2x40 + 10, 70 = 40 + 30
        Assert.Equal(90 * 70, list.Sum(t => t.Item3 * t.Item4));
    }

    [Fact]
    public void Cancelar_interrompe()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => CpuRenderBenchmark.Run(2, 5, cts.Token, null, null, 100, 60, 1));
    }
}
