using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MBExtreme.Core.Network;

namespace MBExtreme.UI.Controls;

public enum LinkMode { Idle, Ping, Download, Upload }

/// <summary>Área do mapa: Auto (cabe tudo, mínimo a América do Sul), só o Brasil ou o mundo.</summary>
public enum MapScope { Auto, Brazil, World }

/// <summary>Uma ligação no mapa: até um servidor, com a cor e o sentido das bolinhas.</summary>
public sealed class MapLink
{
    public required GeoPoint To { get; init; }
    public LinkMode Mode { get; set; } = LinkMode.Idle;
    public Color Color { get; set; } = Color.FromRgb(0xFF, 0xC4, 0x00);
    public string? Label { get; set; }
    /// <summary>Saltos do traceroute (posição de 0 a 1 na linha e o texto).</summary>
    public List<(double T, string Text)> Hops { get; } = new();
}

/// <summary>
/// Mapa do teste de internet: países (o Brasil em destaque), a sua cidade pulsando e linhas curvas até cada servidor, com bolinhas
/// andando: do servidor para você no download (amarelo), de você para o servidor no upload (roxo) e nos dois sentidos no ping.
/// Os saltos do traceroute aparecem ao longo da linha, pela fração do tempo de resposta (não é a posição geográfica do roteador).
/// </summary>
internal sealed class NetMapControl : FrameworkElement
{
    public static readonly Color Download = Color.FromRgb(0xFF, 0xC4, 0x00);
    public static readonly Color Upload = Color.FromRgb(0xB5, 0x7C, 0xFF);
    public static readonly Color Ping = Color.FromRgb(0xE8, 0xE8, 0xEC);

    private static readonly List<(double Lon, double Lat)[]> Countries = NetMap.Rings(WorldMapData.Countries).ToList();
    private static readonly List<(double Lon, double Lat)[]> BrazilRings = NetMap.Rings(WorldMapData.Brazil).ToList();

    private GeoPoint? _user;
    private readonly List<MapLink> _links = new();
    private MapBounds _bounds;
    private Size _lastSize;
    private Geometry? _land, _brazil;
    private DateTime _start = DateTime.UtcNow;
    private bool _animating;

    public NetMapControl()
    {
        ClipToBounds = true;
        Loaded += (_, _) => StartAnimation();
        Unloaded += (_, _) => StopAnimation();
        IsVisibleChanged += (_, _) => { if (IsVisible) StartAnimation(); else StopAnimation(); };
    }

    public GeoPoint? User { get => _user; set { _user = value; Refit(); } }

    /// <summary>Segunda linha do rótulo "Você" (ex.: o provedor).</summary>
    public string? UserDetail { get => _userDetail; set { _userDetail = value; InvalidateVisual(); } }
    private string? _userDetail;

    /// <summary>Área mostrada.</summary>
    public MapScope Scope { get => _scope; set { _scope = value; Refit(); } }
    private MapScope _scope = MapScope.Auto;

    /// <summary>Ponta de seta no servidor (mapa de ping).</summary>
    public bool Arrows { get; set; }
    public IReadOnlyList<MapLink> Links => _links;

    public MapLink SetLink(GeoPoint to, LinkMode mode, Color color, string? label = null)
    {
        var link = _links.FirstOrDefault(l => l.To.Name == to.Name) ?? new MapLink { To = to };
        if (!_links.Contains(link)) _links.Add(link);
        link.Mode = mode;
        link.Color = color;
        link.Label = label;
        Refit();
        return link;
    }

    public void ClearLinks() { _links.Clear(); Refit(); }

    private void Refit()
    {
        _lastSize = default;   // recalcula na próxima pintura
        InvalidateVisual();
    }

    private void StartAnimation()
    {
        if (_animating || !Motion.Enabled) return;
        _animating = true;
        CompositionTarget.Rendering += OnFrame;
    }

    private void StopAnimation()
    {
        if (!_animating) return;
        _animating = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private DateTime _lastFrame;

    private void OnFrame(object? sender, EventArgs e)
    {
        // 30 quadros por segundo bastam para as bolinhas e gastam pouco
        var now = DateTime.UtcNow;
        if ((now - _lastFrame).TotalMilliseconds < 33) return;
        _lastFrame = now;
        InvalidateVisual();
    }

    private Point P(double lat, double lon) { var (x, y) = NetMap.Project(lat, lon, _bounds, ActualWidth, ActualHeight); return new Point(x, y); }

    private Geometry Build(IEnumerable<(double Lon, double Lat)[]> rings)
    {
        var g = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var ctx = g.Open())
            foreach (var ring in rings)
            {
                // pula anéis inteiros fora da área (economiza desenho)
                if (ring.All(p => p.Lon < _bounds.MinLon - 5) || ring.All(p => p.Lon > _bounds.MaxLon + 5) || ring.All(p => p.Lat < _bounds.MinLat - 5) || ring.All(p => p.Lat > _bounds.MaxLat + 5)) continue;
                ctx.BeginFigure(P(ring[0].Lat, ring[0].Lon), true, true);
                for (int i = 1; i < ring.Length; i++)
                {
                    // país que cruza a linha de 180° (Fiji, Rússia): começa outro pedaço em vez de riscar o mapa de ponta a ponta
                    if (Math.Abs(ring[i].Lon - ring[i - 1].Lon) > 180) ctx.BeginFigure(P(ring[i].Lat, ring[i].Lon), true, true);
                    else ctx.LineTo(P(ring[i].Lat, ring[i].Lon), true, false);
                }
            }
        g.Freeze();
        return g;
    }

    private static Point Bezier(Point a, Point c, Point b, double t)
    {
        double u = 1 - t;
        return new Point(u * u * a.X + 2 * u * t * c.X + t * t * b.X, u * u * a.Y + 2 * u * t * c.Y + t * t * b.Y);
    }

    private static Point Control(Point a, Point b)
    {
        var mid = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return mid;
        // levanta a curva para o lado de cima (como as rotas de avião nos mapas)
        double nx = -dy / len, ny = dx / len;
        if (ny > 0) { nx = -nx; ny = -ny; }
        return new Point(mid.X + nx * len * 0.25, mid.Y + ny * len * 0.25);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth < 40 || ActualHeight < 40) return;
        var size = new Size(ActualWidth, ActualHeight);
        if (size != _lastSize)
        {
            var points = new List<(double, double)>();
            if (_user is not null) points.Add((_user.Lat, _user.Lon));
            points.AddRange(_links.Where(l => !double.IsNaN(l.To.Lat)).Select(l => (l.To.Lat, l.To.Lon)));
            double aspect = ActualWidth / ActualHeight;
            _bounds = _scope switch
            {
                MapScope.Brazil => NetMap.BrazilArea(aspect),
                MapScope.World => NetMap.WorldArea(aspect),
                _ => NetMap.Fit(points, aspect),
            };
            _land = Build(Countries);
            _brazil = Build(BrazilRings);
            _lastSize = size;
        }

        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0E)), null, new Rect(size), 12, 12);
        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x22)), new Pen(new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x36)), 0.7), _land);
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xC4, 0x00)), new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0xFF, 0xC4, 0x00)), 1), _brazil);

        double time = (DateTime.UtcNow - _start).TotalSeconds;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface("Segoe UI");
        Point? user = _user is null ? null : P(_user.Lat, _user.Lon);
        var placed = new List<Rect>();
        if (user is { } uu) { placed.Add(new Rect(uu.X - 8, uu.Y - 8, 16, 16)); placed.Add(new Rect(uu.X + 8, uu.Y + 6, 150, 32)); }

        // nome sem encobrir outro: tenta a posição normal e depois um pouco acima ou abaixo
        Point Place(FormattedText text, Point anchor)
        {
            double x = anchor.X + 9 + text.Width > ActualWidth ? anchor.X - 9 - text.Width : anchor.X + 9;
            foreach (double dy in new[] { 0.0, -14, 14, -28, 28, -42, 42 })
            {
                var r = new Rect(x, anchor.Y - text.Height / 2 + dy, text.Width, text.Height);
                if (!placed.Any(p => p.IntersectsWith(r))) { placed.Add(r); return r.TopLeft; }
            }
            var fallback = new Rect(x, anchor.Y - text.Height / 2, text.Width, text.Height);
            placed.Add(fallback);
            return fallback.TopLeft;
        }

        foreach (var link in _links)
        {
            if (user is null || double.IsNaN(link.To.Lat)) continue;
            if (_scope == MapScope.Brazil && !NetMap.Contains(_bounds, link.To.Lat, link.To.Lon, 1)) continue;   // fora do Brasil: aparece no mapa-múndi
            var a = user.Value;
            var b = P(link.To.Lat, link.To.Lon);
            var c = Control(a, b);
            var curve = new StreamGeometry();
            using (var ctx = curve.Open()) { ctx.BeginFigure(a, false, false); ctx.QuadraticBezierTo(c, b, true, false); }
            curve.Freeze();
            byte alpha = link.Mode == LinkMode.Idle ? (byte)0x50 : (byte)0xB0;
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(alpha, link.Color.R, link.Color.G, link.Color.B)), 1.6) { DashStyle = link.Mode == LinkMode.Idle ? DashStyles.Dash : DashStyles.Solid }, curve);

            // bolinhas andando: download chega em você, upload sai de você, ping vai e volta
            if (link.Mode != LinkMode.Idle)
            {
                int count = 7;
                double speed = link.Mode == LinkMode.Ping ? 0.45 : 0.7;
                for (int i = 0; i < count; i++)
                {
                    double phase = (time * speed + (double)i / count) % 1.0;
                    double t = link.Mode switch
                    {
                        LinkMode.Download => 1 - phase,
                        LinkMode.Upload => phase,
                        _ => i % 2 == 0 ? phase : 1 - phase,
                    };
                    var dot = Bezier(a, c, b, t);
                    double fade = Math.Sin(phase * Math.PI);
                    dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(60 * fade), link.Color.R, link.Color.G, link.Color.B)), null, dot, 7, 7);
                    dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(255 * fade), link.Color.R, link.Color.G, link.Color.B)), null, dot, 2.6, 2.6);
                }
            }

            // saltos do traceroute ao longo da linha
            for (int i = 0; i < link.Hops.Count; i++)
            {
                var (t, text) = link.Hops[i];
                var hp = Bezier(a, c, b, t);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0E)), new Pen(new SolidColorBrush(link.Color), 1.2), hp, 4, 4);
                if (link.Hops.Count <= 14 || i % 2 == 0)
                {
                    var label = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 9.5, new SolidColorBrush(Color.FromRgb(0xB4, 0xB4, 0xBC)), dpi);
                    dc.DrawText(label, new Point(hp.X + 6, hp.Y - (i % 2 == 0 ? 14 : -2)));
                }
            }

            // ponta de seta chegando no servidor
            if (Arrows)
            {
                var from = Bezier(a, c, b, 0.94);
                double ax = b.X - from.X, ay = b.Y - from.Y, al = Math.Sqrt(ax * ax + ay * ay);
                if (al > 0.5)
                {
                    ax /= al; ay /= al;
                    var tip = new Point(b.X - ax * 6, b.Y - ay * 6);
                    var head = new StreamGeometry();
                    using (var ctx = head.Open())
                    {
                        ctx.BeginFigure(tip, true, true);
                        ctx.LineTo(new Point(tip.X - ax * 8 - ay * 4.5, tip.Y - ay * 8 + ax * 4.5), true, false);
                        ctx.LineTo(new Point(tip.X - ax * 8 + ay * 4.5, tip.Y - ay * 8 - ax * 4.5), true, false);
                    }
                    head.Freeze();
                    dc.DrawGeometry(new SolidColorBrush(link.Color), null, head);
                }
            }

            // servidor
            dc.DrawEllipse(new SolidColorBrush(link.Color), new Pen(Brushes.Black, 1.5), b, 5.5, 5.5);
            var name = new FormattedText(link.Label ?? link.To.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 11, Brushes.White, dpi);
            dc.DrawText(name, Place(name, b));
        }

        // você: ponto amarelo com um anel pulsando
        if (user is { } u)
        {
            double pulse = (time * 0.8) % 1.0;
            dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(200 * (1 - pulse)), 0xFF, 0xC4, 0x00)), 1.5), u, 6 + 16 * pulse, 6 + 16 * pulse);
            dc.DrawEllipse(new SolidColorBrush(Download), new Pen(Brushes.Black, 1.5), u, 6, 6);
            var me = new FormattedText("Você · " + _user!.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 11.5, Brushes.White, dpi);
            dc.DrawText(me, new Point(Math.Min(u.X + 10, ActualWidth - me.Width - 4), u.Y + 8));
            if (!string.IsNullOrEmpty(_userDetail))
            {
                var detail = new FormattedText(_userDetail, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 10.5, new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA3)), dpi);
                dc.DrawText(detail, new Point(Math.Min(u.X + 10, ActualWidth - detail.Width - 4), u.Y + 8 + me.Height));
            }
        }
        else
        {
            var wait = new FormattedText("Localizando você e o servidor...", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 12, new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x96)), dpi);
            dc.DrawText(wait, new Point((ActualWidth - wait.Width) / 2, ActualHeight - wait.Height - 10));
        }
    }
}
