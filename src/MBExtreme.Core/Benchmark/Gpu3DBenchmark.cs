using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MBExtreme.Core.Progress;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace MBExtreme.Core.Benchmark;

public sealed record Gpu3DResult(string GpuName, int Score, IReadOnlyList<GpuSceneResult> Scenes);

/// <summary>
/// Benchmark 3D próprio em Direct3D 11, sem janela: renderiza cenas fora da tela na resolução escolhida e mede os quadros por segundo.
/// Cenas: fractal por raymarching (cálculo), centenas de milhares de cubos (geometria), camadas de preenchimento (pixels), um teste de
/// cálculo puro (GFLOPS) e um render de estúdio por path tracing (luz que rebate, como nos programas de modelagem 3D), que vai limpando
/// o ruído quadro a quadro. Escolhe a placa de vídeo com mais memória dedicada. Não usa marcas ou cenas de terceiros.
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class Gpu3DBenchmark
{
    /// <summary>Resolução de referência das notas: as cenas presas aos pixels são convertidas para ela, então 720p e 4K dão notas parecidas.</summary>
    public const double ReferencePixels = 1920.0 * 1080.0;

    // Tamanho fixo do queimador do Teste de estresse; o benchmark usa a resolução escolhida.
    private const int Width = 1920;
    private const int Height = 1080;

    private const string Common = @"
cbuffer Frame : register(b0) { float time; float layers; float2 res; float sampleIndex; float spp; float2 pad; };
struct VSOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; float3 col : COLOR0; };
VSOut VSFull(uint id : SV_VertexID, uint inst : SV_InstanceID)
{
    VSOut o; float2 uv = float2((id << 1) & 2, id & 2);
    o.pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1); o.uv = uv; o.col = float3(inst, 0, 0); return o;
}";

    private const string Fractal = Common + @"
float DE(float3 p)
{
    float3 z = p; float dr = 1.0; float r = 0.0;
    for (int i = 0; i < 7; i++)
    {
        r = length(z); if (r > 2.0) break;
        float theta = acos(clamp(z.z / r, -1.0, 1.0)); float phi = atan2(z.y, z.x);
        dr = pow(r, 7.0) * 8.0 * dr + 1.0;
        float zr = pow(r, 8.0); theta *= 8.0; phi *= 8.0;
        z = zr * float3(sin(theta) * cos(phi), sin(phi) * sin(theta), cos(theta)) + p;
    }
    return 0.5 * log(r) * r / dr;
}
float4 PSFractal(VSOut i) : SV_Target
{
    float2 uv = (i.uv * 2.0 - 1.0) * float2(res.x / res.y, -1.0);
    float a = time * 0.4;
    float3 ro = float3(2.6 * cos(a), 0.6, 2.6 * sin(a));
    float3 fw = normalize(-ro); float3 rt = normalize(cross(float3(0, 1, 0), fw)); float3 up = cross(fw, rt);
    float3 rd = normalize(fw * 1.6 + rt * uv.x + up * uv.y);
    float t = 0.0; int steps = 0;
    for (int s = 0; s < 90; s++) { float d = DE(ro + rd * t); if (d < 0.0008) break; t += d; steps = s; if (t > 6.0) break; }
    float g = 1.0 - steps / 90.0; float3 col = lerp(float3(0.02, 0.02, 0.05), float3(1.0, 0.8, 0.1) * g, step(t, 6.0));
    return float4(col, 1);
}";

    private const string Cubes = Common + @"
static const float3 C[8] = { float3(-1,-1,-1), float3(1,-1,-1), float3(1,1,-1), float3(-1,1,-1), float3(-1,-1,1), float3(1,-1,1), float3(1,1,1), float3(-1,1,1) };
static const int I[36] = { 0,2,1, 0,3,2, 4,5,6, 4,6,7, 0,1,5, 0,5,4, 3,6,2, 3,7,6, 0,4,7, 0,7,3, 1,2,6, 1,6,5 };
VSOut VSCubes(uint vid : SV_VertexID, uint iid : SV_InstanceID)
{
    float3 local = C[I[vid]] * 0.28;
    float3 cell = float3(iid % 64, (iid / 64) % 64, iid / 4096) - 31.5;
    float w = time + cell.x * 0.15 + cell.z * 0.11; cell.y += sin(w) * 0.6;
    float3 p = (cell + local) * 0.5;
    float a = time * 0.25; float ca = cos(a), sa = sin(a);
    float3 q = float3(p.x * ca - p.z * sa, p.y, p.x * sa + p.z * ca);
    q.z += 40.0; q.y -= 6.0;
    float aspect = res.x / res.y;
    VSOut o; o.pos = float4(q.x * 1.6 / aspect, q.y * 1.6, (q.z - 1.0) * 0.02, q.z); o.uv = 0;
    o.col = float3(0.4 + 0.6 * frac(iid * 0.00123), 0.5 + 0.5 * sin(w), 0.3 + 0.7 * frac(iid * 0.00071));
    return o;
}
float4 PSCubes(VSOut i) : SV_Target { return float4(i.col, 1); }";

    private const string Fill = Common + @"
float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
float4 PSFill(VSOut i) : SV_Target
{
    float2 p = i.uv * 8.0 + i.col.x * 0.37 + time;
    float v = 0.0; float amp = 0.5;
    for (int k = 0; k < 6; k++) { v += amp * hash(floor(p)); p *= 2.0; amp *= 0.5; }
    return float4(v * 0.04, v * 0.03, v * 0.05, 1);
}";

    /// <summary>
    /// Estúdio: chão e parede de fundo, uma luz de área no teto, uma esfera amarela laqueada, uma cromada, um cubo branco fosco
    /// e uma pequena esfera que brilha. Cada pixel lança raios que rebatem até 5 vezes (difuso e reflexo com aspereza) e a soma
    /// vai para uma textura de ponto flutuante; a resolução divide pelo número de amostras, então o ruído some com o tempo.
    /// </summary>
    private const string Studio = Common + @"
Texture2D<float4> Accum : register(t0);
uint Hash(uint s) { s = (s ^ 61u) ^ (s >> 16); s *= 9u; s = s ^ (s >> 4); s *= 0x27d4eb2du; s = s ^ (s >> 15); return s; }
float Rnd(inout uint s) { s = s * 747796405u + 2891336453u; uint w = ((s >> ((s >> 28u) + 4u)) ^ s) * 277803737u; w = (w >> 22u) ^ w; return w / 4294967296.0; }
float3 RndSphere(inout uint s) { float z = Rnd(s) * 2.0 - 1.0; float a = Rnd(s) * 6.2831853; float r = sqrt(max(0.0, 1.0 - z * z)); return float3(r * cos(a), r * sin(a), z); }
struct Hit { float t; float3 n; int m; };
void Sphere(float3 ro, float3 rd, float3 c, float r, int m, inout Hit h)
{
    float3 oc = ro - c; float b = dot(oc, rd); float k = dot(oc, oc) - r * r; float d = b * b - k;
    if (d < 0.0) return;
    float q = sqrt(d); float t = -b - q; if (t < 0.001) t = -b + q;
    if (t > 0.001 && t < h.t) { h.t = t; h.n = normalize(ro + rd * t - c); h.m = m; }
}
void Box(float3 ro, float3 rd, float3 c, float3 e, float ang, int m, inout Hit h)
{
    float ca = cos(ang), sa = sin(ang);
    float3 o = ro - c; o = float3(ca * o.x + sa * o.z, o.y, -sa * o.x + ca * o.z);
    float3 d = float3(ca * rd.x + sa * rd.z, rd.y, -sa * rd.x + ca * rd.z);
    float3 inv = 1.0 / d; float3 t0 = (-e - o) * inv; float3 t1 = (e - o) * inv;
    float3 mn = min(t0, t1); float3 mx = max(t0, t1);
    float tn = max(max(mn.x, mn.y), mn.z); float tf = min(min(mx.x, mx.y), mx.z);
    if (tn > tf || tf < 0.001) return;
    float t = tn > 0.001 ? tn : tf;
    if (t >= h.t) return;
    float3 p = (o + d * t) / e; float3 a = abs(p); float3 n;
    if (a.x > a.y && a.x > a.z) n = float3(sign(p.x), 0, 0); else if (a.y > a.z) n = float3(0, sign(p.y), 0); else n = float3(0, 0, sign(p.z));
    h.t = t; h.n = float3(ca * n.x - sa * n.z, n.y, sa * n.x + ca * n.z); h.m = m;
}
void Scene(float3 ro, float3 rd, inout Hit h)
{
    if (rd.y < 0.0) { float t = -ro.y / rd.y; if (t > 0.001 && t < h.t) { h.t = t; h.n = float3(0, 1, 0); h.m = 0; } }
    if (rd.z < 0.0) { float t = (-2.6 - ro.z) / rd.z; if (t > 0.001 && t < h.t) { h.t = t; h.n = float3(0, 0, 1); h.m = 1; } }
    if (rd.y > 0.0)
    {
        float t = (3.6 - ro.y) / rd.y; float3 p = ro + rd * t;
        if (t > 0.001 && t < h.t && abs(p.x) < 2.2 && abs(p.z + 0.2) < 1.4) { h.t = t; h.n = float3(0, -1, 0); h.m = 6; }
    }
    Sphere(ro, rd, float3(-0.95, 0.7, -0.1), 0.7, 2, h);
    Sphere(ro, rd, float3(0.95, 0.5, 0.35), 0.5, 3, h);
    Box(ro, rd, float3(0.35, 0.38, -1.1), float3(0.38, 0.38, 0.38), 0.65, 4, h);
    Sphere(ro, rd, float3(0.05, 0.16, 0.95), 0.16, 5, h);
}
void Material(int m, float3 p, out float3 albedo, out float rough, out float metal, out float3 emit)
{
    emit = 0; metal = 0; rough = 1; albedo = 0.6;
    if (m == 0) { float c = fmod(floor(p.x * 2.0) + floor(p.z * 2.0), 2.0); albedo = lerp(0.16, 0.22, abs(c)); rough = 0.6; }
    else if (m == 1) { albedo = 0.18; rough = 0.9; }
    else if (m == 2) { albedo = float3(1.0, 0.72, 0.02); rough = 0.12; }
    else if (m == 3) { albedo = float3(0.92, 0.93, 0.95); rough = 0.05; metal = 1; }
    else if (m == 4) { albedo = 0.85; rough = 1; }
    else if (m == 5) { emit = float3(6.0, 4.2, 0.6); }
    else { emit = 5.0; }
}
float3 Sky(float3 rd) { return lerp(float3(0.04, 0.04, 0.05), float3(0.32, 0.33, 0.38), saturate(rd.y * 0.5 + 0.5)); }
float3 Trace(float3 ro, float3 rd, inout uint seed)
{
    float3 acc = 0; float3 thr = 1;
    [loop] for (int b = 0; b < 5; b++)
    {
        Hit h; h.t = 1e9; h.n = 0; h.m = -1;
        Scene(ro, rd, h);
        if (h.m < 0) { acc += thr * Sky(rd); break; }
        float3 p = ro + rd * h.t; float3 n = h.n;
        float3 albedo; float rough; float metal; float3 emit;
        Material(h.m, p, albedo, rough, metal, emit);
        if (emit.x + emit.y + emit.z > 0.0) { acc += thr * emit; break; }
        float fres = metal > 0.5 ? 1.0 : 0.04 + 0.96 * pow(1.0 - saturate(dot(-rd, n)), 5.0);
        float3 dir;
        if (Rnd(seed) < fres)
        {
            dir = normalize(reflect(rd, n) + rough * RndSphere(seed));
            if (dot(dir, n) < 0.0) dir = reflect(dir, n);
            if (metal > 0.5) thr *= albedo;
        }
        else
        {
            dir = normalize(n + RndSphere(seed) * 0.999);
            thr *= albedo;
        }
        ro = p + n * 0.001; rd = dir;
        if (b > 1) { float pr = max(max(thr.x, max(thr.y, thr.z)), 0.001); if (Rnd(seed) > pr) break; thr /= pr; }
    }
    return acc;
}
float4 PSStudio(VSOut i) : SV_Target
{
    uint2 px = uint2(i.pos.xy);
    uint seed = Hash(px.x * 1973u + px.y * 9277u + uint(sampleIndex) * 26699u) | 1u;
    float2 j = float2(Rnd(seed), Rnd(seed));
    float2 uv = ((floor(i.pos.xy) + j) / res * 2.0 - 1.0) * float2(res.x / res.y, -1.0);
    float3 ro = float3(0.0, 1.25, 4.4); float3 at = float3(0.0, 0.55, 0.0);
    float3 fw = normalize(at - ro); float3 rt = normalize(cross(fw, float3(0, 1, 0))); float3 up = cross(rt, fw);
    float3 rd = normalize(fw * 2.1 + rt * uv.x + up * uv.y);
    return float4(Trace(ro, rd, seed), 1.0);
}
float4 PSResolve(VSOut i) : SV_Target
{
    float4 a = Accum.Load(int3(i.pos.xy, 0));
    float3 c = a.rgb / max(a.a, 1.0);
    c = (c * (2.51 * c + 0.03)) / (c * (2.43 * c + 0.59) + 0.14);
    return float4(pow(saturate(c), 1.0 / 2.2), 1);
}";

    private const string Compute = @"
RWBuffer<float> Out : register(u0);
[numthreads(256, 1, 1)]
void CS(uint3 id : SV_DispatchThreadID)
{
    float a = id.x * 0.001; float b = 1.0001; float c = 0.5; float d = 0.25;
    [loop] for (int i = 0; i < 2048; i++)
    {
        a = mad(a, b, c); b = mad(b, a, d); c = mad(c, d, a); d = mad(d, b, c);
        a = mad(a, b, c); b = mad(b, a, d); c = mad(c, d, a); d = mad(d, b, c);
    }
    Out[id.x] = a + b + c + d;
}";

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameConstants
    {
        public float Time, Layers, ResX, ResY;
        public float SampleIndex, Spp, Pad0, Pad1;
    }

    /// <summary>Nota de uma rodada: as cenas presas aos pixels são convertidas para 1080p; a geometria não depende da resolução.</summary>
    public static int Score(double fractalFps, double cubesFps, double fillFps, double gflops, double studioMsps, long pixels)
    {
        double scale = pixels / ReferencePixels;
        return (int)Math.Round(fractalFps * scale * 25 + cubesFps * 8 + fillFps * scale * 15 + gflops * 0.5 + studioMsps * 4);
    }

    public static Gpu3DResult Run(IProgress<ProgressEvent> progress, string step, double from, double span,
        Action<byte[], int, int>? preview, CancellationToken token, int secondsPerScene = 5, int width = 1920, int height = 1080, int studioSamples = 4)
    {
        int Width = Math.Clamp(width, 320, 7680);
        int Height = Math.Clamp(height, 240, 4320);
        var adapter = PickAdapter(out string gpuName);
        try
        {
            D3D11.D3D11CreateDevice(adapter, adapter is null ? DriverType.Hardware : DriverType.Unknown, DeviceCreationFlags.None, new[] { FeatureLevel.Level_11_0 },
                out ID3D11Device? device, out ID3D11DeviceContext? context).CheckError();
            using (device!)
            using (context!)
            {
                return RunScenes(device!, context!, gpuName, progress, step, from, span, preview, token, secondsPerScene, Width, Height, Math.Clamp(studioSamples, 1, 64));
            }
        }
        finally
        {
            adapter?.Dispose();
        }
    }

    private static IDXGIAdapter1? PickAdapter(out string name)
    {
        name = "Placa de vídeo padrão";
        try
        {
            DXGI.CreateDXGIFactory1(out IDXGIFactory1? factory).CheckError();
            using (factory!)
            {
                IDXGIAdapter1? best = null;
                ulong bestMemory = 0;
                for (uint i = 0; factory!.EnumAdapters1(i, out IDXGIAdapter1? candidate).Success; i++)
                {
                    var desc = candidate!.Description1;
                    bool software = (desc.Flags & AdapterFlags.Software) != 0;
                    if (!software && (ulong)desc.DedicatedVideoMemory >= bestMemory)
                    {
                        best?.Dispose();
                        best = candidate;
                        bestMemory = (ulong)desc.DedicatedVideoMemory;
                        name = desc.Description;
                    }
                    else
                    {
                        candidate.Dispose();
                    }
                }

                return best;
            }
        }
        catch (Exception ex) when (ex is SharpGen.Runtime.SharpGenException or DllNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private static byte[] CompileShader(string source, string entry, string profile)
    {
        var result = Compiler.Compile(source, entry, "mbx", profile, out Blob? blob, out Blob? error);
        if (result.Failure || blob is null)
            throw new InvalidOperationException("Falha ao compilar o shader: " + (error is null ? result.ToString() : System.Text.Encoding.ASCII.GetString(error.AsBytes())));
        using (blob) return blob.AsBytes().ToArray();
    }

    private static Gpu3DResult RunScenes(ID3D11Device device, ID3D11DeviceContext ctx, string gpuName, IProgress<ProgressEvent> progress,
        string step, double from, double span, Action<byte[], int, int>? preview, CancellationToken token, int seconds, int Width, int Height, int studioSamples)
    {
        const int SceneCount = BenchmarkModes.GpuScenes;
        using var target = device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)Width, Height = (uint)Height, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget,
        });
        using var depth = device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)Width, Height = (uint)Height, MipLevels = 1, ArraySize = 1, Format = Format.D32_Float,
            SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default, BindFlags = BindFlags.DepthStencil,
        });
        using var staging = device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)Width, Height = (uint)Height, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read,
        });
        using var rtv = device.CreateRenderTargetView(target);
        using var dsv = device.CreateDepthStencilView(depth);
        using var constants = device.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<FrameConstants>(), BindFlags.ConstantBuffer, ResourceUsage.Default));
        using var query = device.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));
        using var noCull = device.CreateRasterizerState(RasterizerDescription.CullNone);
        using var additive = device.CreateBlendState(BlendDescription.Additive);
        using var opaque = device.CreateBlendState(BlendDescription.Opaque);

        IntPtr eventData = Marshal.AllocHGlobal(4);
        try
        {
            return RunScenesCore();
        }
        finally
        {
            Marshal.FreeHGlobal(eventData);
        }

        void Wait()
        {
            ctx.End(query);
            ctx.Flush();
            // S_OK (0) = o trabalho terminou; S_FALSE (1) = ainda rodando; falha = placa de vídeo reiniciada (TDR).
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var state = ctx.GetData(query, eventData, 4, AsyncGetDataFlags.None);
                if (state.Failure) throw new InvalidOperationException("A placa de vídeo foi reiniciada pelo Windows durante o teste (TDR).");
                if (state.Code == 0) return;
                Thread.Yield();
            }
        }

        void Present(double elapsed, ref double lastPreview, Action? resolve = null)
        {
            if (preview is null || elapsed - lastPreview < 0.35) return;
            lastPreview = elapsed;
            resolve?.Invoke();
            ctx.CopyResource(staging, target);
            ctx.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None, out MappedSubresource mapped);
            try
            {
                var bytes = new byte[Width * Height * 4];
                for (int y = 0; y < Height; y++)
                    Marshal.Copy(mapped.DataPointer + y * (int)mapped.RowPitch, bytes, y * Width * 4, Width * 4);
                preview(bytes, Width, Height);
            }
            finally
            {
                ctx.Unmap(staging, 0);
            }
        }

        Gpu3DResult RunScenesCore()
        {
        ctx.RSSetViewport(0, 0, Width, Height);
        ctx.RSSetState(noCull);
        ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        ctx.VSSetConstantBuffer(0, constants);
        ctx.PSSetConstantBuffer(0, constants);

        var scenes = new List<GpuSceneResult>();
        double done = 0;

        // Cena 1: fractal
        double Frames(string label, Action<float> drawFrame, int index, Action? beforeEach = null)
        {
            progress.Report(new ProgressEvent(step, from + span * index / SceneCount, label));
            beforeEach?.Invoke();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            long frames = 0;
            double lastPreview = -1;
            while (watch.Elapsed.TotalSeconds < seconds)
            {
                token.ThrowIfCancellationRequested();
                float t = (float)watch.Elapsed.TotalSeconds;
                var fc = new FrameConstants { Time = t, Layers = 24, ResX = Width, ResY = Height };
                ctx.UpdateSubresource(in fc, constants);
                drawFrame(t);
                Wait();
                frames++;
                Present(watch.Elapsed.TotalSeconds, ref lastPreview);
            }

            return frames / watch.Elapsed.TotalSeconds;
        }

        using var vsFull = device.CreateVertexShader(CompileShader(Fractal, "VSFull", "vs_5_0"));
        using var psFractal = device.CreatePixelShader(CompileShader(Fractal, "PSFractal", "ps_5_0"));
        double fractalFps = Frames("Fractal (cálculo de pixels)", _ =>
        {
            ctx.OMSetRenderTargets(rtv, null);
            ctx.OMSetBlendState(opaque);
            ctx.ClearRenderTargetView(rtv, new Color4(0, 0, 0, 1));
            ctx.VSSetShader(vsFull);
            ctx.PSSetShader(psFractal);
            ctx.Draw(3, 0);
        }, 0);
        scenes.Add(new GpuSceneResult("Fractal", fractalFps));

        // Cena 2: geometria
        using var vsCubes = device.CreateVertexShader(CompileShader(Cubes, "VSCubes", "vs_5_0"));
        using var psCubes = device.CreatePixelShader(CompileShader(Cubes, "PSCubes", "ps_5_0"));
        double cubesFps = Frames("Cubos (geometria)", _ =>
        {
            ctx.OMSetRenderTargets(rtv, dsv);
            ctx.OMSetBlendState(opaque);
            ctx.ClearRenderTargetView(rtv, new Color4(0.02f, 0.02f, 0.05f, 1));
            ctx.ClearDepthStencilView(dsv, DepthStencilClearFlags.Depth, 1.0f, 0);
            ctx.VSSetShader(vsCubes);
            ctx.PSSetShader(psCubes);
            ctx.DrawInstanced(36, 64 * 64 * 24, 0, 0);
        }, 1);
        scenes.Add(new GpuSceneResult("Geometria", cubesFps));

        // Cena 3: preenchimento
        using var vsFill = device.CreateVertexShader(CompileShader(Fill, "VSFull", "vs_5_0"));
        using var psFill = device.CreatePixelShader(CompileShader(Fill, "PSFill", "ps_5_0"));
        double fillFps = Frames("Camadas (preenchimento)", _ =>
        {
            ctx.OMSetRenderTargets(rtv, null);
            ctx.OMSetBlendState(additive);
            ctx.ClearRenderTargetView(rtv, new Color4(0, 0, 0, 1));
            ctx.VSSetShader(vsFill);
            ctx.PSSetShader(psFill);
            ctx.DrawInstanced(3, 24, 0, 0);
        }, 2);
        scenes.Add(new GpuSceneResult("Preenchimento", fillFps));

        // Cena 4: cálculo puro (GFLOPS)
        const int threads = 1 << 20;
        using var buffer = device.CreateBuffer(new BufferDescription((uint)(threads * sizeof(float)), BindFlags.UnorderedAccess, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
        using var uav = device.CreateUnorderedAccessView(buffer, new UnorderedAccessViewDescription
        {
            Format = Format.R32_Float,
            ViewDimension = UnorderedAccessViewDimension.Buffer,
            Buffer = new BufferUnorderedAccessView { FirstElement = 0, NumElements = threads },
        });
        using var cs = device.CreateComputeShader(CompileShader(Compute, "CS", "cs_5_0"));
        progress.Report(new ProgressEvent(step, from + span * 3 / SceneCount, "Cálculo puro (compute)"));
        ctx.CSSetShader(cs);
        ctx.CSSetUnorderedAccessView(0, uav);
        var cWatch = System.Diagnostics.Stopwatch.StartNew();
        long dispatches = 0;
        while (cWatch.Elapsed.TotalSeconds < seconds)
        {
            token.ThrowIfCancellationRequested();
            ctx.Dispatch(threads / 256, 1, 1);
            Wait();
            dispatches++;
        }

        // 2048 iterações x 8 mad x 2 operações por thread
        double gflops = dispatches * (double)threads * 2048 * 8 * 2 / cWatch.Elapsed.TotalSeconds / 1e9;
        scenes.Add(new GpuSceneResult("Compute (GFLOPS)", gflops, "GFLOPS"));
        ctx.CSSetUnorderedAccessView(0, null);
        ctx.CSSetShader(null);

        // Cena 5: render do estúdio (path tracing progressivo)
        double studioMsps = RunStudio();
        scenes.Add(new GpuSceneResult("Estúdio 3D (path tracing)", studioMsps, "M amostras/s"));
        done = 1;
        progress.Report(new ProgressEvent(step, from + span * done, "Concluído"));

        int score = Score(fractalFps, cubesFps, fillFps, gflops, studioMsps, (long)Width * Height);
        return new Gpu3DResult(gpuName, score, scenes);

        double RunStudio()
        {
            progress.Report(new ProgressEvent(step, from + span * 4 / SceneCount, "Estúdio 3D: luz que rebate (path tracing)"));
            using var accum = device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)Width, Height = (uint)Height, MipLevels = 1, ArraySize = 1, Format = Format.R32G32B32A32_Float,
                SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            });
            using var accumRtv = device.CreateRenderTargetView(accum);
            using var accumSrv = device.CreateShaderResourceView(accum);
            using var sum = device.CreateBlendState(new BlendDescription(Blend.One, Blend.One, Blend.One, Blend.One));
            using var vsStudio = device.CreateVertexShader(CompileShader(Studio, "VSFull", "vs_5_0"));
            using var psStudio = device.CreatePixelShader(CompileShader(Studio, "PSStudio", "ps_5_0"));
            using var psResolve = device.CreatePixelShader(CompileShader(Studio, "PSResolve", "ps_5_0"));
            ctx.ClearRenderTargetView(accumRtv, new Color4(0, 0, 0, 0));
            ctx.VSSetShader(vsStudio);

            void Resolve()
            {
                ctx.OMSetRenderTargets(rtv, null);
                ctx.OMSetBlendState(opaque);
                ctx.PSSetShader(psResolve);
                ctx.PSSetShaderResource(0, accumSrv);
                ctx.Draw(3, 0);
                // Ao voltar a usar a textura como alvo, o Direct3D 11 desliga sozinho esta leitura (não há conflito).
            }

            // Uma amostra por desenho (com Flush entre eles) para nenhum desenho durar o bastante para o Windows reiniciar a placa;
            // se um quadro passar de 0,8 s, o próximo leva menos amostras.
            int spp = studioSamples;
            long samples = 0;
            double lastPreview = -1;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (watch.Elapsed.TotalSeconds < seconds)
            {
                token.ThrowIfCancellationRequested();
                double frameStart = watch.Elapsed.TotalSeconds;
                ctx.OMSetRenderTargets(accumRtv, null);
                ctx.OMSetBlendState(sum);
                ctx.PSSetShader(psStudio);
                for (int s = 0; s < spp; s++)
                {
                    var fc = new FrameConstants { Time = (float)frameStart, ResX = Width, ResY = Height, SampleIndex = samples + s, Spp = spp };
                    ctx.UpdateSubresource(in fc, constants);
                    ctx.Draw(3, 0);
                    ctx.Flush();
                }

                Wait();
                samples += spp;
                if (watch.Elapsed.TotalSeconds - frameStart > 0.8 && spp > 1) spp = Math.Max(1, spp / 2);
                Present(watch.Elapsed.TotalSeconds, ref lastPreview, Resolve);
            }

            double elapsed = watch.Elapsed.TotalSeconds;
            if (preview is not null) { lastPreview = -1; Present(1e9, ref lastPreview, Resolve); }
            return samples * (double)Width * Height / Math.Max(elapsed, 0.001) / 1e6;
        }
        }
    }
}
