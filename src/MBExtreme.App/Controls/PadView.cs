using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MBExtreme.Core.Gamepads;

namespace MBExtreme.UI.Controls;

/// <summary>Formato do desenho: cada controle aparece com o próprio jeito.</summary>
internal enum PadLook { DualSense, DualShock4, Xbox }

/// <summary>
/// Desenho próprio do controle (PS5, PS4 ou Xbox) que acende o que está sendo apertado, ao vivo:
/// botões, gatilhos que enchem conforme a pressão e analógicos que se movem.
/// </summary>
internal sealed class PadView : FrameworkElement
{
    private static readonly Brush Hot = Frozen(0xFF, 0xC4, 0x00);
    private static readonly Brush Ink = Frozen(0x0B, 0x0B, 0x0B);
    private static readonly Brush Muted = Frozen(0x8E, 0x8E, 0x96);
    private PadState _s = PadState.Empty;

    private static SolidColorBrush Frozen(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }
    private static Pen PenOf(Brush b, double w) { var p = new Pen(b, w); p.Freeze(); return p; }

    public PadLook Look { get; set; } = PadLook.DualSense;

    /// <summary>Cor da barra de luz (PS4/PS5).</summary>
    public Color LightColor { get; set; } = Color.FromRgb(0x2D, 0x6C, 0xFF);

    /// <summary>Compatibilidade com o código antigo.</summary>
    public bool Xbox { get => Look == PadLook.Xbox; set { if (value) Look = PadLook.Xbox; else if (Look == PadLook.Xbox) Look = PadLook.DualSense; } }

    public PadView() { Width = 440; Height = 290; }

    public void Show(PadState s)
    {
        _s = s;
        InvalidateVisual();
    }

    private bool B(PadButtons b) => (_s.Buttons & b) != 0;

    protected override void OnRender(DrawingContext dc)
    {
        double k = Math.Min(ActualWidth / 440, ActualHeight / 290);
        dc.PushTransform(new TranslateTransform((ActualWidth - 440 * k) / 2, 0));
        dc.PushTransform(new ScaleTransform(k, k));
        switch (Look)
        {
            case PadLook.Xbox: DrawXbox(dc); break;
            case PadLook.DualShock4: DrawDs4(dc); break;
            default: DrawDualSense(dc); break;
        }
        dc.Pop();
        dc.Pop();
    }

    // ------------------------------------------------------------------ PS5 (DualSense): corpo branco, centro preto, touchpad grande

    private void DrawDualSense(DrawingContext dc)
    {
        var shell = Frozen(0xEC, 0xEC, 0xF0);
        var center = Frozen(0x14, 0x14, 0x18);
        var edge = PenOf(Frozen(0xC8, 0xC8, 0xD0), 1.5);
        Trigger(dc, 92, 14, _s.L2, "L2", Frozen(0x22, 0x22, 0x28));
        Trigger(dc, 292, 14, _s.R2, "R2", Frozen(0x22, 0x22, 0x28));
        Bumper(dc, 96, 36, B(PadButtons.L1), Frozen(0x22, 0x22, 0x28));
        Bumper(dc, 296, 36, B(PadButtons.R1), Frozen(0x22, 0x22, 0x28));
        // corpo branco (asas) + centro preto
        dc.DrawGeometry(shell, edge, Geometry.Parse("M120 46 C160 38 280 38 320 46 C366 54 392 82 404 124 C420 182 428 232 410 258 C396 278 366 276 350 256 C334 236 318 214 296 208 L144 208 C122 214 106 236 90 256 C74 276 44 278 30 258 C12 232 20 182 36 124 C48 82 74 54 120 46 Z"));
        dc.DrawGeometry(center, null, Geometry.Parse("M150 60 L290 60 C300 60 306 66 306 76 L306 196 C306 204 300 208 292 208 L148 208 C140 208 134 204 134 196 L134 76 C134 66 140 60 150 60 Z"));
        // touchpad com a barra de luz nas bordas
        var light = new SolidColorBrush(LightColor);
        dc.DrawRoundedRectangle(light, null, new Rect(156, 58, 128, 66), 14, 14);
        dc.DrawRoundedRectangle(B(PadButtons.Touchpad) ? Hot : Frozen(0xF4, 0xF4, 0xF6), PenOf(Frozen(0xD8, 0xD8, 0xDE), 1), new Rect(160, 60, 120, 62), 12, 12);
        dc.DrawRoundedRectangle(B(PadButtons.Create) ? Hot : Frozen(0xD0, 0xD0, 0xD6), null, new Rect(140, 64, 8, 16), 4, 4);
        dc.DrawRoundedRectangle(B(PadButtons.Options) ? Hot : Frozen(0xD0, 0xD0, 0xD6), null, new Rect(292, 64, 8, 16), 4, 4);
        // direcional e botões
        Dpad(dc, 92, 112, Frozen(0xD4, 0xD4, 0xDA), Ink);
        Face(dc, 346, 92, PadButtons.Triangle, "△", Frozen(0xD8, 0xD8, 0xDE), Frozen(0x5A, 0x5A, 0x62));
        Face(dc, 322, 116, PadButtons.Square, "□", Frozen(0xD8, 0xD8, 0xDE), Frozen(0x5A, 0x5A, 0x62));
        Face(dc, 370, 116, PadButtons.Circle, "○", Frozen(0xD8, 0xD8, 0xDE), Frozen(0x5A, 0x5A, 0x62));
        Face(dc, 346, 140, PadButtons.Cross, "✕", Frozen(0xD8, 0xD8, 0xDE), Frozen(0x5A, 0x5A, 0x62));
        Stick(dc, 168, 166, _s.LX, _s.LY, B(PadButtons.L3), Frozen(0x08, 0x08, 0x0A), Frozen(0x2E, 0x2E, 0x36));
        Stick(dc, 272, 166, _s.RX, _s.RY, B(PadButtons.R3), Frozen(0x08, 0x08, 0x0A), Frozen(0x2E, 0x2E, 0x36));
        dc.DrawEllipse(B(PadButtons.PS) ? Hot : Frozen(0x3A, 0x3A, 0x42), null, new Point(220, 168), 8, 8);
        dc.DrawRoundedRectangle(B(PadButtons.Mute) ? Hot : Frozen(0x3A, 0x3A, 0x42), null, new Rect(210, 184, 20, 6), 3, 3);
    }

    // ------------------------------------------------------------------ PS4 (DualShock 4): corpo escuro, barra de luz no alto

    private void DrawDs4(DrawingContext dc)
    {
        var body = Frozen(0x1E, 0x1E, 0x24);
        var edge = PenOf(Frozen(0x3A, 0x3A, 0x44), 1.5);
        Trigger(dc, 96, 14, _s.L2, "L2", Frozen(0x2A, 0x2A, 0x32));
        Trigger(dc, 288, 14, _s.R2, "R2", Frozen(0x2A, 0x2A, 0x32));
        Bumper(dc, 100, 36, B(PadButtons.L1), Frozen(0x2A, 0x2A, 0x32));
        Bumper(dc, 292, 36, B(PadButtons.R1), Frozen(0x2A, 0x2A, 0x32));
        dc.DrawRoundedRectangle(new SolidColorBrush(LightColor), null, new Rect(170, 40, 100, 6), 3, 3);
        dc.DrawGeometry(body, edge, Geometry.Parse("M118 48 C160 42 280 42 322 48 C364 56 388 84 400 126 C414 180 420 228 404 254 C390 274 362 272 346 252 C330 232 316 214 294 208 L146 208 C124 214 110 232 94 252 C78 272 50 274 36 254 C20 228 26 180 40 126 C52 84 76 56 118 48 Z"));
        dc.DrawRoundedRectangle(B(PadButtons.Touchpad) ? Hot : Frozen(0x2A, 0x2A, 0x32), edge, new Rect(166, 56, 108, 58), 10, 10);
        dc.DrawRoundedRectangle(B(PadButtons.Create) ? Hot : Frozen(0x3A, 0x3A, 0x44), null, new Rect(146, 60, 8, 14), 4, 4);
        dc.DrawRoundedRectangle(B(PadButtons.Options) ? Hot : Frozen(0x3A, 0x3A, 0x44), null, new Rect(286, 60, 8, 14), 4, 4);
        Dpad(dc, 92, 112, Frozen(0x33, 0x33, 0x3C), Ink);
        Face(dc, 346, 92, PadButtons.Triangle, "△", Frozen(0x2E, 0x2E, 0x36), Frozen(0x3D, 0xDC, 0x97));
        Face(dc, 322, 116, PadButtons.Square, "□", Frozen(0x2E, 0x2E, 0x36), Frozen(0xE0, 0x7B, 0xD9));
        Face(dc, 370, 116, PadButtons.Circle, "○", Frozen(0x2E, 0x2E, 0x36), Frozen(0xFF, 0x5A, 0x5F));
        Face(dc, 346, 140, PadButtons.Cross, "✕", Frozen(0x2E, 0x2E, 0x36), Frozen(0x6C, 0xB6, 0xFF));
        Stick(dc, 166, 170, _s.LX, _s.LY, B(PadButtons.L3), Frozen(0x0E, 0x0E, 0x12), Frozen(0x40, 0x40, 0x4A));
        Stick(dc, 274, 170, _s.RX, _s.RY, B(PadButtons.R3), Frozen(0x0E, 0x0E, 0x12), Frozen(0x40, 0x40, 0x4A));
        dc.DrawEllipse(B(PadButtons.PS) ? Hot : Frozen(0x3A, 0x3A, 0x44), null, new Point(220, 168), 8, 8);
    }

    // ------------------------------------------------------------------ Xbox: analógico esquerdo no alto, ABXY coloridos

    private void DrawXbox(DrawingContext dc)
    {
        var body = Frozen(0x23, 0x23, 0x28);
        var edge = PenOf(Frozen(0x3E, 0x3E, 0x46), 1.5);
        Trigger(dc, 96, 14, _s.L2, "LT", Frozen(0x2E, 0x2E, 0x34));
        Trigger(dc, 288, 14, _s.R2, "RT", Frozen(0x2E, 0x2E, 0x34));
        Bumper(dc, 100, 36, B(PadButtons.L1), Frozen(0x2E, 0x2E, 0x34));
        Bumper(dc, 292, 36, B(PadButtons.R1), Frozen(0x2E, 0x2E, 0x34));
        dc.DrawGeometry(body, edge, Geometry.Parse("M112 50 C150 40 290 40 328 50 C370 60 394 92 404 134 C416 186 418 232 400 256 C384 276 354 270 338 250 C318 226 300 212 270 210 L170 210 C140 212 122 226 102 250 C86 270 56 276 40 256 C22 232 24 186 36 134 C46 92 70 60 112 50 Z"));
        dc.DrawEllipse(B(PadButtons.PS) ? Hot : Frozen(0x3E, 0x3E, 0x46), PenOf(Frozen(0x55, 0x55, 0x60), 1), new Point(220, 72), 14, 14);
        dc.DrawEllipse(B(PadButtons.Create) ? Hot : Frozen(0x3E, 0x3E, 0x46), null, new Point(194, 104), 6, 6);
        dc.DrawEllipse(B(PadButtons.Options) ? Hot : Frozen(0x3E, 0x3E, 0x46), null, new Point(246, 104), 6, 6);
        Stick(dc, 110, 112, _s.LX, _s.LY, B(PadButtons.L3), Frozen(0x10, 0x10, 0x14), Frozen(0x44, 0x44, 0x4E));
        Dpad(dc, 160, 160, Frozen(0x3A, 0x3A, 0x44), Ink);
        Stick(dc, 280, 170, _s.RX, _s.RY, B(PadButtons.R3), Frozen(0x10, 0x10, 0x14), Frozen(0x44, 0x44, 0x4E));
        Face(dc, 338, 88, PadButtons.Triangle, "Y", Frozen(0x2E, 0x2E, 0x34), Frozen(0xFF, 0xC4, 0x00), lit: Frozen(0xFF, 0xC4, 0x00));
        Face(dc, 314, 112, PadButtons.Square, "X", Frozen(0x2E, 0x2E, 0x34), Frozen(0x4D, 0x9F, 0xFF), lit: Frozen(0x4D, 0x9F, 0xFF));
        Face(dc, 362, 112, PadButtons.Circle, "B", Frozen(0x2E, 0x2E, 0x34), Frozen(0xFF, 0x5A, 0x5F), lit: Frozen(0xFF, 0x5A, 0x5F));
        Face(dc, 338, 136, PadButtons.Cross, "A", Frozen(0x2E, 0x2E, 0x34), Frozen(0x3D, 0xDC, 0x97), lit: Frozen(0x3D, 0xDC, 0x97));
    }

    // ------------------------------------------------------------------ peças

    private static void Trigger(DrawingContext dc, double x, double y, double value, string name, Brush baseBrush)
    {
        dc.DrawRoundedRectangle(baseBrush, null, new Rect(x, y, 56, 18), 8, 8);
        if (value > 0.01) dc.DrawRoundedRectangle(Hot, null, new Rect(x, y, Math.Max(10, 56 * Math.Clamp(value, 0, 1)), 18), 8, 8);
        Label(dc, value > 0.01 ? $"{name} {value * 100:0}%" : name, x + 28, y + 9, 9.5, value > 0.5 ? Ink : Muted);
    }

    private static void Bumper(DrawingContext dc, double x, double y, bool on, Brush baseBrush) =>
        dc.DrawRoundedRectangle(on ? Hot : baseBrush, null, new Rect(x, y, 48, 8), 4, 4);

    private void Dpad(DrawingContext dc, double cx, double cy, Brush part, Brush glyph)
    {
        dc.DrawRoundedRectangle(B(PadButtons.Up) ? Hot : part, null, new Rect(cx - 9, cy - 30, 18, 22), 4, 4);
        dc.DrawRoundedRectangle(B(PadButtons.Down) ? Hot : part, null, new Rect(cx - 9, cy + 8, 18, 22), 4, 4);
        dc.DrawRoundedRectangle(B(PadButtons.Left) ? Hot : part, null, new Rect(cx - 30, cy - 9, 22, 18), 4, 4);
        dc.DrawRoundedRectangle(B(PadButtons.Right) ? Hot : part, null, new Rect(cx + 8, cy - 9, 22, 18), 4, 4);
    }

    private void Face(DrawingContext dc, double x, double y, PadButtons b, string glyph, Brush part, Brush mark, Brush? lit = null)
    {
        bool on = B(b);
        dc.DrawEllipse(on ? (lit ?? Hot) : part, null, new Point(x, y), 12, 12);
        Label(dc, glyph, x, y, 12, on ? Ink : mark);
    }

    private static void Stick(DrawingContext dc, double cx, double cy, double x, double y, bool pressed, Brush well, Brush cap)
    {
        dc.DrawEllipse(well, null, new Point(cx, cy), 25, 25);
        double px = cx + Math.Clamp(x, -1, 1) * 12, py = cy + Math.Clamp(y, -1, 1) * 12;
        dc.DrawEllipse(pressed ? Hot : cap, null, new Point(px, py), 15, 15);
        if (Math.Abs(x) > 0.05 || Math.Abs(y) > 0.05) dc.DrawEllipse(Hot, null, new Point(px, py), 4.5, 4.5);
    }

    private static void Label(DrawingContext dc, string text, double x, double y, double size, Brush brush)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), size, brush, 1.0);
        dc.DrawText(ft, new Point(x - ft.Width / 2, y - ft.Height / 2));
    }
}
