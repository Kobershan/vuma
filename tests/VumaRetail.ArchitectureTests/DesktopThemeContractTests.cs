using System.Text.RegularExpressions;
using VumaRetail.TestSupport;

namespace VumaRetail.ArchitectureTests;

/// <summary>Semantic checks for the desktop resources that source-only scans cannot prove.</summary>
public sealed class DesktopThemeContractTests
{
    [Fact]
    public void MainWindow_resources_exist_in_both_loaded_theme_dictionaries()
    {
        string root = SolutionSource.RepositoryRoot.FullName;
        string window = File.ReadAllText(Path.Combine(root, "src/VumaRetail.Desktop/MainWindow.xaml"));
        string[] referenced = Regex.Matches(window, @"\{StaticResource\s+(Vuma[A-Za-z0-9]+)\}")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (string theme in new[] { "Light.xaml", "Dark.xaml" })
        {
            string content = File.ReadAllText(Path.Combine(root, "src/VumaRetail.Desktop/Themes", theme));
            foreach (string key in referenced)
            {
                Assert.Contains($"x:Key=\"{key}\"", content);
            }
        }
    }

    [Fact]
    public void App_and_theme_manager_load_the_generated_dictionary_contract()
    {
        string root = SolutionSource.RepositoryRoot.FullName;
        string app = File.ReadAllText(Path.Combine(root, "src/VumaRetail.Desktop/App.xaml"));
        string manager = File.ReadAllText(Path.Combine(root, "src/VumaRetail.Desktop/ThemeManager.cs"));

        Assert.Contains("Themes/Light.xaml", app);
        Assert.Contains("Themes/Light.xaml", manager);
        Assert.Contains("Themes/Dark.xaml", manager);
        Assert.DoesNotContain("Themes/LightTheme.xaml", app + manager);
        Assert.DoesNotContain("Themes/DarkTheme.xaml", app + manager);
    }
}
