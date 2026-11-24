using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace MBExtreme.Core.Benchmark;

/// <summary>FPS de uma resolução no teste de limite por resolução.</summary>
public sealed record ResolutionFps(int Width, int Height, double Fps, double Low1Fps = 0)
{
    public string Label => $"{Width}×{Height}";

    /// <summary>Faixa de uso: Competitivo (144+), Fluido (60+), Jogável (30+) ou Abaixo de 30.</summary>
    public string Tier => Fps >= 144 ? "Competitivo 144+" : Fps >= 60 ? "Fluido 60+" : Fps >= 30 ? "Jogável 30+" : "Abaixo de 30";
}

/// <summary>Uma cena 3D: FPS médio, 1% low e 0,1% low (média dos piores quadros) e o tempo de cada quadro (reduzido para o gráfico).</summary>
public sealed record GpuSceneStat(string Scene, double AvgFps, double Low1Fps, double Low01Fps, IReadOnlyList<double> FrameMs);

/// <summary>Resultado das cenas 3D da janela separada.</summary>
public sealed record GpuWindowResult(
    string GpuName,
    GpuSceneStat? Reflections,
    GpuSceneStat? Geometry,
    double StudioMsps,
    GpuSceneStat? Game,
    IReadOnlyList<ResolutionFps> Resolutions,
    int Score,
    bool FocusLost = false)
{
    public double ReflectionsFps => Reflections?.AvgFps ?? 0;
    public double GeometryFps => Geometry?.AvgFps ?? 0;
    public double GameFps => Game?.AvgFps ?? 0;

    /// <summary>A maior resolução que passou de 60 FPS na simulação de jogo (null quando nenhuma passou).</summary>
    public ResolutionFps? Max60 => Resolutions.Where(r => r.Fps >= 60).OrderByDescending(r => (long)r.Width * r.Height).FirstOrDefault();
}

/// <summary>Progresso da janela 3D: etapa, fração dela (0 a 1), FPS atual (negativo = ainda sem) e passada.</summary>
public readonly record struct GpuProgress(string Step, double Fraction, double Fps, int Pass, int Passes);

/// <summary>
/// Benchmark 3D numa janela separada (como os programas de benchmark), com o FPS no canto e a barra de progresso embaixo.
/// Cenas próprias: Reflexos (raymarching com reflexo e sombra), Geometria (milhões de triângulos e 3 luzes),
/// Estúdio (path tracing), Simulação de jogo (terreno e dois exércitos) e o Limite por resolução (a simulação em
/// cada resolução que o monitor aguenta). Fechar a janela interrompe o teste.
/// </summary>
[SupportedOSPlatform("windows")]
public static class GpuWindowBench
{
    public const string Reflections = "Cena 1 · Reflexos";
    public const string Geometry = "Cena 2 · Geometria";
    public const string Studio = "Cena 3 · Estúdio (path tracing)";
    public const string Game = "Simulação de jogo";
    public const string ResolutionLimit = "Limite por resolução";

    private const int GeoSlices = 48, GeoStacks = 24, GeoGrid = 50, TerrainSize = 220;

    public static int Score(double reflectionsFps, double geometryFps, double studioMsps, double gameFps, long pixels)
    {
        double scale = pixels / Gpu3DBenchmark.ReferencePixels;
        return (int)Math.Round(reflectionsFps * scale * 20 + geometryFps * 2 + studioMsps * 4 + gameFps * scale * 8);
    }

    /// <summary>
    /// Roda as cenas, cada uma <paramref name="passes"/> vezes com a câmera em outro ângulo (vale a passada do meio),
    /// e depois o limite por resolução com a simulação de jogo.
    /// </summary>
    public static GpuWindowResult Run(int width, int height, int secondsPerScene, int studioSamples, IReadOnlyList<(int Width, int Height)> resolutions,
        Action<GpuProgress>? progress, CancellationToken token, bool resolutionLimit = true, int passes = 1)
    {
        passes = Math.Max(1, passes);
        using var host = new Host(width, height, token);
        GpuSceneStat Scene(string name, Func<int, GpuSceneStat> run)
        {
            var list = new List<GpuSceneStat>();
            for (int p = 0; p < passes; p++) list.Add(run(p));
            return list.OrderBy(x => x.AvgFps).ElementAt(list.Count / 2);
        }

        var refl = Scene(Reflections, p => host.RunScene(Reflections, secondsPerScene, p, passes, progress, host.DrawReflections));
        var geo = Scene(Geometry, p => host.RunScene(Geometry, secondsPerScene, p, passes, progress, host.DrawGeometry));
        var studioRuns = new List<double>();
        for (int p = 0; p < passes; p++) studioRuns.Add(host.RunStudioTiles(secondsPerScene, p, passes, progress));
        double studio = studioRuns.OrderBy(x => x).ElementAt(studioRuns.Count / 2);
        var game = Scene(Game, p => host.RunGame(Game, secondsPerScene, p, passes, progress));
        var list = new List<ResolutionFps>();
        if (resolutionLimit)
        {
            int per = Math.Max(3, secondsPerScene / 2);
            for (int i = 0; i < resolutions.Count; i++)
            {
                var (w, h) = resolutions[i];
                host.Resize(w, h);
                int index = i;
                var stat = host.RunGame($"{ResolutionLimit} · {w}×{h}", per, 0, 1,
                    e => progress?.Invoke(new GpuProgress(ResolutionLimit, (index + e.Fraction) / resolutions.Count, e.Fps, 1, 1)));
                list.Add(new ResolutionFps(w, h, stat.AvgFps, stat.Low1Fps));
            }
        }
        progress?.Invoke(new GpuProgress(ResolutionLimit, 1, -1, 1, 1));
        return new GpuWindowResult(host.GpuName, refl, geo, studio, game, list,
            Score(refl.AvgFps, geo.AvgFps, studio, game.AvgFps, (long)width * height), host.FocusLost);
    }

    /// <summary>Multiplicação de matrizes 1024 x 1024 num compute shader (etapa de IA). Devolve GFLOPS.</summary>
    public static double GemmGflops(int milliseconds, CancellationToken token)
    {
        const int n = 1024;
        var adapter = PickAdapter(out _);
        try
        {
            D3D11.D3D11CreateDevice(adapter, adapter is null ? DriverType.Hardware : DriverType.Unknown, DeviceCreationFlags.None, new[] { FeatureLevel.Level_11_0 },
                out ID3D11Device? device, out ID3D11DeviceContext? ctx).CheckError();
            using (device!)
            using (ctx!)
            {
                var data = new float[n * n];
                var rng = new Random(9);
                for (int i = 0; i < data.Length; i++) data[i] = (float)rng.NextDouble() - 0.5f;
                using var a = device!.CreateBuffer(data, BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 0, sizeof(float));
                using var b = device.CreateBuffer(data, BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 0, sizeof(float));
                using var c = device.CreateBuffer(data, BindFlags.UnorderedAccess, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 0, sizeof(float));
                using var srvA = device.CreateShaderResourceView(a);
                using var srvB = device.CreateShaderResourceView(b);
                using var uav = device.CreateUnorderedAccessView(c);
                using var cs = device.CreateComputeShader(Gpu3DBenchmark.CompileShader(ShaderSource("AiGemm", false), "CSGemm", "cs_5_0"));
                using var query = device.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));
                ctx!.CSSetShader(cs);
                ctx.CSSetShaderResource(0, srvA);
                ctx.CSSetShaderResource(1, srvB);
                ctx.CSSetUnorderedAccessView(0, uav);
                IntPtr flag = Marshal.AllocHGlobal(4);
                try
                {
                    void Wait()
                    {
                        ctx.End(query);
                        ctx.Flush();
                        while (true)
                        {
                            token.ThrowIfCancellationRequested();
                            var state = ctx.GetData(query, flag, 4, AsyncGetDataFlags.None);
                            if (state.Failure) throw new InvalidOperationException("A placa de vídeo foi reiniciada pelo Windows durante o teste (TDR).");
                            if (state.Code == 0) return;
                            Thread.Yield();
                        }
                    }

                    ctx.Dispatch(n / 16, n / 16, 1);
                    Wait();
                    long runs = 0;
                    var watch = Stopwatch.StartNew();
                    while (watch.ElapsedMilliseconds < milliseconds)
                    {
                        ctx.Dispatch(n / 16, n / 16, 1);
                        ctx.Dispatch(n / 16, n / 16, 1);
                        Wait();
                        runs += 2;
                    }
                    return runs * 2.0 * n * n * n / watch.Elapsed.TotalSeconds / 1e9;
                }
                finally
                {
                    Marshal.FreeHGlobal(flag);
                }
            }
        }
        finally
        {
            adapter?.Dispose();
        }
    }

    // ------------------------------------------------------------------ sombreadores (arquivos .hlsl embutidos)

    internal static string ShaderSource(string name, bool withCommon = true)
    {
        string Read(string n)
        {
            using var stream = typeof(GpuWindowBench).Assembly.GetManifestResourceStream("MBExtreme.Bench." + n + ".hlsl")
                ?? throw new InvalidOperationException("Sombreador não encontrado: " + n);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        return withCommon ? Read("Common") + "\n" + Read(name) : Read(name);
    }

    internal static IDXGIAdapter1? PickAdapter(out string name)
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
                    if ((desc.Flags & AdapterFlags.Software) == 0 && (ulong)desc.DedicatedVideoMemory >= bestMemory)
                    {
                        best?.Dispose();
                        best = candidate;
                        bestMemory = (ulong)desc.DedicatedVideoMemory;
                        name = desc.Description;
                    }
                    else candidate.Dispose();
                }
                return best;
            }
        }
        catch (Exception ex) when (ex is SharpGen.Runtime.SharpGenException or DllNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameConstants
    {
        public float Time, Progress, Fps, FrameIndex;
        public float ResX, ResY, Spp, Units;
        public float TileX, TileY, TileSize, TileOn;
        public float Variation, Sparks, Pad0, Pad1;
    }

    // ------------------------------------------------------------------ janela + Direct3D

    private sealed class Host : IDisposable
    {
        private readonly CancellationToken _token;
        private readonly IntPtr _hwnd;
        private readonly ID3D11Device _device;
        private readonly ID3D11DeviceContext _ctx;
        private readonly IDXGISwapChain1 _swap;
        private ID3D11RenderTargetView _rtv = null!;
        private ID3D11Texture2D _depth = null!;
        private ID3D11DepthStencilView _dsv = null!;
        private readonly ID3D11Buffer _constants;
        private readonly ID3D11RasterizerState _noCull;
        private readonly ID3D11BlendState _opaque, _alpha, _sum;
        private readonly ID3D11DepthStencilState _depthOn, _depthOff, _depthRead;
        private readonly ID3D11BlendState _additive;
        private readonly ID3D11RasterizerState _scissor;
        private readonly ID3D11VertexShader _vsFull;
        private readonly ID3D11PixelShader _psOverlay;
        private readonly Dictionary<string, ID3D11DeviceChild> _shaders = new();
        private int _width, _height;
        private FrameConstants _frame;

        public string GpuName { get; }

        public Host(int width, int height, CancellationToken token)
        {
            _token = token;
            _width = width;
            _height = height;
            var adapter = PickAdapter(out string name);
            GpuName = name;
            try
            {
                D3D11.D3D11CreateDevice(adapter, adapter is null ? DriverType.Hardware : DriverType.Unknown, DeviceCreationFlags.BgraSupport, new[] { FeatureLevel.Level_11_0 },
                    out ID3D11Device? device, out ID3D11DeviceContext? ctx).CheckError();
                _device = device!;
                _ctx = ctx!;
            }
            finally
            {
                adapter?.Dispose();
            }

            _hwnd = Win32.CreateBenchWindow(width, height, "MB Bench");
            using (var dxgiDevice = _device.QueryInterface<IDXGIDevice>())
            using (var dxgiAdapter = dxgiDevice.GetAdapter())
            using (var factory = dxgiAdapter.GetParent<IDXGIFactory2>())
            {
                var desc = new SwapChainDescription1((uint)width, (uint)height, Format.B8G8R8A8_UNorm, false, Usage.RenderTargetOutput, 2,
                    Scaling.Stretch, SwapEffect.FlipDiscard, AlphaMode.Ignore, SwapChainFlags.None);
                _swap = factory.CreateSwapChainForHwnd(_device, _hwnd, desc, null, null);
                factory.MakeWindowAssociation(_hwnd, WindowAssociationFlags.IgnoreAltEnter);
            }
            CreateTargets();

            _constants = _device.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<FrameConstants>(), BindFlags.ConstantBuffer, ResourceUsage.Default));
            _noCull = _device.CreateRasterizerState(RasterizerDescription.CullNone);
            _opaque = _device.CreateBlendState(BlendDescription.Opaque);
            _alpha = _device.CreateBlendState(BlendDescription.NonPremultiplied);
            _sum = _device.CreateBlendState(new BlendDescription(Blend.One, Blend.One, Blend.One, Blend.One));
            _depthOn = _device.CreateDepthStencilState(DepthStencilDescription.Default);
            _depthOff = _device.CreateDepthStencilState(DepthStencilDescription.None);
            _depthRead = _device.CreateDepthStencilState(DepthStencilDescription.DepthRead);
            _additive = _device.CreateBlendState(new BlendDescription(Blend.SourceAlpha, Blend.One, Blend.One, Blend.One));
            _scissor = _device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid) { ScissorEnable = true });
            string common = ShaderSource("Reflections");
            _vsFull = _device.CreateVertexShader(Gpu3DBenchmark.CompileShader(common, "VSFull", "vs_5_0"));
            _psOverlay = _device.CreatePixelShader(Gpu3DBenchmark.CompileShader(common, "PSOverlay", "ps_5_0"));
        }

        private void CreateTargets()
        {
            using (var back = _swap.GetBuffer<ID3D11Texture2D>(0)) _rtv = _device.CreateRenderTargetView(back);
            _depth = _device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)_width, Height = (uint)_height, MipLevels = 1, ArraySize = 1, Format = Format.D32_Float,
                SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default, BindFlags = BindFlags.DepthStencil,
            });
            _dsv = _device.CreateDepthStencilView(_depth);
        }

        public void Resize(int width, int height)
        {
            _ctx.UnsetRenderTargets();
            _rtv.Dispose();
            _dsv.Dispose();
            _depth.Dispose();
            _ctx.Flush();
            _width = width;
            _height = height;
            _swap.ResizeBuffers(2, (uint)width, (uint)height, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();
            CreateTargets();
        }

        private T Shader<T>(string file, string entry, string profile) where T : ID3D11DeviceChild
        {
            string key = file + "|" + entry;
            if (_shaders.TryGetValue(key, out var existing)) return (T)existing;
            byte[] code = Gpu3DBenchmark.CompileShader(ShaderSource(file), entry, profile);
            ID3D11DeviceChild created = profile.StartsWith("vs", StringComparison.Ordinal)
                ? _device.CreateVertexShader(code)
                : _device.CreatePixelShader(code);
            _shaders[key] = created;
            return (T)created;
        }

        private void Begin(bool depth, Color4 clear)
        {
            _ctx.RSSetViewport(0, 0, _width, _height);
            _ctx.RSSetState(_noCull);
            _ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _ctx.VSSetConstantBuffer(0, _constants);
            _ctx.PSSetConstantBuffer(0, _constants);
            _ctx.OMSetRenderTargets(_rtv, depth ? _dsv : null);
            _ctx.ClearRenderTargetView(_rtv, clear);
            if (depth) _ctx.ClearDepthStencilView(_dsv, DepthStencilClearFlags.Depth, 1.0f, 0);
            _ctx.OMSetBlendState(_opaque);
        }

        private void FullScreen(ID3D11PixelShader ps, bool depthOff = true)
        {
            if (depthOff) _ctx.OMSetDepthStencilState(_depthOff);
            _ctx.VSSetShader(_vsFull);
            _ctx.PSSetShader(ps);
            _ctx.Draw(3, 0);
        }

        public void DrawReflections()
        {
            Begin(false, new Color4(0, 0, 0, 1));
            FullScreen(Shader<ID3D11PixelShader>("Reflections", "PSReflections", "ps_5_0"));
        }

        public void DrawGeometry()
        {
            Begin(true, new Color4(0, 0, 0, 1));
            FullScreen(Shader<ID3D11PixelShader>("Geometry", "PSBackdrop", "ps_5_0"));
            _ctx.OMSetDepthStencilState(_depthOn);
            _ctx.VSSetShader(Shader<ID3D11VertexShader>("Geometry", "VSGeometry", "vs_5_0"));
            _ctx.PSSetShader(Shader<ID3D11PixelShader>("Geometry", "PSGeometry", "ps_5_0"));
            _ctx.DrawInstanced(GeoSlices * GeoStacks * 6, GeoGrid * GeoGrid, 0, 0);
        }

        private void Overlay()
        {
            _ctx.OMSetRenderTargets(_rtv, null);
            _ctx.OMSetBlendState(_alpha);
            FullScreen(_psOverlay);
        }

        private void Pump()
        {
            if (Win32.Pump(_hwnd)) throw new OperationCanceledException("A janela do teste 3D foi fechada.");
            _token.ThrowIfCancellationRequested();
        }

        private void Upload(double time, double fraction, double fps, double frame, double spp = 1)
        {
            _frame.Time = (float)time;
            _frame.Progress = (float)Math.Clamp(fraction, 0, 1);
            _frame.Fps = (float)fps;
            _frame.FrameIndex = (float)frame;
            _frame.ResX = _width;
            _frame.ResY = _height;
            _frame.Spp = (float)spp;
            _frame.Units = UnitsPerArmy;
            _ctx.UpdateSubresource(in _frame, _constants);
        }

        private void Present()
        {
            var result = _swap.Present(0, PresentFlags.None);
            if (result.Failure) throw new InvalidOperationException("A placa de vídeo parou de responder durante o teste (" + result.Code.ToString("X8") + ").");
        }

        private double _unfocused;
        public bool FocusLost => _unfocused > 0.5;

        private static GpuSceneStat Stats(string scene, List<double> frameMs, double seconds)
        {
            if (frameMs.Count < 2) return new GpuSceneStat(scene, frameMs.Count / Math.Max(seconds, 0.001), 0, 0, frameMs);
            var m = MBExtreme.Core.Fps.FrameAnalysis.Compute(frameMs);
            return new GpuSceneStat(scene, frameMs.Count / Math.Max(seconds, 0.001), m.Low1Fps, m.Low01Fps, MBExtreme.Core.Fps.FrameAnalysis.DownsampleMax(frameMs, 600));
        }

        /// <summary>
        /// Laço de uma cena: aquece meio segundo, depois mede cada quadro (tempo entre um e outro) pelo tempo pedido.
        /// <paramref name="beforeDraw"/> roda antes de desenhar (a simulação de jogo calcula as unidades ali, no processador).
        /// </summary>
        private GpuSceneStat Loop(string title, string step, int seconds, int pass, int passes, Action<GpuProgress>? progress, Action draw, Action<double, double>? beforeDraw = null)
        {
            Win32.SetTitle(_hwnd, $"MB Bench · {title}" + (passes > 1 ? $" · passada {pass + 1} de {passes}" : "") + " (não feche esta janela)");
            _frame.Variation = pass;
            const double warmup = 0.5;
            var watch = Stopwatch.StartNew();
            var frameMs = new List<double>(8192);
            long shownFrames = 0;
            double shownAt = 0, shownFps = -1, lastReport = -1, last = 0;
            while (true)
            {
                Pump();
                double t = watch.Elapsed.TotalSeconds;
                if (t >= seconds + warmup) break;
                double fraction = Math.Max(0, t - warmup) / seconds;
                beforeDraw?.Invoke(t, t - last);
                Upload(t, fraction, shownFps, frameMs.Count);
                draw();
                Overlay();
                Present();
                double now = watch.Elapsed.TotalSeconds;
                if (now > warmup && last >= warmup) frameMs.Add((now - last) * 1000);
                if (now > warmup && !Win32.IsForeground(_hwnd)) _unfocused += now - last;
                last = now;
                shownFrames++;
                if (now - shownAt >= 0.5) { shownFps = shownFrames / (now - shownAt); shownFrames = 0; shownAt = now; }
                if (now - lastReport >= 0.25) { lastReport = now; progress?.Invoke(new GpuProgress(step, (pass + fraction) / passes, shownFps, pass + 1, passes)); }
            }
            _ctx.Flush();
            return Stats(title, frameMs, frameMs.Sum() / 1000);
        }

        public GpuSceneStat RunScene(string title, int seconds, int pass, int passes, Action<GpuProgress>? progress, Action draw) =>
            Loop(title, title, seconds, pass, passes, progress, draw);

        // ---- simulação de jogo: unidades calculadas no processador a cada quadro
        public const int UnitsPerArmy = 5000;
        private ID3D11Buffer? _unitBuffer, _sparkBuffer;
        private ID3D11ShaderResourceView? _unitView, _sparkView;
        private readonly float[] _unitData = new float[UnitsPerArmy * 2 * 4];

        private void EnsureGameBuffers()
        {
            if (_unitBuffer is not null) return;
            _unitBuffer = _device.CreateBuffer(new BufferDescription((uint)(_unitData.Length * sizeof(float)), BindFlags.ShaderResource, ResourceUsage.Dynamic, CpuAccessFlags.Write, ResourceOptionFlags.BufferStructured, 16));
            _sparkBuffer = _device.CreateBuffer(new BufferDescription((uint)(ArmySim.MaxSparks * 4 * sizeof(float)), BindFlags.ShaderResource, ResourceUsage.Dynamic, CpuAccessFlags.Write, ResourceOptionFlags.BufferStructured, 16));
            _unitView = _device.CreateShaderResourceView(_unitBuffer);
            _sparkView = _device.CreateShaderResourceView(_sparkBuffer);
        }

        private void Upload(ID3D11Buffer buffer, ReadOnlySpan<float> data)
        {
            var mapped = _ctx.Map(buffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
            try { Marshal.Copy(data.ToArray(), 0, mapped.DataPointer, data.Length); }
            finally { _ctx.Unmap(buffer, 0); }
        }

        public GpuSceneStat RunGame(string title, int seconds, int pass, int passes, Action<GpuProgress>? progress)
        {
            EnsureGameBuffers();
            var sim = new ArmySim(UnitsPerArmy, 7 + pass);
            return Loop(title, title.StartsWith(ResolutionLimit, StringComparison.Ordinal) ? ResolutionLimit : Game, seconds, pass, passes, progress, DrawGame, (t, dt) =>
            {
                sim.Step((float)dt, (float)t);
                sim.WriteUnits(_unitData);
                Upload(_unitBuffer!, _unitData);
                Upload(_sparkBuffer!, sim.Sparks);
                _frame.Sparks = ArmySim.MaxSparks;
            });
        }

        public void DrawGame()
        {
            Begin(true, new Color4(0, 0, 0, 1));
            FullScreen(Shader<ID3D11PixelShader>("Game", "PSSky", "ps_5_0"));
            _ctx.OMSetDepthStencilState(_depthOn);
            // as explosões iluminam o terreno, os soldados e o lago
            _ctx.VSSetShaderResource(0, _unitView!);
            _ctx.VSSetShaderResource(1, _sparkView!);
            _ctx.PSSetShaderResource(1, _sparkView!);
            var ps = Shader<ID3D11PixelShader>("Game", "PSGame", "ps_5_0");
            _ctx.VSSetShader(Shader<ID3D11VertexShader>("Game", "VSTerrain", "vs_5_0"));
            _ctx.PSSetShader(ps);
            _ctx.Draw((TerrainSize - 1) * (TerrainSize - 1) * 6, 0);
            // soldados em 3D: 6 partes com 36 vértices cada
            _ctx.VSSetShader(Shader<ID3D11VertexShader>("Game", "VSUnits", "vs_5_0"));
            _ctx.DrawInstanced(216, UnitsPerArmy * 2, 0, 0);
            // lago com reflexo do terreno e do céu
            _ctx.VSSetShader(Shader<ID3D11VertexShader>("Game", "VSWater", "vs_5_0"));
            _ctx.PSSetShader(Shader<ID3D11PixelShader>("Game", "PSWater", "ps_5_0"));
            _ctx.Draw(6, 0);
            // fumaça: mistura comum, lendo a profundidade sem gravar
            _ctx.OMSetDepthStencilState(_depthRead);
            _ctx.OMSetBlendState(_alpha);
            _ctx.VSSetShader(Shader<ID3D11VertexShader>("Game", "VSSmoke", "vs_5_0"));
            _ctx.PSSetShader(Shader<ID3D11PixelShader>("Game", "PSSmoke", "ps_5_0"));
            _ctx.DrawInstanced(6, ArmySim.MaxSparks * 14, 0, 0);
            // fogo, faíscas e disparos de energia: luz somada
            _ctx.OMSetBlendState(_additive);
            _ctx.VSSetShader(Shader<ID3D11VertexShader>("Game", "VSSparks", "vs_5_0"));
            _ctx.PSSetShader(Shader<ID3D11PixelShader>("Game", "PSSparks", "ps_5_0"));
            _ctx.DrawInstanced(6, ArmySim.MaxSparks * 26, 0, 0);
            _ctx.VSSetShader(Shader<ID3D11VertexShader>("Game", "VSBeams", "vs_5_0"));
            _ctx.PSSetShader(Shader<ID3D11PixelShader>("Game", "PSBeams", "ps_5_0"));
            _ctx.DrawInstanced(6, 1200, 0, 0);
            _ctx.OMSetBlendState(_opaque);
        }

        // ---- estúdio em blocos (o render vai aparecendo quadrado por quadrado)
        private ID3D11Query? _event;
        private IntPtr _eventData;

        private void WaitGpu()
        {
            _event ??= _device.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));
            if (_eventData == IntPtr.Zero) _eventData = Marshal.AllocHGlobal(4);
            _ctx.End(_event);
            _ctx.Flush();
            while (true)
            {
                Pump();
                var state = _ctx.GetData(_event, _eventData, 4, AsyncGetDataFlags.None);
                if (state.Failure) throw new InvalidOperationException("A placa de vídeo foi reiniciada pelo Windows durante o teste (TDR).");
                if (state.Code == 0) return;
                Thread.Yield();
            }
        }

        public double RunStudioTiles(int seconds, int pass, int passes, Action<GpuProgress>? progress)
        {
            Win32.SetTitle(_hwnd, $"MB Bench · {Studio}" + (passes > 1 ? $" · passada {pass + 1} de {passes}" : "") + " (não feche esta janela)");
            _frame.Variation = pass;
            const int size = 64;
            using var accum = _device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)_width, Height = (uint)_height, MipLevels = 1, ArraySize = 1, Format = Format.R32G32B32A32_Float,
                SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            });
            using var accumRtv = _device.CreateRenderTargetView(accum);
            using var accumSrv = _device.CreateShaderResourceView(accum);
            var psTile = Shader<ID3D11PixelShader>("Studio", "PSStudioTile", "ps_5_0");
            var psResolve = Shader<ID3D11PixelShader>("Studio", "PSResolveTiles", "ps_5_0");
            _ctx.ClearRenderTargetView(accumRtv, new Color4(0, 0, 0, 0));

            // ordem dos quadrados: do centro para fora
            var tiles = new List<(int X, int Y)>();
            for (int y = 0; y < _height; y += size)
                for (int x = 0; x < _width; x += size) tiles.Add((x, y));
            double cx = _width / 2.0, cy = _height / 2.0;
            tiles = tiles.OrderBy(t => Math.Pow(t.X + size / 2.0 - cx, 2) + Math.Pow(t.Y + size / 2.0 - cy, 2)).ToList();

            void DrawTile((int X, int Y) tile, int spp, long seed)
            {
                _ctx.RSSetViewport(0, 0, _width, _height);
                _ctx.RSSetState(_scissor);
                _ctx.RSSetScissorRect(new Vortice.RawRect(tile.X, tile.Y, tile.X + size, tile.Y + size)); // esquerda, topo, direita, base
                _ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                _ctx.VSSetConstantBuffer(0, _constants);
                _ctx.PSSetConstantBuffer(0, _constants);
                _ctx.OMSetRenderTargets(accumRtv, null);
                _ctx.OMSetBlendState(_sum);
                Upload(0, 0, -1, seed, spp);
                FullScreen(psTile);
            }

            // calibra: quantas amostras por pixel cabem no tempo pedido
            var calib = Stopwatch.StartNew();
            DrawTile(tiles[0], 16, 1);
            WaitGpu();
            double perSample = calib.Elapsed.TotalSeconds / (16.0 * size * size);
            int spp = (int)Math.Clamp(Math.Pow(2, Math.Round(Math.Log2(Math.Max(1, seconds / (perSample * _width * _height))))), 4, 2048);
            _ctx.ClearRenderTargetView(accumRtv, new Color4(0, 0, 0, 0));

            long samples = 0;
            var watch = Stopwatch.StartNew();
            double lastShow = -1;
            for (int i = 0; i < tiles.Count; i++)
            {
                Pump();
                var tileStart = watch.Elapsed.TotalSeconds;
                DrawTile(tiles[i], spp, 1000 + i * 7919L);
                samples += (long)spp * Math.Min(size, _width - tiles[i].X) * Math.Min(size, _height - tiles[i].Y);
                double t = watch.Elapsed.TotalSeconds;
                if (t - lastShow > 0.03 || i == tiles.Count - 1)
                {
                    lastShow = t;
                    _frame.TileX = i + 1 < tiles.Count ? tiles[i + 1].X : 0;
                    _frame.TileY = i + 1 < tiles.Count ? tiles[i + 1].Y : 0;
                    _frame.TileSize = size;
                    _frame.TileOn = i + 1 < tiles.Count ? 1 : 0;
                    _ctx.RSSetState(_noCull);
                    Upload(t, (pass + (i + 1.0) / tiles.Count) / passes, -1, 0, spp);
                    _ctx.OMSetRenderTargets(_rtv, null);
                    _ctx.OMSetBlendState(_opaque);
                    _ctx.PSSetShaderResource(0, accumSrv);
                    FullScreen(psResolve);
                    Overlay();
                    Present();
                    progress?.Invoke(new GpuProgress(Studio, (pass + (i + 1.0) / tiles.Count) / passes, -1, pass + 1, passes));
                }
                if (watch.Elapsed.TotalSeconds - tileStart > 1.0 && spp > 4) spp /= 2; // proteção contra a placa travar
            }
            WaitGpu();
            _frame.TileOn = 0;
            double msps = samples / Math.Max(watch.Elapsed.TotalSeconds, 0.001) / 1e6;
            // deixa a imagem pronta na tela por um instante
            var pause = Stopwatch.StartNew();
            while (pause.Elapsed.TotalSeconds < 0.8) { Pump(); Thread.Sleep(15); }
            return msps;
        }

        public void Dispose()
        {
            try { _ctx.ClearState(); _ctx.Flush(); } catch (SharpGen.Runtime.SharpGenException) { }
            foreach (var s in _shaders.Values) s.Dispose();
            _unitView?.Dispose(); _sparkView?.Dispose(); _unitBuffer?.Dispose(); _sparkBuffer?.Dispose();
            _event?.Dispose();
            if (_eventData != IntPtr.Zero) Marshal.FreeHGlobal(_eventData);
            _depthRead.Dispose(); _additive.Dispose(); _scissor.Dispose();
            _vsFull.Dispose();
            _psOverlay.Dispose();
            _depthOn.Dispose();
            _depthOff.Dispose();
            _sum.Dispose();
            _alpha.Dispose();
            _opaque.Dispose();
            _noCull.Dispose();
            _constants.Dispose();
            _rtv.Dispose();
            _dsv.Dispose();
            _depth.Dispose();
            _swap.Dispose();
            _ctx.Dispose();
            _device.Dispose();
            Win32.Destroy(_hwnd);
        }
    }

    // ------------------------------------------------------------------ janela Win32 simples (sem WPF)

    internal static class Win32
    {
        private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize, style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WNDCLASSEX c);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(int ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DispatchMessage(ref MSG msg);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowText(IntPtr hWnd, string text);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool AdjustWindowRect(ref RECT rect, uint style, bool menu);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr inst, IntPtr name);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private const uint Style = 0x00C00000 | 0x00080000 | 0x00020000; // título, menu do sistema, minimizar (tamanho fixo)
        private const string ClassName = "MBExtremeBenchWindow";
        private static readonly WndProc Proc = WindowProc;
        private static bool _registered;
        private static readonly HashSet<IntPtr> Closed = new();

        private static IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == 0x0010) // WM_CLOSE: não fecha na hora; o teste para e fecha a janela
            {
                lock (Closed) Closed.Add(hWnd);
                return IntPtr.Zero;
            }
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        public static IntPtr CreateBenchWindow(int width, int height, string title)
        {
            IntPtr inst = GetModuleHandle(null);
            if (!_registered)
            {
                var wc = new WNDCLASSEX
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Proc),
                    hInstance = inst,
                    hCursor = LoadCursor(IntPtr.Zero, (IntPtr)32512),
                    lpszClassName = ClassName,
                };
                RegisterClassEx(ref wc);
                _registered = true;
            }
            // cabe em 75% da tela, mantendo a proporção da resolução do teste
            int screenW = GetSystemMetrics(0), screenH = GetSystemMetrics(1);
            double scale = Math.Min(1.0, Math.Min(screenW * 0.75 / width, screenH * 0.75 / height));
            var rect = new RECT { Right = (int)(width * scale), Bottom = (int)(height * scale) };
            AdjustWindowRect(ref rect, Style, false);
            int w = rect.Right - rect.Left, h = rect.Bottom - rect.Top;
            IntPtr hwnd = CreateWindowEx(0, ClassName, title, Style, Math.Max(0, (screenW - w) / 2), Math.Max(0, (screenH - h) / 2), w, h, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
            if (hwnd == IntPtr.Zero) throw new InvalidOperationException("Não foi possível abrir a janela do teste 3D.");
            try { int dark = 1; DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)); } catch (DllNotFoundException) { }
            ShowWindow(hwnd, 5);
            SetForegroundWindow(hwnd);
            return hwnd;
        }

        /// <summary>Atende as mensagens da janela. Devolve true quando a pessoa pediu para fechar.</summary>
        public static bool Pump(IntPtr hwnd)
        {
            while (PeekMessage(out MSG msg, IntPtr.Zero, 0, 0, 1))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            lock (Closed) return Closed.Contains(hwnd);
        }

        public static bool IsForeground(IntPtr hwnd) => GetForegroundWindow() == hwnd;

        public static void SetTitle(IntPtr hwnd, string title) => SetWindowText(hwnd, title);

        public static void Destroy(IntPtr hwnd)
        {
            lock (Closed) Closed.Remove(hwnd);
            DestroyWindow(hwnd);
        }
    }
}
