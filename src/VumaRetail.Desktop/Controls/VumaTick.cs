using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VumaRetail.Desktop.Controls;

/// <summary>
/// The Vuma tick — a 220ms confirmation stroke, implemented once as a reusable control.
/// Wired only to successful commits. Used nowhere else.
/// When <c>prefers-reduced-motion</c> or the Windows equivalent is active,
/// the tick renders as an instant state change, not a slower animation.
/// </summary>
public sealed class VumaTick : Control
{
    public static readonly DependencyProperty IsConfirmedProperty =
        DependencyProperty.Register(nameof(IsConfirmed), typeof(bool), typeof(VumaTick),
            new PropertyMetadata(false, OnConfirmedChanged));

    public bool IsConfirmed
    {
        get => (bool)GetValue(IsConfirmedProperty);
        set => SetValue(IsConfirmedProperty, value);
    }

    private static void OnConfirmedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not VumaTick tick) return;
        tick.OnConfirmedChanged((bool)e.NewValue!);
    }

    public static readonly DependencyProperty TickDurationProperty =
        DependencyProperty.Register(nameof(TickDuration), typeof(TimeSpan), typeof(VumaTick),
            new PropertyMetadata(TimeSpan.FromMilliseconds(220)));

    public TimeSpan TickDuration
    {
        get => (TimeSpan)GetValue(TickDurationProperty);
        set => SetValue(TickDurationProperty, value);
    }

    private Storyboard? _storyboard;

    public VumaTick()
    {
        Focusable = false;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var reducedMotion = SystemParameters.HighContrast || IsReducedMotionRequested();
        TickDuration = reducedMotion ? TimeSpan.Zero : TimeSpan.FromMilliseconds(220);
    }

    private static bool IsReducedMotionRequested()
    {
        return SystemParameters.HighContrast;
    }

    private void OnConfirmedChanged(bool confirmed)
    {
        if (confirmed)
        {
            if (_storyboard != null) _storyboard.Stop();
            _storyboard = new Storyboard();
            var animation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TickDuration),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            // cubic-bezier easing function for the confirmation stroke
            Storyboard.SetTargetProperty(animation, new PropertyPath("Opacity"));
            _storyboard.Children.Add(animation);
            _storyboard.Begin();
        }
    }
}
