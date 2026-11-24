using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace MBExtreme.Core.Benchmark;

/// <summary>
/// Teste de ray tracing em DirectX 12 (DXR 1.1, raios no compute shader) numa janela separada, com o FPS no canto.
/// Roda a mesma arena duas vezes: com o ray tracing desligado (só a imagem básica) e ligado (sombras, reflexos em dois
/// quiques e oclusão de ambiente por raio). A estrutura de aceleração é refeita a cada quadro, como num jogo.
/// Placas sem DXR 1.1 (GTX 900, RX 5000 e anteriores) recebem "sem ray tracing" e o resto do benchmark continua.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Dx12RayBench
{
    public const string Step = "Ray tracing (DirectX 12)";
    public const string OffTitle = "Ray tracing desligado";
    public const string OnTitle = "Ray tracing ligado";

    /// <summary>Um sombreador já compilado para DXIL (cs_6_5), embutido no programa.</summary>
    internal static byte[] Dxil(string name)
    {
        using var stream = typeof(Dx12RayBench).Assembly.GetManifestResourceStream("MBExtreme.Bench." + name + ".dxil")
            ?? throw new InvalidOperationException("Sombreador não encontrado: " + name);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    internal static byte[] ShaderDxil() => Dxil("RayTracing");

    /// <summary>Abre o DirectX 12 na melhor placa e confere o ray tracing. Devolve null (e o motivo) quando não dá.</summary>
    internal static ID3D12Device5? OpenDevice(out string gpuName, out string? why)
    {
        why = null;
        var adapter = GpuWindowBench.PickAdapter(out gpuName);
        try
        {
            if (D3D12.D3D12CreateDevice(adapter, FeatureLevel.Level_12_0, out ID3D12Device5? device).Failure || device is null)
            {
                why = "DirectX 12 indisponível nesta placa ou driver";
                return null;
            }
            if (device.Options5.RaytracingTier < RaytracingTier.Tier1_1)
            {
                device.Dispose();
                why = "esta placa não tem ray tracing por hardware (DXR 1.1)";
                return null;
            }
            return device;
        }
        catch (Exception ex) when (ex is SharpGen.Runtime.SharpGenException or DllNotFoundException or EntryPointNotFoundException)
        {
            why = "DirectX 12 indisponível: " + ex.Message;
            return null;
        }
        finally
        {
            adapter?.Dispose();
        }
    }

    public static RayTracingResult Run(int width, int height, int seconds, int pass, int passes, Action<GpuProgress>? progress, CancellationToken token)
    {
        using var device = OpenDevice(out string gpuName, out string? why);
        if (device is null) return new RayTracingResult(gpuName, null, null, why);
        using var host = new NativeHost(device, width, height, token);
        host.RtOn = false;
        var off = host.Run(Step, OffTitle, seconds, pass, passes, 0, 2, progress);
        host.RtOn = true;
        var on = host.Run(Step, OnTitle, seconds, pass, passes, 1, 2, progress);
        return new RayTracingResult(gpuName, off, on);
    }

    /// <summary>A arena na placa: malhas (BLAS, uma vez) e a TLAS refeita a cada quadro, com as instâncias do quadro anterior guardadas para os vetores de movimento.</summary>
    internal sealed class GpuScene : IDisposable
    {
        public const int Frames = 2;
        private readonly ID3D12Device5 _device;
        public ID3D12Resource Verts { get; }
        public ID3D12Resource Indices { get; }
        public ID3D12Resource MeshInfo { get; }
        public ID3D12Resource Tlas { get; }
        private readonly ID3D12Resource _scratch;
        private readonly List<ID3D12Resource> _blas = new();
        private readonly List<BuildRaytracingAccelerationStructureInputs> _blasInputs = new();
        private readonly ulong[] _blasAddress;
        private readonly ID3D12Resource[] _instances = new ID3D12Resource[Frames];
        private readonly ID3D12Resource[] _previous = new ID3D12Resource[Frames];
        private readonly BuildRaytracingAccelerationStructureInputs _tlasInputs;
        private readonly RtInstance[] _now = new RtInstance[RtScene.InstanceCount];
        private readonly RtInstance[] _before = new RtInstance[RtScene.InstanceCount];
        private readonly float[] _prevRows = new float[RtScene.InstanceCount * 12];
        private bool _hasBefore;

        public GpuScene(ID3D12Device5 device)
        {
            _device = device;
            var meshes = RtScene.Build();
            Verts = Upload(MemoryMarshal.AsBytes(meshes.Vertices.AsSpan()));
            Indices = Upload(MemoryMarshal.AsBytes(meshes.Indices.AsSpan()));
            MeshInfo = Upload(MemoryMarshal.AsBytes(meshes.Info.AsSpan()));
            ulong scratchSize = 0;
            for (int m = 0; m < meshes.Count; m++)
            {
                uint idxStart = meshes.Info[m * 4], vtxStart = meshes.Info[m * 4 + 1], idxCount = meshes.Info[m * 4 + 2], vtxCount = meshes.Info[m * 4 + 3];
                var vb = new GpuVirtualAddressAndStride(Verts.GPUVirtualAddress + vtxStart * 32ul, 32);
                var tri = new RaytracingGeometryTrianglesDescription(vb, Format.R32G32B32_Float, vtxCount, 0,
                    Indices.GPUVirtualAddress + idxStart * 4ul, Format.R32_UInt, idxCount);
                var inputs = new BuildRaytracingAccelerationStructureInputs
                {
                    Type = RaytracingAccelerationStructureType.BottomLevel,
                    Flags = RaytracingAccelerationStructureBuildFlags.PreferFastTrace,
                    Layout = ElementsLayout.Array,
                    DescriptorsCount = 1,
                    GeometryDescriptions = new[] { new RaytracingGeometryDescription(tri, RaytracingGeometryFlags.Opaque) },
                };
                var info = device.GetRaytracingAccelerationStructurePrebuildInfo(inputs);
                _blas.Add(Buffer(info.ResultDataMaxSizeInBytes, ResourceStates.RaytracingAccelerationStructure));
                scratchSize = Math.Max(scratchSize, info.ScratchDataSizeInBytes);
                _blasInputs.Add(inputs);
            }
            _blasAddress = _blas.Select(b => b.GPUVirtualAddress).ToArray();
            _tlasInputs = new BuildRaytracingAccelerationStructureInputs
            {
                Type = RaytracingAccelerationStructureType.TopLevel,
                Flags = RaytracingAccelerationStructureBuildFlags.PreferFastBuild,
                Layout = ElementsLayout.Array,
                DescriptorsCount = (uint)RtScene.InstanceCount,
            };
            var tlasInfo = device.GetRaytracingAccelerationStructurePrebuildInfo(_tlasInputs);
            Tlas = Buffer(tlasInfo.ResultDataMaxSizeInBytes, ResourceStates.RaytracingAccelerationStructure);
            _scratch = Buffer(Math.Max(scratchSize, tlasInfo.ScratchDataSizeInBytes), ResourceStates.UnorderedAccess);
            for (int i = 0; i < Frames; i++)
            {
                _instances[i] = device.CreateCommittedResource(HeapType.Upload, ResourceDescription.Buffer((ulong)(RtScene.InstanceCount * Marshal.SizeOf<RtInstance>())), ResourceStates.GenericRead);
                _previous[i] = device.CreateCommittedResource(HeapType.Upload, ResourceDescription.Buffer((ulong)(_prevRows.Length * sizeof(float))), ResourceStates.GenericRead);
            }
        }

        private ID3D12Resource Buffer(ulong size, ResourceStates state) =>
            _device.CreateCommittedResource(HeapType.Default, ResourceDescription.Buffer(Math.Max(size, 256), ResourceFlags.AllowUnorderedAccess), state);

        private ID3D12Resource Upload(ReadOnlySpan<byte> data)
        {
            var res = _device.CreateCommittedResource(HeapType.Upload, ResourceDescription.Buffer((ulong)Math.Max(data.Length, 256)), ResourceStates.GenericRead);
            var target = res.Map<byte>(0, data.Length);
            data.CopyTo(target);
            res.Unmap(0);
            return res;
        }

        /// <summary>Grava a montagem das malhas (uma vez, antes do primeiro quadro).</summary>
        public void BuildMeshes(ID3D12GraphicsCommandList4 cmd)
        {
            for (int m = 0; m < _blasInputs.Count; m++)
            {
                cmd.BuildRaytracingAccelerationStructure(new BuildRaytracingAccelerationStructureDescription(_blas[m].GPUVirtualAddress, _blasInputs[m], 0, _scratch.GPUVirtualAddress));
                cmd.ResourceBarrierUnorderedAccessView(_scratch);
            }
        }

        /// <summary>Move as instâncias para o instante <paramref name="time"/> e grava a reconstrução da TLAS.</summary>
        public void Update(ID3D12GraphicsCommandList4 cmd, int fi, double time)
        {
            if (_hasBefore) Array.Copy(_now, _before, _now.Length);
            RtScene.Animate(_now, time, _blasAddress);
            if (!_hasBefore) { Array.Copy(_now, _before, _now.Length); _hasBefore = true; }
            var span = _instances[fi].Map<RtInstance>(0, _now.Length);
            _now.CopyTo(span);
            _instances[fi].Unmap(0);
            for (int i = 0; i < _before.Length; i++)
            {
                var b = _before[i];
                int o = i * 12;
                _prevRows[o] = b.M00; _prevRows[o + 1] = b.M01; _prevRows[o + 2] = b.M02; _prevRows[o + 3] = b.M03;
                _prevRows[o + 4] = b.M10; _prevRows[o + 5] = b.M11; _prevRows[o + 6] = b.M12; _prevRows[o + 7] = b.M13;
                _prevRows[o + 8] = b.M20; _prevRows[o + 9] = b.M21; _prevRows[o + 10] = b.M22; _prevRows[o + 11] = b.M23;
            }
            var prev = _previous[fi].Map<float>(0, _prevRows.Length);
            _prevRows.CopyTo(prev);
            _previous[fi].Unmap(0);
            _tlasInputs.InstanceDescriptions = _instances[fi].GPUVirtualAddress;
            cmd.BuildRaytracingAccelerationStructure(new BuildRaytracingAccelerationStructureDescription(Tlas.GPUVirtualAddress, _tlasInputs, 0, _scratch.GPUVirtualAddress));
            cmd.ResourceBarrierUnorderedAccessView(Tlas);
        }

        public ulong PreviousAddress(int fi) => _previous[fi].GPUVirtualAddress;

        public void Dispose()
        {
            foreach (var r in _instances) r?.Dispose();
            foreach (var r in _previous) r?.Dispose();
            _scratch.Dispose(); Tlas.Dispose();
            foreach (var b in _blas) b.Dispose();
            Verts.Dispose(); Indices.Dispose(); MeshInfo.Dispose();
        }
    }

    /// <summary>Janela, fila, swapchain e o laço de quadros com 2 quadros em voo. Cada teste grava o seu quadro em <see cref="Record"/>.</summary>
    internal abstract class HostBase : IDisposable
    {
        protected const int Frames = GpuScene.Frames, BackBuffers = 3;
        protected readonly ID3D12Device5 Device;
        protected readonly IntPtr Hwnd;
        protected readonly int Width, Height;
        protected readonly ID3D12CommandQueue Queue;
        protected IDXGISwapChain3 Swap = null!;
        private readonly ID3D12Resource[] _back = new ID3D12Resource[BackBuffers];
        private readonly ID3D12CommandAllocator[] _alloc = new ID3D12CommandAllocator[Frames];
        private readonly ulong[] _frameFence = new ulong[Frames];
        protected readonly ID3D12GraphicsCommandList4 Cmd;
        private readonly ID3D12Fence _fence;
        private ulong _fenceValue;
        private readonly CancellationToken _token;
        protected readonly GpuScene Scene;
        protected readonly float[] Constants = new float[16];
        protected int FrameIndex;
        /// <summary>Multiplica o FPS mostrado (geração de quadros: 2 quadros exibidos por quadro renderizado).</summary>
        protected double ShownMultiplier = 1;

        protected HostBase(ID3D12Device5 device, int width, int height, CancellationToken token, string title)
        {
            Device = device;
            _token = token;
            Width = width;
            Height = height;
            Hwnd = GpuWindowBench.Win32.CreateBenchWindow(width, height, "MB Bench · " + title);
            try
            {
                Queue = device.CreateCommandQueue(CommandListType.Direct);
                for (int i = 0; i < Frames; i++) _alloc[i] = device.CreateCommandAllocator(CommandListType.Direct);
                Cmd = device.CreateCommandList<ID3D12GraphicsCommandList4>(CommandListType.Direct, _alloc[0]);
                Cmd.Close();
                _fence = device.CreateFence(0);
                Scene = new GpuScene(device);
                _alloc[0].Reset();
                Cmd.Reset(_alloc[0], null);
                Scene.BuildMeshes(Cmd);
                Cmd.Close();
                Queue.ExecuteCommandList(Cmd);
                WaitIdle();
            }
            catch
            {
                GpuWindowBench.Win32.Destroy(Hwnd);
                throw;
            }
        }

        /// <summary>Cria a swapchain (chamado pelo construtor da classe filha). Por padrão, a do DXGI.</summary>
        protected void CreateSwapChain(Func<IDXGIFactory2, IDXGISwapChain1>? custom = null)
        {
            DXGI.CreateDXGIFactory2(false, out IDXGIFactory2? factory).CheckError();
            using (factory!)
            {
                IDXGISwapChain1 swap1 = custom is not null ? custom(factory) : factory.CreateSwapChainForHwnd(Queue, Hwnd,
                    new SwapChainDescription1((uint)Width, (uint)Height, Format.R8G8B8A8_UNorm, false, Usage.RenderTargetOutput, BackBuffers,
                        Scaling.Stretch, SwapEffect.FlipDiscard, AlphaMode.Ignore, SwapChainFlags.None), null, null);
                factory.MakeWindowAssociation(Hwnd, WindowAssociationFlags.IgnoreAltEnter);
                Swap = swap1.QueryInterface<IDXGISwapChain3>();
                swap1.Dispose();
            }
            for (uint i = 0; i < BackBuffers; i++) _back[i] = Swap.GetBuffer<ID3D12Resource>(i);
        }

        protected void Pump()
        {
            if (GpuWindowBench.Win32.Pump(Hwnd)) throw new OperationCanceledException("A janela do teste 3D foi fechada.");
            _token.ThrowIfCancellationRequested();
        }

        private void WaitFor(ulong value)
        {
            var watch = Stopwatch.StartNew();
            while (_fence.CompletedValue < value)
            {
                Pump();
                if (Device.DeviceRemovedReason.Failure || watch.Elapsed.TotalSeconds > 10)
                    throw new InvalidOperationException("A placa de vídeo parou de responder durante o ray tracing (TDR).");
                Thread.Yield();
            }
        }

        protected void WaitIdle()
        {
            Queue.Signal(_fence, ++_fenceValue).CheckError();
            WaitFor(_fenceValue);
        }

        protected void SetFrameConstants(double time, double fraction, double fps, int width, int height, int variation)
        {
            Array.Clear(Constants);
            Constants[0] = (float)time; Constants[1] = (float)fraction; Constants[2] = (float)fps; Constants[3] = FrameIndex;
            Constants[4] = width; Constants[5] = height; Constants[6] = 1;
            Constants[12] = variation;
        }

        /// <summary>Grava o quadro (a TLAS já foi refeita). Devolve a imagem final, em UNORDERED_ACCESS, do tamanho da janela.</summary>
        protected abstract ID3D12Resource Record(int fi, double time, double dt, double fraction, double fps, int variation);

        /// <summary>Chamado depois de gravar a cópia para a tela e antes de fechar a lista (geração de quadros).</summary>
        protected virtual void BeforeClose(int fi) { }

        private void Frame(double time, double dt, double fraction, double fps, int variation)
        {
            int fi = FrameIndex % Frames;
            WaitFor(_frameFence[fi]);
            _alloc[fi].Reset();
            Cmd.Reset(_alloc[fi], null);
            Scene.Update(Cmd, fi, time);
            var image = Record(fi, time, dt, fraction, fps, variation);
            var back = _back[Swap.CurrentBackBufferIndex];
            Cmd.ResourceBarrier(new[]
            {
                ResourceBarrier.BarrierTransition(image, ResourceStates.UnorderedAccess, ResourceStates.CopySource),
                ResourceBarrier.BarrierTransition(back, ResourceStates.Present, ResourceStates.CopyDest),
            });
            Cmd.CopyResource(back, image);
            Cmd.ResourceBarrier(new[]
            {
                ResourceBarrier.BarrierTransition(image, ResourceStates.CopySource, ResourceStates.UnorderedAccess),
                ResourceBarrier.BarrierTransition(back, ResourceStates.CopyDest, ResourceStates.Present),
            });
            BeforeClose(fi);
            Cmd.Close();
            Queue.ExecuteCommandList(Cmd);
            var result = Swap.Present(0, PresentFlags.None);
            if (result.Failure) throw new InvalidOperationException("A placa de vídeo parou de responder durante o teste (" + result.Code.ToString("X8") + ").");
            Queue.Signal(_fence, ++_fenceValue).CheckError();
            _frameFence[fi] = _fenceValue;
            FrameIndex++;
        }

        /// <summary>Uma rodada: aquece meio segundo e mede cada quadro pelo tempo pedido. <paramref name="part"/> de <paramref name="parts"/> divide a barra.</summary>
        public GpuSceneStat Run(string step, string title, int seconds, int pass, int passes, int part, int parts, Action<GpuProgress>? progress)
        {
            GpuWindowBench.Win32.SetTitle(Hwnd, $"MB Bench · {step} · {title}" + (passes > 1 ? $" · passada {pass + 1} de {passes}" : "") + " (não feche esta janela)");
            const double warmup = 0.5;
            var watch = Stopwatch.StartNew();
            var frameMs = new List<double>(8192);
            long shown = 0;
            double shownAt = 0, shownFps = -1, lastReport = -1, last = 0, prevT = 0;
            while (true)
            {
                Pump();
                double t = watch.Elapsed.TotalSeconds;
                if (t >= seconds + warmup) break;
                double fraction = Math.Max(0, t - warmup) / seconds;
                Frame(t, t - prevT, (part + fraction) / parts, shownFps, pass);
                prevT = t;
                double now = watch.Elapsed.TotalSeconds;
                if (now > warmup && last >= warmup) frameMs.Add((now - last) * 1000);
                last = now;
                shown++;
                if (now - shownAt >= 0.5) { shownFps = shown / (now - shownAt) * ShownMultiplier; shown = 0; shownAt = now; }
                if (now - lastReport >= 0.25)
                {
                    lastReport = now;
                    progress?.Invoke(new GpuProgress(step, (pass + (part + fraction) / parts) / passes, shownFps, pass + 1, passes));
                }
            }
            WaitIdle();
            double total = frameMs.Sum() / 1000;
            if (frameMs.Count < 2) return new GpuSceneStat(title, frameMs.Count / Math.Max(total, 0.001), 0, 0, frameMs);
            var m = MBExtreme.Core.Fps.FrameAnalysis.Compute(frameMs);
            return new GpuSceneStat(title, frameMs.Count / Math.Max(total, 0.001), m.Low1Fps, m.Low01Fps, MBExtreme.Core.Fps.FrameAnalysis.DownsampleMax(frameMs, 600));
        }

        protected virtual void DisposeResources() { }

        public void Dispose()
        {
            try { WaitIdle(); } catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or SharpGen.Runtime.SharpGenException) { }
            DisposeResources();
            Scene?.Dispose();
            _fence?.Dispose(); Cmd?.Dispose();
            foreach (var a in _alloc) a?.Dispose();
            foreach (var b in _back) b?.Dispose();
            Swap?.Dispose(); Queue?.Dispose();
            GpuWindowBench.Win32.Destroy(Hwnd);
        }
    }

    /// <summary>Ray tracing na resolução da janela (sem upscaling): RT desligado ou ligado.</summary>
    private sealed class NativeHost : HostBase
    {
        private readonly ID3D12RootSignature _root;
        private readonly ID3D12PipelineState _pso;
        private readonly ID3D12DescriptorHeap _heap;
        private readonly ID3D12Resource _output;
        public bool RtOn;

        public NativeHost(ID3D12Device5 device, int width, int height, CancellationToken token) : base(device, width, height, token, Step)
        {
            CreateSwapChain();
            byte[] dxil = ShaderDxil();
            _root = device.CreateRootSignature(0, dxil);    // a assinatura vem dentro do próprio DXIL
            _pso = device.CreateComputePipelineState(new ComputePipelineStateDescription { RootSignature = _root, ComputeShader = dxil });
            _output = device.CreateCommittedResource(HeapType.Default,
                ResourceDescription.Texture2D(Format.R8G8B8A8_UNorm, (uint)width, (uint)height, 1, 1, flags: ResourceFlags.AllowUnorderedAccess),
                ResourceStates.UnorderedAccess);
            _heap = device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, 1, DescriptorHeapFlags.ShaderVisible));
            device.CreateUnorderedAccessView(_output, null, null, _heap.GetCPUDescriptorHandleForHeapStart());
        }

        protected override ID3D12Resource Record(int fi, double time, double dt, double fraction, double fps, int variation)
        {
            SetFrameConstants(time, fraction, fps, Width, Height, variation);
            Cmd.SetPipelineState(_pso);
            Cmd.SetComputeRootSignature(_root);
            Cmd.SetDescriptorHeaps(_heap);
            Cmd.SetComputeRoot32BitConstants(0, Constants, 0);
            Cmd.SetComputeRoot32BitConstants(1, new uint[] { RtOn ? 1u : 0u, 2, 2, 0 }, 0);
            Cmd.SetComputeRootShaderResourceView(2, Scene.Tlas.GPUVirtualAddress);
            Cmd.SetComputeRootShaderResourceView(3, Scene.Verts.GPUVirtualAddress);
            Cmd.SetComputeRootShaderResourceView(4, Scene.Indices.GPUVirtualAddress);
            Cmd.SetComputeRootShaderResourceView(5, Scene.MeshInfo.GPUVirtualAddress);
            Cmd.SetComputeRootDescriptorTable(6, _heap.GetGPUDescriptorHandleForHeapStart());
            Cmd.Dispatch((uint)(Width + 7) / 8, (uint)(Height + 7) / 8, 1);
            return _output;
        }

        protected override void DisposeResources()
        {
            _output.Dispose(); _heap.Dispose(); _pso.Dispose(); _root.Dispose();
        }
    }
}
