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

    /// <summary>O sombreador já compilado para DXIL (cs_6_5), embutido no programa.</summary>
    internal static byte[] ShaderDxil()
    {
        using var stream = typeof(Dx12RayBench).Assembly.GetManifestResourceStream("MBExtreme.Bench.RayTracing.dxil")
            ?? throw new InvalidOperationException("Sombreador de ray tracing não encontrado.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public static RayTracingResult Run(int width, int height, int seconds, int pass, int passes, Action<GpuProgress>? progress, CancellationToken token)
    {
        var adapter = GpuWindowBench.PickAdapter(out string gpuName);
        ID3D12Device5? device;
        try
        {
            if (D3D12.D3D12CreateDevice(adapter, FeatureLevel.Level_12_0, out device).Failure || device is null)
                return new RayTracingResult(gpuName, null, null, "DirectX 12 indisponível nesta placa ou driver");
        }
        catch (Exception ex) when (ex is SharpGen.Runtime.SharpGenException or DllNotFoundException or EntryPointNotFoundException)
        {
            return new RayTracingResult(gpuName, null, null, "DirectX 12 indisponível: " + ex.Message);
        }
        finally
        {
            adapter?.Dispose();
        }

        using (device)
        {
            if (device.Options5.RaytracingTier < RaytracingTier.Tier1_1)
                return new RayTracingResult(gpuName, null, null, "esta placa não tem ray tracing por hardware (DXR 1.1)");
            using var host = new Host(device, width, height, token);
            var off = host.Run(OffTitle, false, seconds, pass, passes, 0, progress);
            var on = host.Run(OnTitle, true, seconds, pass, passes, 1, progress);
            return new RayTracingResult(gpuName, off, on);
        }
    }

    private sealed class Host : IDisposable
    {
        private const int Frames = 2, BackBuffers = 3;
        private readonly ID3D12Device5 _device;
        private readonly CancellationToken _token;
        private readonly IntPtr _hwnd;
        private readonly int _width, _height;
        private readonly ID3D12CommandQueue _queue;
        private readonly IDXGISwapChain3 _swap;
        private readonly ID3D12Resource[] _back = new ID3D12Resource[BackBuffers];
        private readonly ID3D12CommandAllocator[] _alloc = new ID3D12CommandAllocator[Frames];
        private readonly ulong[] _frameFence = new ulong[Frames];
        private readonly ID3D12GraphicsCommandList4 _cmd;
        private readonly ID3D12Fence _fence;
        private ulong _fenceValue;
        private readonly ID3D12RootSignature _root;
        private readonly ID3D12PipelineState _pso;
        private readonly ID3D12DescriptorHeap _heap;
        private readonly ID3D12Resource _output;
        private readonly ID3D12Resource _verts, _indices, _meshInfo;
        private readonly List<ID3D12Resource> _blas = new();
        private readonly ulong[] _blasAddress;
        private readonly ID3D12Resource _tlas, _scratch;
        private readonly ID3D12Resource[] _instances = new ID3D12Resource[Frames];
        private readonly BuildRaytracingAccelerationStructureInputs _tlasInputs;
        private readonly RtInstance[] _instanceData = new RtInstance[RtScene.InstanceCount];
        private readonly float[] _constants = new float[16];

        public Host(ID3D12Device5 device, int width, int height, CancellationToken token)
        {
            _device = device;
            _token = token;
            _width = width;
            _height = height;
            _hwnd = GpuWindowBench.Win32.CreateBenchWindow(width, height, "MB Bench · " + Step);
            try
            {
            _queue = device.CreateCommandQueue(CommandListType.Direct);
            DXGI.CreateDXGIFactory2(false, out IDXGIFactory2? factory).CheckError();
            using (factory!)
            {
                var desc = new SwapChainDescription1((uint)width, (uint)height, Format.R8G8B8A8_UNorm, false, Usage.RenderTargetOutput, BackBuffers,
                    Scaling.Stretch, SwapEffect.FlipDiscard, AlphaMode.Ignore, SwapChainFlags.None);
                using var swap1 = factory.CreateSwapChainForHwnd(_queue, _hwnd, desc, null, null);
                factory.MakeWindowAssociation(_hwnd, WindowAssociationFlags.IgnoreAltEnter);
                _swap = swap1.QueryInterface<IDXGISwapChain3>();
            }
            for (uint i = 0; i < BackBuffers; i++) _back[i] = _swap.GetBuffer<ID3D12Resource>(i);
            for (int i = 0; i < Frames; i++) _alloc[i] = device.CreateCommandAllocator(CommandListType.Direct);
            _cmd = device.CreateCommandList<ID3D12GraphicsCommandList4>(CommandListType.Direct, _alloc[0]);
            _cmd.Close();
            _fence = device.CreateFence(0);

            // sombreador e assinatura (a assinatura vem dentro do próprio DXIL)
            byte[] dxil = ShaderDxil();
            _root = device.CreateRootSignature(0, dxil);
            _pso = device.CreateComputePipelineState(new ComputePipelineStateDescription { RootSignature = _root, ComputeShader = dxil });

            // imagem de saída (escrita pelos raios e copiada para a tela)
            _output = device.CreateCommittedResource(HeapType.Default,
                ResourceDescription.Texture2D(Format.R8G8B8A8_UNorm, (uint)width, (uint)height, 1, 1, flags: ResourceFlags.AllowUnorderedAccess),
                ResourceStates.UnorderedAccess);
            _heap = device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, 1, DescriptorHeapFlags.ShaderVisible));
            device.CreateUnorderedAccessView(_output, null, null, _heap.GetCPUDescriptorHandleForHeapStart());

            // malhas na memória de envio (a placa lê direto dali; são pequenas)
            var meshes = RtScene.Build();
            _verts = Upload(MemoryMarshal.AsBytes(meshes.Vertices.AsSpan()));
            _indices = Upload(MemoryMarshal.AsBytes(meshes.Indices.AsSpan()));
            _meshInfo = Upload(MemoryMarshal.AsBytes(meshes.Info.AsSpan()));

            // BLAS de cada malha (uma vez) e TLAS (refeita a cada quadro)
            var blasInputs = new List<BuildRaytracingAccelerationStructureInputs>();
            ulong scratchSize = 0;
            for (int m = 0; m < meshes.Count; m++)
            {
                uint idxStart = meshes.Info[m * 4], vtxStart = meshes.Info[m * 4 + 1], idxCount = meshes.Info[m * 4 + 2], vtxCount = meshes.Info[m * 4 + 3];
                var vb = new GpuVirtualAddressAndStride(_verts.GPUVirtualAddress + vtxStart * 32ul, 32);
                var tri = new RaytracingGeometryTrianglesDescription(vb, Format.R32G32B32_Float, vtxCount, 0,
                    _indices.GPUVirtualAddress + idxStart * 4ul, Format.R32_UInt, idxCount);
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
                blasInputs.Add(inputs);
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
            _tlas = Buffer(tlasInfo.ResultDataMaxSizeInBytes, ResourceStates.RaytracingAccelerationStructure);
            scratchSize = Math.Max(scratchSize, tlasInfo.ScratchDataSizeInBytes);
            _scratch = Buffer(scratchSize, ResourceStates.UnorderedAccess);
            for (int i = 0; i < Frames; i++)
                _instances[i] = device.CreateCommittedResource(HeapType.Upload, ResourceDescription.Buffer((ulong)(RtScene.InstanceCount * Marshal.SizeOf<RtInstance>())), ResourceStates.GenericRead);

            _alloc[0].Reset();
            _cmd.Reset(_alloc[0], null);
            for (int m = 0; m < blasInputs.Count; m++)
            {
                _cmd.BuildRaytracingAccelerationStructure(new BuildRaytracingAccelerationStructureDescription(_blas[m].GPUVirtualAddress, blasInputs[m], 0, _scratch.GPUVirtualAddress));
                _cmd.ResourceBarrierUnorderedAccessView(_scratch);
            }
            _cmd.Close();
            _queue.ExecuteCommandList(_cmd);
            WaitIdle();
            }
            catch
            {
                // falhou ao montar o DirectX 12: fecha a janela antes de avisar
                GpuWindowBench.Win32.Destroy(_hwnd);
                throw;
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

        private void Pump()
        {
            if (GpuWindowBench.Win32.Pump(_hwnd)) throw new OperationCanceledException("A janela do teste 3D foi fechada.");
            _token.ThrowIfCancellationRequested();
        }

        private void WaitFor(ulong value)
        {
            var watch = Stopwatch.StartNew();
            while (_fence.CompletedValue < value)
            {
                Pump();
                if (_device.DeviceRemovedReason.Failure || watch.Elapsed.TotalSeconds > 10)
                    throw new InvalidOperationException("A placa de vídeo parou de responder durante o ray tracing (TDR).");
                Thread.Yield();
            }
        }

        private void WaitIdle()
        {
            _queue.Signal(_fence, ++_fenceValue).CheckError();
            WaitFor(_fenceValue);
        }

        private int _frameIndex;

        private void Frame(double time, double fraction, double fps, bool rtOn, int variation)
        {
            int fi = _frameIndex++ % Frames;
            WaitFor(_frameFence[fi]);
            _alloc[fi].Reset();
            _cmd.Reset(_alloc[fi], _pso);

            // instâncias se mexendo: a TLAS é refeita neste quadro
            RtScene.Animate(_instanceData, time, _blasAddress);
            var span = _instances[fi].Map<RtInstance>(0, _instanceData.Length);
            _instanceData.CopyTo(span);
            _instances[fi].Unmap(0);
            _tlasInputs.InstanceDescriptions = _instances[fi].GPUVirtualAddress;
            _cmd.BuildRaytracingAccelerationStructure(new BuildRaytracingAccelerationStructureDescription(_tlas.GPUVirtualAddress, _tlasInputs, 0, _scratch.GPUVirtualAddress));
            _cmd.ResourceBarrierUnorderedAccessView(_tlas);

            _constants[0] = (float)time; _constants[1] = (float)fraction; _constants[2] = (float)fps; _constants[3] = _frameIndex;
            _constants[4] = _width; _constants[5] = _height; _constants[6] = 1; _constants[7] = 0;
            _constants[12] = variation;
            _cmd.SetComputeRootSignature(_root);
            _cmd.SetDescriptorHeaps(_heap);
            _cmd.SetComputeRoot32BitConstants(0, _constants, 0);
            _cmd.SetComputeRoot32BitConstants(1, new uint[] { rtOn ? 1u : 0u, 2, 2, 0 }, 0);
            _cmd.SetComputeRootShaderResourceView(2, _tlas.GPUVirtualAddress);
            _cmd.SetComputeRootShaderResourceView(3, _verts.GPUVirtualAddress);
            _cmd.SetComputeRootShaderResourceView(4, _indices.GPUVirtualAddress);
            _cmd.SetComputeRootShaderResourceView(5, _meshInfo.GPUVirtualAddress);
            _cmd.SetComputeRootDescriptorTable(6, _heap.GetGPUDescriptorHandleForHeapStart());
            _cmd.Dispatch((uint)(_width + 7) / 8, (uint)(_height + 7) / 8, 1);

            var back = _back[_swap.CurrentBackBufferIndex];
            _cmd.ResourceBarrier(new[]
            {
                ResourceBarrier.BarrierTransition(_output, ResourceStates.UnorderedAccess, ResourceStates.CopySource),
                ResourceBarrier.BarrierTransition(back, ResourceStates.Present, ResourceStates.CopyDest),
            });
            _cmd.CopyResource(back, _output);
            _cmd.ResourceBarrier(new[]
            {
                ResourceBarrier.BarrierTransition(_output, ResourceStates.CopySource, ResourceStates.UnorderedAccess),
                ResourceBarrier.BarrierTransition(back, ResourceStates.CopyDest, ResourceStates.Present),
            });
            _cmd.Close();
            _queue.ExecuteCommandList(_cmd);
            var result = _swap.Present(0, PresentFlags.None);
            if (result.Failure) throw new InvalidOperationException("A placa de vídeo parou de responder durante o teste (" + result.Code.ToString("X8") + ").");
            _queue.Signal(_fence, ++_fenceValue).CheckError();
            _frameFence[fi] = _fenceValue;
        }

        /// <summary>Uma rodada da cena: aquece meio segundo e mede cada quadro pelo tempo pedido.</summary>
        public GpuSceneStat Run(string title, bool rtOn, int seconds, int pass, int passes, int part, Action<GpuProgress>? progress)
        {
            GpuWindowBench.Win32.SetTitle(_hwnd, $"MB Bench · {Step} · {title}" + (passes > 1 ? $" · passada {pass + 1} de {passes}" : "") + " (não feche esta janela)");
            const double warmup = 0.5;
            var watch = Stopwatch.StartNew();
            var frameMs = new List<double>(8192);
            long shown = 0;
            double shownAt = 0, shownFps = -1, lastReport = -1, last = 0;
            while (true)
            {
                Pump();
                double t = watch.Elapsed.TotalSeconds;
                if (t >= seconds + warmup) break;
                double fraction = Math.Max(0, t - warmup) / seconds;
                Frame(t, (part + fraction) / 2, shownFps, rtOn, pass);
                double now = watch.Elapsed.TotalSeconds;
                if (now > warmup && last >= warmup) frameMs.Add((now - last) * 1000);
                last = now;
                shown++;
                if (now - shownAt >= 0.5) { shownFps = shown / (now - shownAt); shown = 0; shownAt = now; }
                if (now - lastReport >= 0.25)
                {
                    lastReport = now;
                    progress?.Invoke(new GpuProgress(Step, (pass + (part + fraction) / 2) / passes, shownFps, pass + 1, passes));
                }
            }
            WaitIdle();
            double total = frameMs.Sum() / 1000;
            if (frameMs.Count < 2) return new GpuSceneStat(title, frameMs.Count / Math.Max(total, 0.001), 0, 0, frameMs);
            var m = MBExtreme.Core.Fps.FrameAnalysis.Compute(frameMs);
            return new GpuSceneStat(title, frameMs.Count / Math.Max(total, 0.001), m.Low1Fps, m.Low01Fps, MBExtreme.Core.Fps.FrameAnalysis.DownsampleMax(frameMs, 600));
        }

        public void Dispose()
        {
            try { WaitIdle(); } catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or SharpGen.Runtime.SharpGenException) { }
            foreach (var r in _instances) r?.Dispose();
            _scratch?.Dispose(); _tlas?.Dispose();
            foreach (var b in _blas) b.Dispose();
            _verts?.Dispose(); _indices?.Dispose(); _meshInfo?.Dispose();
            _output?.Dispose(); _heap?.Dispose(); _pso?.Dispose(); _root?.Dispose();
            _fence?.Dispose(); _cmd?.Dispose();
            foreach (var a in _alloc) a?.Dispose();
            foreach (var b in _back) b?.Dispose();
            _swap?.Dispose(); _queue?.Dispose();
            GpuWindowBench.Win32.Destroy(_hwnd);
        }
    }
}
