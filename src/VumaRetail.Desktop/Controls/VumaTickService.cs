using System.Windows;
using System.Windows.Threading;

namespace VumaRetail.Desktop.Controls;

/// <summary>
/// Wires the VumaTick control to successful commit events.
/// The tick appears only on successful commits and nowhere else in the product.
/// </summary>
public static class VumaTickService
{
    private static VumaTick? _tick;

    /// <summary>
    /// Initialize the Vuma tick service with a reference to the tick control.
    /// Call this during application startup.
    /// </summary>
    public static void Initialize(VumaTick tick)
    {
        _tick = tick;
    }

    /// <summary>
    /// Trigger the Vuma tick animation on a successful commit.
    /// Only called after a command handler completes successfully.
    /// </summary>
    public static void OnCommitSuccess()
    {
        if (_tick == null) return;
        _tick.IsConfirmed = true;
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(270)
        };
        timer.Tick += (s, e) =>
        {
            _tick.IsConfirmed = false;
            timer.Stop();
        };
        timer.Start();
    }

    /// <summary>
    /// Called when a commit fails. The tick does not appear.
    /// </summary>
    public static void OnCommitFailure()
    {
        // No tick on failure. The tick appears only on successful commits.
    }
}
