using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using MBExtreme.Core.Benchmark;
using Xunit;

namespace MBExtreme.Core.Tests;

public class RayTracingDx12Tests
{
    [Fact]
    public void Malhas_tem_indices_validos_e_normais_unitarias()
    {
        var m = RtScene.Build();
        Assert.Equal(4, m.Count);
        int[] triangles = { 2, 48 * 24 * 2, 64 * 24 * 2, 12 };
        for (int k = 0; k < m.Count; k++)
        {
            uint start = m.Info[k * 4], vstart = m.Info[k * 4 + 1], count = m.Info[k * 4 + 2], vcount = m.Info[k * 4 + 3];
            Assert.Equal(triangles[k] * 3, (int)count);
            for (uint i = start; i < start + count; i++) Assert.True(m.Indices[i] < vcount, $"malha {k}: índice fora");
            for (uint v = vstart; v < vstart + vcount; v++)
            {
                float nx = m.Vertices[v * 8 + 4], ny = m.Vertices[v * 8 + 5], nz = m.Vertices[v * 8 + 6];
                Assert.InRange(MathF.Sqrt(nx * nx + ny * ny + nz * nz), 0.99f, 1.01f);
            }
        }
        Assert.True(m.TriangleCount > 5000);
    }

    [Fact]
    public void Instancias_no_formato_do_DirectX_12_e_animadas()
    {
        Assert.Equal(64, Marshal.SizeOf<RtInstance>());
        var a = new RtInstance[RtScene.InstanceCount];
        var b = new RtInstance[RtScene.InstanceCount];
        ulong[] blas = { 0x1000, 0x2000, 0x3000, 0x4000 };
        RtScene.Animate(a, 0, blas);
        RtScene.Animate(b, 1.5, blas);
        Assert.InRange(RtScene.InstanceCount, 150, 400);
        Assert.Equal(RtScene.Floor, (int)a[0].Mesh);
        Assert.Equal(RtScene.MatFloor, (int)a[0].Material);
        int moved = 0;
        for (int i = 0; i < a.Length; i++)
        {
            Assert.True(a[i].Mesh < 4 && a[i].Material <= RtScene.MatPillar);
            Assert.Equal(0xFFu, a[i].IdAndMask >> 24);
            Assert.Equal(blas[a[i].Mesh], a[i].Blas);
            Assert.True(float.IsFinite(a[i].M00 + a[i].M11 + a[i].M22 + a[i].M13));
            if (a[i].M13 != b[i].M13 || a[i].M00 != b[i].M00) moved++;
        }
        Assert.True(moved > 50, "a cena precisa se mexer para a TLAS ser refeita a cada quadro");
        Assert.Contains(a, x => x.Material == RtScene.MatChrome);
        Assert.Contains(a, x => x.Material == RtScene.MatPillar);
        Assert.Contains(a, x => x.Mesh == RtScene.Torus);
    }

    [Fact]
    public void Custo_do_ray_tracing()
    {
        var off = new GpuSceneStat(Dx12RayBench.OffTitle, 200, 150, 120, Array.Empty<double>());
        var on = new GpuSceneStat(Dx12RayBench.OnTitle, 80, 60, 50, Array.Empty<double>());
        var r = new RayTracingResult("RTX", off, on);
        Assert.Equal(60, r.CostPercent, 3);
        Assert.Equal(0, new RayTracingResult("GTX", null, null, "sem DXR").CostPercent);
    }

    [Fact]
    public void Dxil_embutido_esta_assinado_tem_assinatura_raiz_e_bate_com_a_fonte()
    {
        byte[] dxil = Dx12RayBench.ShaderDxil();
        Assert.Equal("DXBC", Encoding.ASCII.GetString(dxil, 0, 4));
        Assert.Contains(dxil.Skip(4).Take(16), x => x != 0);            // assinado pelo validador (dxil)
        int parts = BitConverter.ToInt32(dxil, 28);
        var names = Enumerable.Range(0, parts).Select(i => Encoding.ASCII.GetString(dxil, BitConverter.ToInt32(dxil, 32 + i * 4), 4)).ToList();
        Assert.Contains("RTS0", names);
        Assert.Contains("DXIL", names);
        // quem mudar Common.hlsl ou RayTracing.hlsl precisa rodar tools/shaders/build-raytracing.sh
        string source = GpuWindowBench.ShaderSource("RayTracing").Replace("\r\n", "\n");
        string sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
        Assert.Equal(RtScene.RayTracingSourceSha256, sha);
    }

    [Fact]
    public void Fonte_compila_com_dxc_no_windows()
    {
        if (!OperatingSystem.IsWindows()) return;   // o DXC do pacote só funciona direito no Windows
        string source = GpuWindowBench.ShaderSource("RayTracing");
        using var result = Vortice.Dxc.DxcCompiler.Compile(source, new[] { "-T", "cs_6_5", "-E", "CSRayTrace", "-HV", "2021", "-O3" }, null!);
        Assert.True(result.GetStatus().Success, result.GetErrors());
    }

    [Fact]
    public void Etapa_de_ray_tracing_entra_no_plano_da_placa_de_video()
    {
        Assert.Contains(Dx12RayBench.Step, BenchmarkService.GpuSteps);
        Assert.Contains(Dx12RayBench.Step, BenchmarkService.StepsFor(new BenchmarkOptions(Gpu3D: true)));
        Assert.DoesNotContain(Dx12RayBench.Step, BenchmarkService.StepsFor(new BenchmarkOptions(Gpu3D: false)));
    }
}
