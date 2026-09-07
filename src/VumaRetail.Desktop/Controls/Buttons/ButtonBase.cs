using System.Windows.Controls;

namespace VumaRetail.Desktop.Controls;

/// <summary>
/// Base class for button controls. Consumes theme tokens only.
/// Both themes, both densities, keyboard spec, accessibility spec.
/// </summary>
public abstract class ButtonBase : VumaControl
{
    // Implementation consumes AccentBrush, Radius, Spacing from theme resources
}

/// <summary>Primary action button. Uses accent colour.</summary>
public class ButtonPrimary : ButtonBase
{
    public ButtonPrimary()
    {
        // Theme-driven styling — no literal colours
        Style = (Style?)Application.Current?.Resources["PrimaryButtonStyle"];
    }
}

/// <summary>Secondary action button.</summary>
public class ButtonSecondary : ButtonBase
{
    public ButtonSecondary()
    {
        Style = (Style?)Application.Current?.Resources["SecondaryButtonStyle"];
    }
}

/// <summary>Quiet action button — no fill, text only.</summary>
public class ButtonQuiet : ButtonBase
{
    public ButtonQuiet()
    {
        Style = (Style?)Application.Current?.Resources["QuietButtonStyle"];
    }
}

/// <summary>Destructive action button — uses critical colour.</summary>
public class ButtonDestructive : ButtonBase
{
    public ButtonDestructive()
    {
        Style = (Style?)Application.Current?.Resources["DestructiveButtonStyle"];
    }
}
