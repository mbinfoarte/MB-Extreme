using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MBExtreme.Core.Gamepads;

namespace MBExtreme.UI.Controls;

/// <summary>Desenho próprio do controle (formato genérico) que acende em amarelo o que está sendo apertado, ao vivo.</summary>
internal sealed class PadView : FrameworkElement
{
    private static readonly Brush Body = Frozen(Color.FromRgb(0x1B, 0x1B, 0x1F));
    private static readonly Brush Part = Frozen(Color.FromRgb(0x26, 0x26, 0x2B));
    private static readonly Brush Well = Frozen(Color.FromRgb(0x0E, 0x0E, 0x10));
    private static readonly Brush On = Frozen(Color.FromRgb(0xFF, 0xC4, 0x00));
    private static readonly Brush Text = Frozen(Color.FromRgb(0x8E, 0x8E, 0x96));
    private static readonly Pen Edge = FrozenPen(Color.FromRgb(0x3A, 0x3A, 0x42), 1.5);
    private PadState _s = PadState.Empty;

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    private static Pen FrozenPen(Color c, double w) { var p = new Pen(Frozen(c), w); p.Freeze(); return p; }

    public bool Xbox { get; set; }

    public PadView() { Width = 420; Height = 280; }

    public void Show(PadState s)
    {
        _s = s;
        InvalidateVisual();
    }

    private bool B(PadButtons b) => (_s.Buttons & b) != 0;

    protected override void OnRender(DrawingContext dc)
    {
        double k = Math.Min(ActualWidth / 420, ActualHeight / 280);
        dc.PushTransform(new ScaleTransform(k, k));

        var body = Geometry.Parse("M110 40 C150 30 270 30 310 40 C350 48 372 70 384 110 C398 160 408 210 392 240 C380 262 352 262 336 244 C320 226 304 206 282 200 L138 200 C116 206 100 226 84 244 C68 262 40 262 28 240 C12 210 22 160 36 110 C48 70 70 48 110 40 Z");
        // gatilhos: preenchem conforme a pressão
        Trigger(dc, 96, 18, _s.L2, "L2");
        Trigger(dc, 268, 18, _s.R2, "R2");
        dc.DrawRoundedRectangle(B(PadButtons.L1) ? On : Part, Edge, new Rect(100, 34, 48, 8), 3, 3);
        dc.DrawRoundedRectangle(B(PadButtons.R1) ? On : Part, Edge, new Rect(272, 34, 48, 8), 3, 3);
        dc.DrawGeometry(Body, Edge, body);

        dc.DrawRoundedRectangle(B(PadButtons.Touchpad) ? On : Well, Edge, new Rect(160, 56, 100, 58), 12, 12);
        Label(dc, Xbox ? "" : "touchpad", 210, 85, 10);
        dc.DrawRoundedRectangle(B(PadButtons.Create) ? On : Part, Edge, new Rect(140, 62, 12, 18), 4, 4);
        dc.DrawRoundedRectangle(B(PadButtons.Options) ? On : Part, Edge, new Rect(268, 62, 12, 18), 4, 4);

        // direcional
        dc.DrawRoundedRectangle(B(PadButtons.Up) ? On : Part, Edge, new Rect(82, 80, 18, 20), 3, 3);
        dc.DrawRoundedRectangle(B(PadButtons.Left) ? On : Part, Edge, new Rect(62, 100, 20, 18), 3, 3);
        dc.DrawRoundedRectangle(B(PadButtons.Right) ? On : Part, Edge, new Rect(100, 100, 20, 18), 3, 3);
        dc.DrawRoundedRectangle(B(PadButtons.Down) ? On : Part, Edge, new Rect(82, 118, 18, 20), 3, 3);

        // botões de ação
        Face(dc, 330, 86, PadButtons.Triangle, Xbox ? "Y" : "△");
        Face(dc, 308, 108, PadButtons.Square, Xbox ? "X" : "□");
        Face(dc, 352, 108, PadButtons.Circle, Xbox ? "B" : "○");
        Face(dc, 330, 130, PadButtons.Cross, Xbox ? "A" : "✕");

        Stick(dc, 150, 160, _s.LX, _s.LY, B(PadButtons.L3));
        Stick(dc, 270, 160, _s.RX, _s.RY, B(PadButtons.R3));
        dc.DrawEllipse(B(PadButtons.PS) ? On : Part, Edge, new Point(210, 152), 8, 8);
        if (!Xbox) dc.DrawRoundedRectangle(B(PadButtons.Mute) ? On : Part, Edge, new Rect(200, 168, 20, 6), 3, 3);
        dc.Pop();
    }

    private void Trigger(DrawingContext dc, double x, double y, double value, string name)
    {
        var r = new Rect(x, y, 56, 16);
        dc.DrawRoundedRectangle(Part, Edge, r, 7, 7);
        if (value > 0.01) dc.DrawRoundedRectangle(On, null, new Rect(x, y, 56 * Math.Clamp(value, 0, 1), 16), 7, 7);
        Label(dc, name, x + 28, y + 8, 9, value > 0.5 ? Body : Text);
    }

    private void Face(DrawingContext dc, double x, double y, PadButtons b, string glyph)
    {
        bool on = B(b);
        dc.DrawEllipse(on ? On : Part, Edge, new Point(x, y), 11, 11);
        Label(dc, glyph, x, y, 11, on ? Body : Text);
    }

    private static void Stick(DrawingContext dc, double cx, double cy, double x, double y, bool pressed)
    {
        dc.DrawEllipse(Well, Edge, new Point(cx, cy), 24, 24);
        dc.DrawEllipse(pressed ? On : Frozen(Color.FromRgb(0x4A, 0x4A, 0x52)), null, new Point(cx + Math.Clamp(x, -1, 1) * 12, cy + Math.Clamp(y, -1, 1) * 12), 13, 13);
        if (Math.Abs(x) > 0.05 || Math.Abs(y) > 0.05)
            dc.DrawEllipse(On, null, new Point(cx + Math.Clamp(x, -1, 1) * 12, cy + Math.Clamp(y, -1, 1) * 12), 4, 4);
    }

    private static void Label(DrawingContext dc, string text, double x, double y, double size, Brush? brush = null)
    {
        if (text.Length == 0) return;
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush ?? Text, 1.0);
        dc.DrawText(ft, new Point(x - ft.Width / 2, y - ft.Height / 2));
    }
}
