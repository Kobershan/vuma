namespace ZenithRetail.ArchitectureTests;

/// <summary>
/// Locates the solution's C# source, for the rules that are better checked in source than in IL.
/// </summary>
/// <remarks>
/// Two of the Stage 01 rules — no <c>SaveChanges</c> outside the persistence layer, and no wall-clock
/// read outside <c>SystemClock</c> — are about what somebody typed. Checking the compiled assembly
/// would find the same violations but could only name the method, whereas the point of these tests is
/// to hand the author a file and a line and the reason.
/// </remarks>
public static class SolutionSource
{
    private static readonly Lazy<DirectoryInfo> Root = new(FindRoot);

    /// <summary>The repository root — the directory holding <c>ZenithRetail.sln</c>.</summary>
    public static DirectoryInfo RepositoryRoot => Root.Value;

    /// <summary>
    /// Every <c>.cs</c> file under <c>src/</c> that a human wrote, as (relative path, contents).
    /// </summary>
    /// <remarks>
    /// Generated code is excluded. EF migrations are scaffolded and obj/ is build output; holding
    /// either to a hand-written-code rule would produce failures nobody can act on.
    /// </remarks>
    public static IReadOnlyList<(string Path, string Text)> ProductionFiles()
    {
        DirectoryInfo source = new(Path.Combine(RepositoryRoot.FullName, "src"));

        return source
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(file => (
                Path: Path.GetRelativePath(RepositoryRoot.FullName, file.FullName).Replace('\\', '/'),
                Text: File.ReadAllText(file.FullName)))
            .ToList();
    }

    /// <summary>
    /// Finds every line of every production file matching a predicate, excluding named files.
    /// </summary>
    /// <param name="matches">Whether a line of code violates the rule.</param>
    /// <param name="exemptFiles">Repository-relative paths permitted to violate it.</param>
    /// <returns>Violations, formatted as <c>path:line — text</c>.</returns>
    public static IReadOnlyList<string> FindViolations(Func<string, bool> matches, params string[] exemptFiles)
    {
        ArgumentNullException.ThrowIfNull(matches);

        HashSet<string> exempt = new(exemptFiles ?? [], StringComparer.OrdinalIgnoreCase);
        List<string> violations = [];

        foreach ((string path, string text) in ProductionFiles())
        {
            if (exempt.Contains(path))
            {
                continue;
            }

            string[] lines = text.Split('\n');

            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                string trimmed = line.TrimStart();

                // Comments and XML docs discuss these rules constantly — this file included.
                if (trimmed.StartsWith("//", StringComparison.Ordinal)
                    || trimmed.StartsWith("///", StringComparison.Ordinal)
                    || trimmed.StartsWith('*'))
                {
                    continue;
                }

                if (matches(line))
                {
                    violations.Add($"{path}:{index + 1} — {trimmed.Trim()}");
                }
            }
        }

        return violations;
    }

    private static DirectoryInfo FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ZenithRetail.sln")))
        {
            directory = directory.Parent;
        }

        return directory ?? throw new InvalidOperationException(
            "Could not find ZenithRetail.sln above the test output directory.");
    }
}
