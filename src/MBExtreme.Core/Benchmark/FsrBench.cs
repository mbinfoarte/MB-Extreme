using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace MBExtreme.Core.Benchmark;

/// <summary>
/// AMD FSR no teste de ray tracing (DirectX 12): a arena com RT ligado renderizada numa resolução menor e reconstruída pelo
/// FSR (Qualidade e Desempenho), e o Desempenho com geração de quadros. Cada modo roda num processo separado
/// (<c>MBExtreme.exe --bench-fsr</c>): o FSR é código nativo e, se ele falhar, só aquele processo cai e o benchmark segue.
/// </summary>
[SupportedOSPlatform("windows")]
public static class FsrBench
{
    public const string Step = "Upscaling e geração de quadros (FSR)";
    public const string Argument = "--bench-fsr";
    private const float Near = 0.1f, Far = 600f, Zoom = 1.35f;

    // ------------------------------------------------------------------ processo principal

    /// <summary>
    /// Roda os modos do FSR, cada um num processo filho, e devolve o resultado de cada um (os que falharam vêm com o erro).
    /// <paramref name="addonFolder"/>: pasta com as DLLs do FidelityFX (veja <see cref="FsrAddon"/>).
    /// </summary>
    public static IReadOnlyList<UpscaleRun> Run(int width, int height, int seconds, string addonFolder, Action<GpuProgress>? progress, CancellationToken token)
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("caminho do programa desconhecido");
        ExtractShim(addonFolder);
        var runs = new List<UpscaleRun>();
        var modes = UpscaleModes.All;
        for (int i = 0; i < modes.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var mode = modes[i];
            string output = Path.Combine(Path.GetTempPath(), $"mbx-fsr-{Environment.ProcessId}-{mode.Key}.json");
            string progressFile = output + ".progress";
            try { File.Delete(output); File.Delete(progressFile); } catch (IOException) { }
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
            foreach (var a in new[] { Argument, mode.Key, width.ToString(), height.ToString(), seconds.ToString(), addonFolder, output })
                psi.ArgumentList.Add(a);
            int index = i;
            UpscaleRun run;
            try
            {
                using var child = Process.Start(psi) ?? throw new InvalidOperationException("não abriu o processo do teste");
                var watch = Stopwatch.StartNew();
                double limit = seconds + 45;
                while (!child.WaitForExit(250))
                {
                    if (token.IsCancellationRequested || watch.Elapsed.TotalSeconds > limit)
                    {
                        try { child.Kill(true); } catch (InvalidOperationException) { }
                        token.ThrowIfCancellationRequested();
                        break;
                    }
                    var p = ReadProgress(progressFile);
                    progress?.Invoke(new GpuProgress(Step, (index + p.Fraction) / modes.Count, p.Fps, 1, 1));
                }
                run = File.Exists(output)
                    ? JsonSerializer.Deserialize<UpscaleRun>(File.ReadAllText(output)) ?? UpscaleRun.Failed(mode, "resultado vazio")
                    : UpscaleRun.Failed(mode, child.HasExited ? $"o teste fechou sem resultado (código {child.ExitCode})" : "o teste passou do tempo e foi encerrado");
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException or System.ComponentModel.Win32Exception)
            {
                run = UpscaleRun.Failed(mode, ex.Message);
            }
            finally
            {
                try { File.Delete(output); File.Delete(progressFile); } catch (IOException) { }
            }
            runs.Add(run);
            // sem FSR nesta placa (ou sem as DLLs): não adianta tentar os outros modos
            if (!run.Ok && i == 0) break;
        }
        return runs;
    }

    private static (double Fraction, double Fps) ReadProgress(string file)
    {
        try
        {
            var parts = File.ReadAllText(file).Split(';');
            if (parts.Length == 2 && double.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var f) &&
                double.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var fps)) return (f, fps);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return (0, -1);
    }

    /// <summary>Copia a ponte mbfsr.dll (embutida no programa) para a pasta do FidelityFX.</summary>
    internal static string ExtractShim(string folder)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "mbfsr.dll");
        using var stream = typeof(FsrBench).Assembly.GetManifestResourceStream("MBExtreme.Bench.mbfsr.dll")
            ?? throw new InvalidOperationException("mbfsr.dll não está no programa");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        byte[] bytes = ms.ToArray();
        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
        {
            try { File.WriteAllBytes(path, bytes); }
            catch (IOException) { /* outro processo usando: a cópia que está lá serve */ }
        }
        return path;
    }

    // ------------------------------------------------------------------ processo filho (MBExtreme.exe --bench-fsr modo largura altura segundos pasta saída)

    /// <summary>Ponto de entrada do processo filho. Sempre grava o resultado (ou o erro) em JSON antes de encerrar o FSR.</summary>
    public static int ChildMain(IReadOnlyList<string> args)
    {
        // args: --bench-fsr modo largura altura segundos pasta saída
        if (args.Count < 7) return 2;
        var mode = UpscaleModes.All.FirstOrDefault(m => m.Key == args[1]) ?? UpscaleModes.All[0];
        int width = int.Parse(args[2]), height = int.Parse(args[3]), seconds = int.Parse(args[4]);
        string folder = args[5], output = args[6];
        SetErrorMode(0x0001 | 0x0002);   // sem a janela "parou de funcionar" do Windows: se o código nativo cair, o processo só fecha
        bool loaded = false, written = false;
        void Save(UpscaleRun r) { File.WriteAllText(output, JsonSerializer.Serialize(r)); written = true; }
        // a ponte fica na pasta do FidelityFX, não ao lado do programa
        NativeLibrary.SetDllImportResolver(typeof(FsrBench).Assembly, (name, _, _) =>
            name.StartsWith("mbfsr", StringComparison.OrdinalIgnoreCase) ? NativeLibrary.Load(Path.Combine(folder, "mbfsr.dll")) : IntPtr.Zero);
        try
        {
            int rc = Native.mbfsr_load(folder);
            if (rc != 0) throw new InvalidOperationException("FSR: " + Native.Error());
            loaded = true;
            using var device = Dx12RayBench.OpenDevice(out _, out string? why) ?? throw new InvalidOperationException(why);
            using var host = new FsrHost(device, width, height, mode, CancellationToken.None);
            var stat = host.Run(Step, mode.Title, seconds, 0, 1, 0, 1, e => WriteProgress(output + ".progress", e));
            // grava antes de encerrar o FSR: se algo falhar ao fechar, o resultado já está salvo
            double shown = host.FrameGenerationOn ? 2 : 1;   // geração de quadros: 1 quadro gerado entre 2 renderizados
            Save(new UpscaleRun(mode.Key, mode.Title, true, null, stat.AvgFps * shown, stat.AvgFps, stat.Low1Fps * shown,
                stat.Low01Fps * shown, stat.FrameMs, host.RenderWidth, host.RenderHeight, host.FrameGenerationOn,
                Native.UpscalerVersion(), mode.FrameGeneration ? Native.FrameGenVersion() : null));
        }
        catch (Exception ex)
        {
            if (!written) Save(UpscaleRun.Failed(mode, ex.Message + (loaded && Native.Error() is { Length: > 0 } e ? " · " + e : "")));
            return written ? 0 : 1;
        }
        return 0;
    }

    [DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);

    private static void WriteProgress(string file, GpuProgress e)
    {
        try { File.WriteAllText(file, FormattableString.Invariant($"{e.Fraction:0.###};{e.Fps:0.#}")); }
        catch (IOException) { }
    }

    // ------------------------------------------------------------------ ponte nativa

    internal static class Native
    {
        private const string Dll = "mbfsr.dll";
        [DllImport(Dll, CharSet = CharSet.Unicode)] public static extern int mbfsr_load(string folder);
        [DllImport(Dll)] private static extern IntPtr mbfsr_error();
        [DllImport(Dll)] private static extern IntPtr mbfsr_upscaler_version();
        [DllImport(Dll)] private static extern IntPtr mbfsr_framegen_version();
        [DllImport(Dll)] public static extern int mbfsr_upscale_create(IntPtr device, uint rw, uint rh, uint dw, uint dh);
        [DllImport(Dll)] public static extern int mbfsr_jitter(int index, uint rw, uint dw, out float x, out float y);
        [DllImport(Dll)] public static extern int mbfsr_upscale(IntPtr cmd, IntPtr color, IntPtr depth, IntPtr motion, IntPtr output,
            uint rw, uint rh, float jx, float jy, float frameMs, float near, float far, float fovY, int reset);
        [DllImport(Dll)] public static extern int mbfsr_fg_swapchain(IntPtr factory, IntPtr queue, IntPtr hwnd, uint w, uint h, uint buffers, out IntPtr swapchain);
        [DllImport(Dll)] public static extern int mbfsr_fg_create(IntPtr device, IntPtr swapchain, uint dw, uint dh);
        [DllImport(Dll)] public static extern int mbfsr_fg_frame(IntPtr cmd, ulong frameId, int enabled, IntPtr depth, IntPtr motion,
            uint rw, uint rh, float jx, float jy, float frameMs, float near, float far, float fovY, float[] camera);
        [DllImport(Dll)] public static extern void mbfsr_shutdown();

        private static string Text(IntPtr p) => p == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(p) ?? "";
        public static string Error() { try { return Text(mbfsr_error()); } catch (DllNotFoundException) { return ""; } }
        public static string UpscalerVersion() => Text(mbfsr_upscaler_version());
        public static string FrameGenVersion() => Text(mbfsr_framegen_version());
        public static void Check(int rc, string what) { if (rc != 0) throw new InvalidOperationException(what + ": " + Error()); }
    }

    // ------------------------------------------------------------------ a cena com FSR

    private sealed class FsrHost : Dx12RayBench.HostBase
    {
        private readonly UpscaleMode _mode;
        private readonly ID3D12RootSignature _rayRoot, _composeRoot;
        private readonly ID3D12PipelineState _rayPso, _composePso;
        private readonly ID3D12DescriptorHeap _heap;
        private readonly ID3D12Resource _color, _depth, _motion, _upscaled, _final;
        private readonly uint _increment;
        private readonly float[] _fsrConstants = new float[8];
        private readonly float[] _camera = new float[12];
        private int _jitterIndex;
        private bool _fsrReady;
        public int RenderWidth { get; }
        public int RenderHeight { get; }
        public bool FrameGenerationOn { get; private set; }

        public FsrHost(ID3D12Device5 device, int width, int height, UpscaleMode mode, CancellationToken token)
            : base(device, width, height, token, Step + " · " + mode.Title)
        {
            _mode = mode;
            RenderWidth = Math.Max(64, (int)Math.Round(width / mode.Ratio));
            RenderHeight = Math.Max(64, (int)Math.Round(height / mode.Ratio));
            if (mode.FrameGeneration)
            {
                CreateSwapChain(factory =>
                {
                    Native.Check(Native.mbfsr_fg_swapchain(factory.NativePointer, Queue.NativePointer, Hwnd, (uint)width, (uint)height, BackBuffers, out IntPtr sc),
                        "geração de quadros");
                    return new IDXGISwapChain1(sc);
                });
            }
            else CreateSwapChain();

            byte[] ray = Dx12RayBench.Dxil("RayTracingFsr"), compose = Dx12RayBench.Dxil("RayTracingCompose");
            _rayRoot = device.CreateRootSignature(0, ray);
            _rayPso = device.CreateComputePipelineState(new ComputePipelineStateDescription { RootSignature = _rayRoot, ComputeShader = ray });
            _composeRoot = device.CreateRootSignature(0, compose);
            _composePso = device.CreateComputePipelineState(new ComputePipelineStateDescription { RootSignature = _composeRoot, ComputeShader = compose });

            ID3D12Resource Tex(Format f, int w, int h) => device.CreateCommittedResource(HeapType.Default,
                ResourceDescription.Texture2D(f, (uint)w, (uint)h, 1, 1, flags: ResourceFlags.AllowUnorderedAccess), ResourceStates.UnorderedAccess);
            _color = Tex(Format.R16G16B16A16_Float, RenderWidth, RenderHeight);
            _depth = Tex(Format.R32_Float, RenderWidth, RenderHeight);
            _motion = Tex(Format.R16G16_Float, RenderWidth, RenderHeight);
            _upscaled = Tex(Format.R16G16B16A16_Float, width, height);
            _final = Tex(Format.R8G8B8A8_UNorm, width, height);
            // 0..2: cor, profundidade e movimento (u0..u2 do raio) · 3..4: imagem final e a do FSR (u0 e u3 da composição)
            _heap = device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, 5, DescriptorHeapFlags.ShaderVisible));
            _increment = device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
            var cpu = _heap.GetCPUDescriptorHandleForHeapStart();
            var list = new[] { _color, _depth, _motion, _final, _upscaled };
            for (int i = 0; i < list.Length; i++) device.CreateUnorderedAccessView(list[i], null, null, cpu + (int)(i * _increment));

            Native.Check(Native.mbfsr_upscale_create(device.NativePointer, (uint)RenderWidth, (uint)RenderHeight, (uint)width, (uint)height), "FSR");
            if (mode.FrameGeneration)
            {
                Native.Check(Native.mbfsr_fg_create(device.NativePointer, Swap.NativePointer, (uint)width, (uint)height), "geração de quadros");
                FrameGenerationOn = true;
                ShownMultiplier = 2;
            }
            _fsrReady = true;
        }

        private static float FovY => 2f * MathF.Atan(1f / Zoom);

        private void Camera(double t, int variation)
        {
            double a = 0.6 + t * 0.12 + variation * 2.1;
            float ex = (float)(30 * Math.Cos(a)), ey = (float)(7.5 + 2 * Math.Sin(t * 0.21)), ez = (float)(30 * Math.Sin(a));
            float fx = -ex, fy = 2f - ey, fz = -ez;
            float fl = MathF.Sqrt(fx * fx + fy * fy + fz * fz); fx /= fl; fy /= fl; fz /= fl;
            // direita = frente × (0,1,0); cima = direita × frente
            float rx = -fz, ry = 0, rz = fx;
            float rl = MathF.Sqrt(rx * rx + rz * rz); rx /= rl; rz /= rl;
            float ux = ry * fz - rz * fy, uy = rz * fx - rx * fz, uz = rx * fy - ry * fx;
            float[] c = { ex, ey, ez, ux, uy, uz, rx, ry, rz, fx, fy, fz };
            c.CopyTo(_camera, 0);
        }

        protected override ID3D12Resource Record(int fi, double time, double dt, double fraction, double fps, int variation)
        {
            Native.Check(Native.mbfsr_jitter(_jitterIndex++, (uint)RenderWidth, (uint)Width, out float jx, out float jy), "jitter");
            var gpu = _heap.GetGPUDescriptorHandleForHeapStart();

            // 1) raios na resolução interna, com o subpixel deslocado
            SetFrameConstants(time, fraction, fps, RenderWidth, RenderHeight, variation);
            _fsrConstants[0] = jx; _fsrConstants[1] = jy; _fsrConstants[2] = (float)(time - dt);
            Cmd.SetPipelineState(_rayPso);
            Cmd.SetComputeRootSignature(_rayRoot);
            Cmd.SetDescriptorHeaps(_heap);
            Cmd.SetComputeRoot32BitConstants(0, Constants, 0);
            Cmd.SetComputeRoot32BitConstants(1, new uint[] { 1, 2, 2, 0 }, 0);
            Cmd.SetComputeRoot32BitConstants(2, _fsrConstants, 0);
            Cmd.SetComputeRootShaderResourceView(3, Scene.Tlas.GPUVirtualAddress);
            Cmd.SetComputeRootShaderResourceView(4, Scene.Verts.GPUVirtualAddress);
            Cmd.SetComputeRootShaderResourceView(5, Scene.Indices.GPUVirtualAddress);
            Cmd.SetComputeRootShaderResourceView(6, Scene.MeshInfo.GPUVirtualAddress);
            Cmd.SetComputeRootShaderResourceView(7, Scene.PreviousAddress(fi));
            Cmd.SetComputeRootDescriptorTable(8, gpu);
            Cmd.Dispatch((uint)(RenderWidth + 7) / 8, (uint)(RenderHeight + 7) / 8, 1);
            Cmd.ResourceBarrier(new[] { ResourceBarrier.BarrierUnorderedAccessView(_color), ResourceBarrier.BarrierUnorderedAccessView(_depth), ResourceBarrier.BarrierUnorderedAccessView(_motion) });

            // 2) FSR reconstrói na resolução da janela (o jitter vai com o sinal invertido, como no exemplo da AMD)
            float frameMs = (float)Math.Max(dt * 1000, 0.1);
            Native.Check(Native.mbfsr_upscale(Cmd.NativePointer, _color.NativePointer, _depth.NativePointer, _motion.NativePointer, _upscaled.NativePointer,
                (uint)RenderWidth, (uint)RenderHeight, -jx, -jy, frameMs, Near, Far, FovY, FrameIndex == 0 ? 1 : 0), "FSR");
            if (FrameGenerationOn)
            {
                Camera(time, variation);
                Native.Check(Native.mbfsr_fg_frame(Cmd.NativePointer, (ulong)FrameIndex, 1, _depth.NativePointer, _motion.NativePointer,
                    (uint)RenderWidth, (uint)RenderHeight, -jx, -jy, frameMs, Near, Far, FovY, _camera), "geração de quadros");
            }
            Cmd.ResourceBarrierUnorderedAccessView(_upscaled);

            // 3) gama + FPS e barra por cima, na resolução da janela (o FSR troca a assinatura e o heap: reaplica)
            SetFrameConstants(time, fraction, fps, Width, Height, variation);
            Cmd.SetPipelineState(_composePso);
            Cmd.SetComputeRootSignature(_composeRoot);
            Cmd.SetDescriptorHeaps(_heap);
            Cmd.SetComputeRoot32BitConstants(0, Constants, 0);
            Cmd.SetComputeRootDescriptorTable(1, gpu + (int)(3 * _increment));
            Cmd.Dispatch((uint)(Width + 7) / 8, (uint)(Height + 7) / 8, 1);
            return _final;
        }

        protected override void DisposeResources()
        {
            if (_fsrReady) { try { Native.mbfsr_shutdown(); } catch (SEHException) { } }
            _color.Dispose(); _depth.Dispose(); _motion.Dispose(); _upscaled.Dispose(); _final.Dispose();
            _heap.Dispose(); _rayPso.Dispose(); _composePso.Dispose(); _rayRoot.Dispose(); _composeRoot.Dispose();
        }
    }
}
