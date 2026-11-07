using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace MBExtreme.Core.Benchmark;

/// <summary>FPS de uma resolução no teste de limite por resolução.</summary>
public sealed record ResolutionFps(int Width, int Height, double Fps)
{
    public string Label => $"{Width}×{Height}";
}

/// <summary>Resultado das cenas 3D da janela separada.</summary>
public sealed record GpuWindowResult(
    string GpuName,
    double ReflectionsFps,
    double GeometryFps,
    double StudioMsps,
    double GameFps,
    IReadOnlyList<ResolutionFps> Resolutions,
    int Score)
{
    /// <summary>A maior resolução que passou de 60 FPS na simulação de jogo (null quando nenhuma passou).</summary>
    public ResolutionFps? Max60 => Resolutions.Where(r => r.Fps >= 60).OrderByDescending(r => (long)r.Width * r.Height).FirstOrDefault();
}

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

    /// <summary>Uma cena por vez; <paramref name="progress"/> recebe a cena, a fração dela (0 a 1) e o FPS atual.</summary>
    public static GpuWindowResult Run(int width, int height, int secondsPerScene, int studioSamples, IReadOnlyList<(int Width, int Height)> resolutions,
        Action<string, double, double>? progress, CancellationToken token, bool resolutionLimit = true)
    {
        using var host = new Host(width, height, token);
        double refl = host.RunScene(Reflections, secondsPerScene, progress, host.DrawReflections);
        double geo = host.RunScene(Geometry, secondsPerScene, progress, host.DrawGeometry);
        double studio = host.RunStudio(secondsPerScene, studioSamples, progress);
        double game = host.RunScene(Game, secondsPerScene, progress, host.DrawGame);
        var list = new List<ResolutionFps>();
        if (resolutionLimit)
        {
            int per = Math.Max(2, secondsPerScene / 2);
            for (int i = 0; i < resolutions.Count; i++)
            {
                var (w, h) = resolutions[i];
                host.Resize(w, h);
                int index = i;
                double fps = host.RunScene($"{ResolutionLimit} · {w}×{h}", per,
                    (_, f, current) => progress?.Invoke(ResolutionLimit, (index + f) / resolutions.Count, current), host.DrawGame, ResolutionLimit);
                list.Add(new ResolutionFps(w, h, fps));
            }
        }
        progress?.Invoke(ResolutionLimit, 1, -1);
        return new GpuWindowResult(host.GpuName, refl, geo, studio, game, list, Score(refl, geo, studio, game, (long)width * height));
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
        private readonly ID3D11DepthStencilState _depthOn, _depthOff;
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

        public void DrawGame()
        {
            Begin(true, new Color4(0, 0, 0, 1));
            FullScreen(Shader<ID3D11PixelShader>("Game", "PSSky", "ps_5_0"));
            _ctx.OMSetDepthStencilState(_depthOn);
            var ps = Shader<ID3D11PixelShader>("Game", "PSGame", "ps_5_0");
            _ctx.VSSetShader(Shader<ID3D11VertexShader>("Game", "VSTerrain", "vs_5_0"));
            _ctx.PSSetShader(ps);
            _ctx.Draw((TerrainSize - 1) * (TerrainSize - 1) * 6, 0);
            _ctx.VSSetShader(Shader<ID3D11VertexShader>("Game", "VSUnits", "vs_5_0"));
            _ctx.DrawInstanced(36, (uint)(_frame.Units * 2), 0, 0);
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
            _frame.Units = 5000;
            _ctx.UpdateSubresource(in _frame, _constants);
        }

        private void Present()
        {
            var result = _swap.Present(0, PresentFlags.None);
            if (result.Failure) throw new InvalidOperationException("A placa de vídeo parou de responder durante o teste (" + result.Code.ToString("X8") + ").");
        }

        /// <summary>Roda uma cena por <paramref name="seconds"/>; o primeiro meio segundo é aquecimento e não conta. Devolve o FPS médio.</summary>
        public double RunScene(string title, int seconds, Action<string, double, double>? progress, Action draw, string? step = null)
        {
            Win32.SetTitle(_hwnd, $"MB Bench · {title} (não feche esta janela)");
            step ??= title;
            const double warmup = 0.5;
            var watch = Stopwatch.StartNew();
            long frames = 0, shownFrames = 0;
            double shownAt = 0, shownFps = -1, lastReport = -1;
            while (true)
            {
                Pump();
                double t = watch.Elapsed.TotalSeconds;
                if (t >= seconds + warmup) break;
                double fraction = Math.Max(0, t - warmup) / seconds;
                Upload(t, fraction, shownFps, frames);
                draw();
                Overlay();
                Present();
                if (t >= warmup) frames++;
                shownFrames++;
                if (t - shownAt >= 0.5)
                {
                    shownFps = shownFrames / (t - shownAt);
                    shownFrames = 0;
                    shownAt = t;
                }
                if (t - lastReport >= 0.25) { lastReport = t; progress?.Invoke(step, fraction, shownFps); }
            }
            _ctx.Flush();
            return frames / Math.Max(watch.Elapsed.TotalSeconds - warmup, 0.001);
        }

        /// <summary>Estúdio: soma amostras numa textura e mostra a média; devolve milhões de amostras por segundo.</summary>
        public double RunStudio(int seconds, int samples, Action<string, double, double>? progress)
        {
            Win32.SetTitle(_hwnd, $"MB Bench · {Studio} (não feche esta janela)");
            using var accum = _device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)_width, Height = (uint)_height, MipLevels = 1, ArraySize = 1, Format = Format.R32G32B32A32_Float,
                SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            });
            using var accumRtv = _device.CreateRenderTargetView(accum);
            using var accumSrv = _device.CreateShaderResourceView(accum);
            var psStudio = Shader<ID3D11PixelShader>("Studio", "PSStudio", "ps_5_0");
            var psResolve = Shader<ID3D11PixelShader>("Studio", "PSResolve", "ps_5_0");
            _ctx.ClearRenderTargetView(accumRtv, new Color4(0, 0, 0, 0));
            int spp = Math.Max(1, samples);
            long total = 0;
            var watch = Stopwatch.StartNew();
            double shownAt = 0, shownFps = -1, lastReport = -1;
            long shownFrames = 0;
            while (watch.Elapsed.TotalSeconds < seconds)
            {
                Pump();
                double frameStart = watch.Elapsed.TotalSeconds;
                _ctx.RSSetViewport(0, 0, _width, _height);
                _ctx.RSSetState(_noCull);
                _ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                _ctx.VSSetConstantBuffer(0, _constants);
                _ctx.PSSetConstantBuffer(0, _constants);
                _ctx.OMSetRenderTargets(accumRtv, null);
                _ctx.OMSetBlendState(_sum);
                for (int s = 0; s < spp; s++)
                {
                    Upload(frameStart, frameStart / seconds, shownFps, total + s, spp);
                    FullScreen(psStudio);
                    _ctx.Flush();
                }
                total += spp;
                // mostra a média na janela
                Upload(frameStart, frameStart / seconds, shownFps, total, spp);
                _ctx.OMSetRenderTargets(_rtv, null);
                _ctx.OMSetBlendState(_opaque);
                _ctx.PSSetShaderResource(0, accumSrv);
                FullScreen(psResolve);
                Overlay();
                Present();
                shownFrames++;
                double t = watch.Elapsed.TotalSeconds;
                if (t - frameStart > 0.8 && spp > 1) spp = Math.Max(1, spp / 2);
                if (t - shownAt >= 0.5) { shownFps = shownFrames / (t - shownAt); shownFrames = 0; shownAt = t; }
                if (t - lastReport >= 0.25) { lastReport = t; progress?.Invoke(Studio, t / seconds, shownFps); }
            }
            return total * (double)_width * _height / Math.Max(watch.Elapsed.TotalSeconds, 0.001) / 1e6;
        }

        public void Dispose()
        {
            try { _ctx.ClearState(); _ctx.Flush(); } catch (SharpGen.Runtime.SharpGenException) { }
            foreach (var s in _shaders.Values) s.Dispose();
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

    private static class Win32
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

        public static void SetTitle(IntPtr hwnd, string title) => SetWindowText(hwnd, title);

        public static void Destroy(IntPtr hwnd)
        {
            lock (Closed) Closed.Remove(hwnd);
            DestroyWindow(hwnd);
        }
    }
}
