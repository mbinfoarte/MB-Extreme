using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace MBExtreme.Core.Benchmark;

public enum BurnScene { Combined, Fractal, Cubes, Fill, Compute }

/// <summary>Teste de estresse da GPU: <paramref name="Scene"/> escolhe a carga (Combined alterna as quatro), <paramref name="CheckErrors"/> confere se um quadro de referência sai sempre igual.</summary>
public sealed record GpuBurnOptions(BurnScene Scene, TimeSpan Duration, bool CheckErrors = true);

public sealed record GpuBurnSample(TimeSpan Elapsed, string Scene, double Fps, double FrameMs, long Errors);

public sealed record GpuBurnResult(string GpuName, bool Completed, string Reason, TimeSpan Elapsed, long Frames, double AverageFps, double MinFps, long Errors, bool DeviceLost);

/// <summary>Quais cenas rodam e quando; e o resumo (hash) usado para conferir se a placa desenha sempre o mesmo quadro.</summary>
public static class GpuBurnPlan
{
    public const int SecondsPerScene = 10;

    public static readonly IReadOnlyList<BurnScene> Rotation = new[] { BurnScene.Fractal, BurnScene.Cubes, BurnScene.Fill, BurnScene.Compute };

    public static BurnScene Current(BurnScene chosen, TimeSpan elapsed) =>
        chosen == BurnScene.Combined ? Rotation[(int)(elapsed.TotalSeconds / SecondsPerScene) % Rotation.Count] : chosen;

    public static string Name(BurnScene scene) => scene switch
    {
        BurnScene.Fractal => "Fractal (cálculo de pixels)",
        BurnScene.Cubes => "Cubos (geometria)",
        BurnScene.Fill => "Camadas (preenchimento)",
        BurnScene.Compute => "Cálculo puro (compute)",
        _ => "Combinado",
    };

    /// <summary>FNV-1a de 64 bits: barato e suficiente para ver se dois quadros são idênticos.</summary>
    public static ulong Fnv(ReadOnlySpan<byte> data)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte b in data) hash = (hash ^ b) * 1099511628211UL;
        return hash;
    }
}

public static partial class Gpu3DBenchmark
{
    /// <summary>
    /// Carga contínua na GPU (estilo Kombustor/FurMark, com cenas nossas) até a duração, o pedido do usuário ou <paramref name="stopReason"/> devolver um motivo
    /// (por exemplo, temperatura passou do limite). Mostra a imagem pelo <paramref name="preview"/>. A cada 3 s desenha um quadro fixo e compara com o primeiro:
    /// diferença conta como erro (sinal de instabilidade). Se o Windows reiniciar a placa (TDR), o resultado vem com <c>DeviceLost</c>.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static GpuBurnResult Burn(GpuBurnOptions options, Func<string?> stopReason, Action<GpuBurnSample> onSample, Action<byte[], int, int>? preview, CancellationToken token)
    {
        var adapter = PickAdapter(out string gpuName);
        try
        {
            D3D11.D3D11CreateDevice(adapter, adapter is null ? DriverType.Hardware : DriverType.Unknown, DeviceCreationFlags.None, new[] { FeatureLevel.Level_11_0 },
                out ID3D11Device? device, out ID3D11DeviceContext? context).CheckError();
            using (device!)
            using (context!)
            {
                return BurnCore(device!, context!, gpuName, options, stopReason, onSample, preview, token);
            }
        }
        finally
        {
            adapter?.Dispose();
        }
    }

    [SupportedOSPlatform("windows")]
    private static GpuBurnResult BurnCore(ID3D11Device device, ID3D11DeviceContext ctx, string gpuName, GpuBurnOptions options, Func<string?> stopReason,
        Action<GpuBurnSample> onSample, Action<byte[], int, int>? preview, CancellationToken token)
    {
        using var target = device.CreateTexture2D(new Texture2DDescription
        {
            Width = Width, Height = Height, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget,
        });
        using var depth = device.CreateTexture2D(new Texture2DDescription
        {
            Width = Width, Height = Height, MipLevels = 1, ArraySize = 1, Format = Format.D32_Float,
            SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default, BindFlags = BindFlags.DepthStencil,
        });
        using var staging = device.CreateTexture2D(new Texture2DDescription
        {
            Width = Width, Height = Height, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read,
        });
        using var rtv = device.CreateRenderTargetView(target);
        using var dsv = device.CreateDepthStencilView(depth);
        using var constants = device.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<FrameConstants>(), BindFlags.ConstantBuffer, ResourceUsage.Default));
        using var query = device.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));
        using var noCull = device.CreateRasterizerState(RasterizerDescription.CullNone);
        using var additive = device.CreateBlendState(BlendDescription.Additive);
        using var opaque = device.CreateBlendState(BlendDescription.Opaque);

        using var vsFull = device.CreateVertexShader(CompileShader(Fractal, "VSFull", "vs_5_0"));
        using var psFractal = device.CreatePixelShader(CompileShader(Fractal, "PSFractal", "ps_5_0"));
        using var vsCubes = device.CreateVertexShader(CompileShader(Cubes, "VSCubes", "vs_5_0"));
        using var psCubes = device.CreatePixelShader(CompileShader(Cubes, "PSCubes", "ps_5_0"));
        using var vsFill = device.CreateVertexShader(CompileShader(Fill, "VSFull", "vs_5_0"));
        using var psFill = device.CreatePixelShader(CompileShader(Fill, "PSFill", "ps_5_0"));
        const int threads = 1 << 20;
        using var buffer = device.CreateBuffer(new BufferDescription((uint)(threads * sizeof(float)), BindFlags.UnorderedAccess, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
        using var uav = device.CreateUnorderedAccessView(buffer, new UnorderedAccessViewDescription
        {
            Format = Format.R32_Float,
            ViewDimension = UnorderedAccessViewDimension.Buffer,
            Buffer = new BufferUnorderedAccessView { FirstElement = 0, NumElements = threads },
        });
        using var cs = device.CreateComputeShader(CompileShader(Compute, "CS", "cs_5_0"));

        ctx.RSSetViewport(0, 0, Width, Height);
        ctx.RSSetState(noCull);
        ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        ctx.VSSetConstantBuffer(0, constants);
        ctx.PSSetConstantBuffer(0, constants);

        IntPtr eventData = Marshal.AllocHGlobal(4);
        try
        {
            void Wait()
            {
                ctx.End(query);
                ctx.Flush();
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    var state = ctx.GetData(query, eventData, 4, AsyncGetDataFlags.None);
                    if (state.Failure) throw new InvalidOperationException("A placa de vídeo foi reiniciada pelo Windows durante o teste (TDR): ela ficou instável com esta carga.");
                    if (state.Code == 0) return;
                    Thread.Yield();
                }
            }

            void Draw(BurnScene scene, float time)
            {
                var fc = new FrameConstants { Time = time, Layers = 24, ResX = Width, ResY = Height };
                ctx.UpdateSubresource(in fc, constants);
                switch (scene)
                {
                    case BurnScene.Fractal:
                        ctx.OMSetRenderTargets(rtv, null);
                        ctx.OMSetBlendState(opaque);
                        ctx.ClearRenderTargetView(rtv, new Color4(0, 0, 0, 1));
                        ctx.VSSetShader(vsFull);
                        ctx.PSSetShader(psFractal);
                        ctx.Draw(3, 0);
                        break;
                    case BurnScene.Cubes:
                        ctx.OMSetRenderTargets(rtv, dsv);
                        ctx.OMSetBlendState(opaque);
                        ctx.ClearRenderTargetView(rtv, new Color4(0.02f, 0.02f, 0.05f, 1));
                        ctx.ClearDepthStencilView(dsv, DepthStencilClearFlags.Depth, 1.0f, 0);
                        ctx.VSSetShader(vsCubes);
                        ctx.PSSetShader(psCubes);
                        ctx.DrawInstanced(36, 64 * 64 * 24, 0, 0);
                        break;
                    case BurnScene.Fill:
                        ctx.OMSetRenderTargets(rtv, null);
                        ctx.OMSetBlendState(additive);
                        ctx.ClearRenderTargetView(rtv, new Color4(0, 0, 0, 1));
                        ctx.VSSetShader(vsFill);
                        ctx.PSSetShader(psFill);
                        ctx.DrawInstanced(3, 24, 0, 0);
                        break;
                    default:
                        ctx.CSSetShader(cs);
                        ctx.CSSetUnorderedAccessView(0, uav);
                        ctx.Dispatch(threads / 256, 1, 1);
                        break;
                }
            }

            byte[] ReadFrame()
            {
                ctx.CopyResource(staging, target);
                ctx.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out MappedSubresource mapped);
                try
                {
                    var bytes = new byte[Width * Height * 4];
                    for (int y = 0; y < Height; y++)
                        Marshal.Copy(mapped.DataPointer + y * (int)mapped.RowPitch, bytes, y * Width * 4, Width * 4);
                    return bytes;
                }
                finally
                {
                    ctx.Unmap(staging, 0);
                }
            }

            var references = new Dictionary<BurnScene, ulong>();
            var watch = Stopwatch.StartNew();
            long frames = 0, errors = 0, windowFrames = 0;
            double minFps = double.MaxValue, lastSample = 0, lastPreview = -1, nextCheck = 3;
            BurnScene lastScene = BurnScene.Combined;
            string? ended = null;
            bool lost = false, canceled = false;

            try
            {
                while (watch.Elapsed < options.Duration)
                {
                    token.ThrowIfCancellationRequested();
                    if (stopReason() is { } reason) { ended = reason; break; }

                    var scene = GpuBurnPlan.Current(options.Scene, watch.Elapsed);
                    lastScene = scene;
                    Draw(scene, (float)watch.Elapsed.TotalSeconds);
                    Wait();
                    frames++;
                    windowFrames++;

                    double now = watch.Elapsed.TotalSeconds;
                    if (preview is not null && scene != BurnScene.Compute && now - lastPreview >= 0.35)
                    {
                        lastPreview = now;
                        preview(ReadFrame(), Width, Height);
                    }

                    if (options.CheckErrors && scene != BurnScene.Compute && now >= nextCheck)
                    {
                        nextCheck = now + 3;
                        Draw(scene, 1.0f);   // quadro de referência: o tempo fixo faz o desenho ser sempre o mesmo
                        Wait();
                        ulong hash = GpuBurnPlan.Fnv(ReadFrame());
                        if (!references.TryGetValue(scene, out ulong reference)) references[scene] = hash;
                        else if (reference != hash) errors++;
                    }

                    if (now - lastSample >= 1)
                    {
                        double fps = windowFrames / (now - lastSample);
                        minFps = Math.Min(minFps, fps);
                        onSample(new GpuBurnSample(watch.Elapsed, GpuBurnPlan.Name(scene), fps, 1000 / Math.Max(fps, 0.001), errors));
                        windowFrames = 0;
                        lastSample = now;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("TDR"))
            {
                lost = true;
                ended = ex.Message;
            }

            double seconds = Math.Max(watch.Elapsed.TotalSeconds, 0.001);
            bool completed = !canceled && !lost && ended is null;
            string text = canceled ? "Interrompido por você." : lost ? ended! : ended ?? $"Concluído no tempo escolhido ({GpuBurnPlan.Name(lastScene)}).";
            return new GpuBurnResult(gpuName, completed, text, watch.Elapsed, frames, frames / seconds, minFps == double.MaxValue ? 0 : minFps, errors, lost);
        }
        finally
        {
            Marshal.FreeHGlobal(eventData);
        }
    }
}
