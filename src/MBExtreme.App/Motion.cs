using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MBExtreme.Core.Ui;

namespace MBExtreme.UI;

/// <summary>
/// Animações da interface num lugar só: entrada de telas e cartões, cascata de listas, pulsar, girar, contar números, abrir e fechar seções.
/// Respeitam o "mostrar animações" do Windows e a opção do programa (Acessibilidade); desligadas, tudo aparece na hora.
/// </summary>
internal static class Motion
{
    public static bool Enabled => AppServices.Settings.Animations && SystemParameters.ClientAreaAnimation;

    public static readonly Duration Fast = new(TimeSpan.FromMilliseconds(160));
    public static readonly Duration Normal = new(TimeSpan.FromMilliseconds(260));
    public static readonly Duration Slow = new(TimeSpan.FromMilliseconds(520));
    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>Chamado ao abrir o programa: com animações desligadas, os efeitos de passar o mouse do tema ficam instantâneos.</summary>
    public static void ApplyToTheme(ResourceDictionary resources)
    {
        if (!Enabled) resources["MotionHover"] = new Duration(TimeSpan.Zero);
    }

    private static TranslateTransform Translate(UIElement element)
    {
        if (element.RenderTransform is TranslateTransform t) return t;
        if (element.RenderTransform is TransformGroup g && g.Children.OfType<TranslateTransform>().FirstOrDefault() is { } inGroup) return inGroup;
        var created = new TranslateTransform();
        element.RenderTransform = created;
        return created;
    }

    /// <summary>Aparece subindo um pouco e clareando (telas, cartões e itens novos).</summary>
    public static void Enter(UIElement element, TimeSpan delay = default, double fromY = 10, Duration? duration = null)
    {
        if (!Enabled) { element.Opacity = 1; return; }
        var d = duration ?? Normal;
        element.Opacity = 0;
        var fade = new DoubleAnimation(0, 1, d) { BeginTime = delay, EasingFunction = Ease };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        if (fromY != 0)
        {
            var move = new DoubleAnimation(fromY, 0, d) { BeginTime = delay, EasingFunction = Ease };
            Translate(element).BeginAnimation(TranslateTransform.YProperty, move);
        }
    }

    /// <summary>Os filhos de um painel entram um depois do outro.</summary>
    public static void Cascade(Panel panel, int stepMs = 35)
    {
        int i = 0;
        foreach (UIElement child in panel.Children) Enter(child, Easing.Stagger(i++, stepMs), 8);
    }

    /// <summary>Respira devagar (escala sobe e desce), para chamar atenção num botão parado.</summary>
    public static void Breathe(FrameworkElement element, double scale = 1.05, double seconds = 1.6)
    {
        var transform = new ScaleTransform(1, 1);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = transform;
        if (!Enabled) return;
        var anim = new DoubleAnimation(1, scale, new Duration(TimeSpan.FromSeconds(seconds)))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    /// <summary>Gira sem parar (anel enquanto analisa, ícone de "trabalhando").</summary>
    public static void Spin(FrameworkElement element, double seconds = 1.4)
    {
        var rotate = new RotateTransform(0);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = rotate;
        if (!Enabled) return;
        rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(seconds))) { RepeatBehavior = RepeatBehavior.Forever });
    }

    /// <summary>Para giros e respiração (volta ao normal).</summary>
    public static void Stop(FrameworkElement element)
    {
        if (element.RenderTransform is RotateTransform r) r.BeginAnimation(RotateTransform.AngleProperty, null);
        if (element.RenderTransform is ScaleTransform s) { s.BeginAnimation(ScaleTransform.ScaleXProperty, null); s.BeginAnimation(ScaleTransform.ScaleYProperty, null); }
        element.RenderTransform = Transform.Identity;
    }

    /// <summary>Pisca o brilho uma vez (algo mudou: nota nova, item corrigido).</summary>
    public static void Flash(UIElement element)
    {
        if (!Enabled) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.35, 1, Slow) { EasingFunction = Ease });
    }

    /// <summary>Anima uma propriedade numérica até o valor (anel de progresso, barras).</summary>
    public static void To(DependencyObject target, DependencyProperty property, double value, Duration? duration = null)
    {
        if (target is not IAnimatable animatable) return;
        if (!Enabled) { animatable.BeginAnimation(property, null); target.SetValue(property, value); return; }
        animatable.BeginAnimation(property, new DoubleAnimation(value, duration ?? Slow) { EasingFunction = Ease });
    }

    /// <summary>Conta de um número até outro no texto (ex.: a nota subindo de 0 a 87).</summary>
    public static void CountUp(TextBlock text, double from, double to, string format = "0", Duration? duration = null)
    {
        var total = (duration ?? new Duration(TimeSpan.FromMilliseconds(900))).TimeSpan;
        if (!Enabled) { text.Text = to.ToString(format); return; }
        var started = DateTime.UtcNow;
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var elapsed = DateTime.UtcNow - started;
            text.Text = Easing.Count(from, to, elapsed, total).ToString(format);
            if (elapsed >= total) { timer.Stop(); text.Text = to.ToString(format); }
        };
        timer.Start();
    }

    /// <summary>Abre ou fecha um painel suavemente (seções do menu lateral).</summary>
    public static void Toggle(FrameworkElement panel, bool open)
    {
        if (panel.LayoutTransform is not ScaleTransform scale)
        {
            scale = new ScaleTransform(1, open ? 0 : 1);
            panel.LayoutTransform = scale;
        }
        if (!Enabled)
        {
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            panel.BeginAnimation(UIElement.OpacityProperty, null);
            scale.ScaleY = open ? 1 : 0;
            panel.Opacity = open ? 1 : 0;
            panel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        panel.Visibility = Visibility.Visible;
        var anim = new DoubleAnimation(open ? 1 : 0, Normal) { EasingFunction = Ease };
        if (!open) anim.Completed += (_, _) => { if (scale.ScaleY == 0) panel.Visibility = Visibility.Collapsed; };
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        panel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(open ? 1 : 0, Normal));
    }

    /// <summary>Gira um ícone até um ângulo (a setinha das seções).</summary>
    public static void RotateTo(FrameworkElement element, double angle)
    {
        if (element.RenderTransform is not RotateTransform rotate)
        {
            rotate = new RotateTransform(angle);
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = rotate;
            return;
        }
        if (!Enabled) { rotate.BeginAnimation(RotateTransform.AngleProperty, null); rotate.Angle = angle; return; }
        rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(angle, Fast) { EasingFunction = Ease });
    }
}
