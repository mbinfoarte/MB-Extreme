using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using MBExtreme.Core.Benchmark;
using Xunit;

namespace MBExtreme.Core.Tests;

public class FsrUpscalingTests
{
    [Fact]
    public void Modos_do_fsr_na_ordem_e_com_a_escala_certa()
    {
        Assert.Equal(new[] { "quality", "performance", "performance-fg" }, UpscaleModes.All.Select(m => m.Key));
        Assert.Equal(1.5, UpscaleModes.Quality.Ratio);
        Assert.Equal(2.0, UpscaleModes.Performance.Ratio);
        Assert.True(UpscaleModes.PerformanceFrameGen.FrameGeneration);
        Assert.False(UpscaleModes.Quality.FrameGeneration);
    }

    [Fact]
    public void Ganho_contra_o_nativo_e_resultado_com_erro()
    {
        var run = new UpscaleRun("performance", "FSR Desempenho", true, null, 120, 120, 90, 70, Array.Empty<double>(), 960, 540, false, "FSR 3.1.5", null);
        Assert.Equal(2.0, run.GainOver(60), 3);
        var failed = UpscaleRun.Failed(UpscaleModes.PerformanceFrameGen, "sem suporte");
        Assert.False(failed.Ok);
        Assert.True(failed.FrameGeneration);
        Assert.Equal(0, failed.GainOver(60));
    }

    [Fact]
    public void Extracao_so_pega_os_arquivos_esperados_sem_pastas()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in FsrAddon.Files.Append("LICENSE-AMD-FidelityFX.md").Append("../fora.dll").Append("pasta/amd_fidelityfx_loader_dx12.dll").Append("outro.exe"))
                using (var w = new StreamWriter(zip.CreateEntry(name).Open())) w.Write(name);
        }
        ms.Position = 0;
        string folder = Path.Combine(Path.GetTempPath(), "mbx-fsr-teste-" + Guid.NewGuid().ToString("N"));
        try
        {
            FsrAddon.Extract(ms, folder);
            Assert.True(FsrAddon.IsReady(folder));
            Assert.Equal("amd_fidelityfx_loader_dx12.dll", File.ReadAllText(Path.Combine(folder, "amd_fidelityfx_loader_dx12.dll")));
            Assert.False(File.Exists(Path.Combine(folder, "outro.exe")));
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(folder)!, "fora.dll")));
            Assert.Equal(4, Directory.GetFiles(folder).Length);
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Hash_do_zip_confere_e_endereco_e_https_do_repositorio_de_downloads()
    {
        Assert.False(FsrAddon.HashMatches(new MemoryStream(Encoding.ASCII.GetBytes("não é o zip"))));
        Assert.StartsWith("https://raw.githubusercontent.com/mbinfoarte/MB-Extreme-Download/", FsrAddon.ZipUrl);
        Assert.Equal(64, FsrAddon.ZipSha256.Length);
        Assert.Contains(FsrAddon.SdkVersion, FsrAddon.ZipUrl);
    }

    private static byte[] Shim()
    {
        using var s = typeof(FsrBench).Assembly.GetManifestResourceStream("MBExtreme.Bench.mbfsr.dll")!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Ponte_nativa_embutida_e_dll_x64_com_as_funcoes_e_so_dependencias_do_windows()
    {
        byte[] dll = Shim();
        Assert.Equal((byte)'M', dll[0]);
        Assert.Equal((byte)'Z', dll[1]);
        int pe = BitConverter.ToInt32(dll, 0x3C);
        Assert.Equal(0x8664, BitConverter.ToUInt16(dll, pe + 4));   // AMD64
        string text = Encoding.ASCII.GetString(dll);
        foreach (var f in new[] { "mbfsr_load", "mbfsr_upscale_create", "mbfsr_upscale", "mbfsr_jitter", "mbfsr_fg_swapchain", "mbfsr_fg_create", "mbfsr_fg_frame", "mbfsr_shutdown" })
            Assert.Contains(f, text);
        Assert.DoesNotContain("libstdc++", text);
        Assert.DoesNotContain("libgcc_s", text);
        Assert.DoesNotContain("libwinpthread", text);
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryW(string path);
    [DllImport("kernel32", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)] private delegate int LoadFn(string folder);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ErrorFn();

    [Fact]
    public void Ponte_nativa_abre_no_windows_e_avisa_quando_falta_o_fidelityfx()
    {
        if (!OperatingSystem.IsWindows()) return;
        string folder = Path.Combine(Path.GetTempPath(), "mbx-fsr-ponte-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "mbfsr.dll");
        File.WriteAllBytes(path, Shim());
        IntPtr lib = LoadLibraryW(path);
        Assert.NotEqual(IntPtr.Zero, lib);
        var load = Marshal.GetDelegateForFunctionPointer<LoadFn>(GetProcAddress(lib, "mbfsr_load"));
        var error = Marshal.GetDelegateForFunctionPointer<ErrorFn>(GetProcAddress(lib, "mbfsr_error"));
        Assert.NotEqual(0, load(folder));                       // pasta sem as DLLs da AMD
        Assert.Contains("amd_fidelityfx_loader_dx12.dll", Marshal.PtrToStringUTF8(error()));
    }

    [Fact]
    public void Dxil_do_fsr_assinado_com_assinatura_raiz_e_bate_com_a_fonte()
    {
        foreach (var name in new[] { "RayTracingFsr", "RayTracingCompose" })
        {
            byte[] dxil = Dx12RayBench.Dxil(name);
            Assert.Equal("DXBC", Encoding.ASCII.GetString(dxil, 0, 4));
            Assert.Contains(dxil.Skip(4).Take(16), x => x != 0);
            int parts = BitConverter.ToInt32(dxil, 28);
            var names = Enumerable.Range(0, parts).Select(i => Encoding.ASCII.GetString(dxil, BitConverter.ToInt32(dxil, 32 + i * 4), 4)).ToList();
            Assert.Contains("RTS0", names);
        }
        string source = (GpuWindowBench.ShaderSource("RayTracing") + "\n" + GpuWindowBench.ShaderSource("RayTracingFsr", false)).Replace("\r\n", "\n");
        Assert.Equal(RtScene.FsrSourceSha256, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant());
    }

    [Fact]
    public void Fonte_do_fsr_compila_com_dxc_no_windows()
    {
        if (!OperatingSystem.IsWindows()) return;
        string source = GpuWindowBench.ShaderSource("RayTracing") + "\n" + GpuWindowBench.ShaderSource("RayTracingFsr", false);
        foreach (var entry in new[] { "CSRayTraceFsr", "CSCompose" })
        {
            using var result = Vortice.Dxc.DxcCompiler.Compile(source, new[] { "-T", "cs_6_5", "-E", entry, "-HV", "2021", "-O3" }, null!);
            Assert.True(result.GetStatus().Success, entry + ": " + result.GetErrors());
        }
    }

    [Fact]
    public void Etapa_do_fsr_vem_depois_do_ray_tracing()
    {
        var steps = BenchmarkService.StepsFor(new BenchmarkOptions(Gpu3D: true));
        Assert.Equal(Array.IndexOf(steps, Dx12RayBench.Step) + 1, Array.IndexOf(steps, FsrBench.Step));
        Assert.True(BenchmarkModes.StepSeconds(new BenchmarkOptions(Gpu3D: true, Mode: BenchmarkMode.Padrao), FsrBench.Step) > 30);
    }
}
