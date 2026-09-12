using System.Windows;

namespace VumaRetail.Desktop;

public partial class App : global::System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ThemeManager.Initialize();
        base.OnStartup(e);
        var window = new MainWindow(new SystemClock());
        window.Show();
    }
}
