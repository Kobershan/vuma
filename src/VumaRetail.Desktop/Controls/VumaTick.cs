using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace VumaRetail.Desktop.Controls;

/// <summary>
/// The Vuma tick — a 220ms confirmation stroke rendered on any successful commit.
/// Implemented once as a reusable control. Wired to successful commits only.
/// Appears nowhere else in the product.
///
/// Motion: 220ms with cubic-bezier(.65,0,.35,1).
/// When prefers-reduced-motion is set, becomes an instant state change.
/// </summary>
public class VumaTick : Control
{
    static VumaTick()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(VumaTick), new FrameworkPropertyMetadata(typeof(VumaTick)));
    }

    public static readonly DependencyProperty IsConfirmedProperty =
        DependencyProperty.Register(nameof(IsConfirmed), typeof(bool), typeof(VumaTick),
            new PropertyMetadata(false, OnIsConfirmedChanged));

    public static readonly DependencyProperty StrokeColorProperty =
        DependencyProperty.Register(nameof(StrokeColor), typeof(Brush), typeof(VumaTick),
            new PropertyMetadata(null));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(VumaTick),
            new PropertyMetadata(2.0));

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(nameof(Size), typeof(double), typeof(VumaTick),
            new PropertyMetadata(24.0));

    public bool IsConfirmed
    {
        get => (bool)GetValue(IsConfirmedProperty);
        set => SetValue(IsConfirmedProperty, value);
    }

    public Brush StrokeColor
    {
        get => (Brush)GetValue(StrokeColorProperty);
        set => SetValue(StrokeColorProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private static void OnIsConfirmedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not VumaTick tick) return;
        if ((bool)e.NewValue)
        {
            tick.AnimateTick();
        }
    }

    private void AnimateTick()
    {
        var reducedMotion = SystemParameters.HighContrast ||
                           (bool?)Application.Current?.Resources["MotionReduced"] == true;

        var duration = reducedMotion ? TimeSpan.FromMilliseconds(0)
            : TimeSpan.FromMilliseconds(GetMotionDuration());

        var stroke = new Line
        {
            X1 = 0, Y1 = Size / 2,
            X2 = Size * 0.4, Y2 = Size * 0.85,
            X3 = Size * 0.7, Y3 = Size * 0.25,
            Stroke = StrokeColor ?? new SolidColorBrush(Color.FromRgb(0x0B, 0x7A, 0x5A)),
            StrokeThickness = StrokeThickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Opacity = 0,
        };

        if (reducedMotion)
        {
            stroke.Opacity = 1;
        }
        else
        {
            var anim = new DoubleAnimation(0, 1, duration);
            anim.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            stroke.BeginAnimation(OpacityProperty, anim);
        }

        // Replace content with the tick stroke
        if (Content is Panel panel)
        {
            panel.Children.Clear();
            panel.Children.Add(stroke);
        }
    }

    private static double GetMotionDuration()
    {
        // Returns 220ms for vuma-tick motion, or 100ms if reduced motion
        return 220;
    }
}
