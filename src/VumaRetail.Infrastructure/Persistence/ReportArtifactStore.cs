using VumaRetail.Application.Reporting;

namespace VumaRetail.Infrastructure.Persistence;

public sealed class ReportArtifactStoreOptions
{
    public string RootDirectory { get; init; } = Path.Combine(Path.GetTempPath(), "vuma-report-artifacts");
}

public sealed class FileSystemReportArtifactStore(ReportArtifactStoreOptions options) : IReportArtifactStore
{
    public async Task<string> PutAsync(Guid tenantId, Guid companyId, Guid exportId, string extension, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        string safeExtension = NormalizeExtension(extension);
        string relative = $"{tenantId:D}/{companyId:D}/{exportId:D}{safeExtension}";
        string path = Path.Combine(options.RootDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using FileStream target = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        return relative;
    }

    public Task<Stream> GetAsync(string artifactReference, CancellationToken cancellationToken = default)
    {
        string relative = artifactReference.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith('/') || relative.Contains("../", StringComparison.Ordinal))
        {
            throw new ArgumentException("The artifact reference is invalid.", nameof(artifactReference));
        }
        string root = Path.GetFullPath(options.RootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(options.RootDirectory, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root, StringComparison.Ordinal))
        {
            throw new ArgumentException("The artifact reference is outside the store.", nameof(artifactReference));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The report artifact was not found.", artifactReference);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
    }

    private static string NormalizeExtension(string extension)
    {
        string value = extension.Trim();
        if (value.Length == 0 || value.Length > 10 || value.Any(character => !char.IsLetterOrDigit(character) && character != '.'))
        {
            throw new ArgumentException("The artifact extension is invalid.", nameof(extension));
        }
        return value.StartsWith('.') ? value.ToLowerInvariant() : $".{value.ToLowerInvariant()}";
    }
}
