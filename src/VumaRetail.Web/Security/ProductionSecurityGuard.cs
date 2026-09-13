using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace VumaRetail.Web.Security;

/// <summary>Rejects unsafe shipped configuration before a production host accepts traffic.</summary>
public static class ProductionSecurityGuard
{
    private const string PlaceholderDatabasePassword = "CHANGE_ME";

    /// <summary>Validates secrets and transport configuration for a non-development host.</summary>
    public static void Validate(IConfiguration configuration, IHostEnvironment environment, bool cloudHost)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        if (environment.IsDevelopment())
        {
            return;
        }

        string[] connectionNames = cloudHost ? ["Vuma", "Registry"] : ["Vuma"];
        foreach (string name in connectionNames)
        {
            string? connection = configuration.GetConnectionString(name);
            if (string.IsNullOrWhiteSpace(connection))
            {
                throw new InvalidOperationException($"ConnectionStrings:{name} is required outside Development.");
            }

            if (connection.Contains(PlaceholderDatabasePassword, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"ConnectionStrings:{name} still contains the shipped database placeholder.");
            }
        }

        string? backupKey = configuration["Vuma:Backup:Encryption:Key"];
        if (!Is256BitBase64(backupKey))
        {
            throw new InvalidOperationException(
                "Vuma:Backup:Encryption:Key must be a 256-bit base64 secret outside Development.");
        }

        string? httpsUrl = configuration["Kestrel:Endpoints:Https:Url"];
        if (!Uri.TryCreate(httpsUrl, UriKind.Absolute, out Uri? uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Kestrel:Endpoints:Https:Url must be configured with an HTTPS URL outside Development.");
        }

        RequireCertificate(configuration, "Https");

        if (!cloudHost)
        {
            string? terminalUrl = configuration["Kestrel:Endpoints:TerminalHttps:Url"];
            string? clientCertificateMode = configuration["Kestrel:Endpoints:TerminalHttps:ClientCertificateMode"];
            if (!Uri.TryCreate(terminalUrl, UriKind.Absolute, out Uri? terminalUri)
                || !string.Equals(terminalUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(clientCertificateMode, "RequireCertificate", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The store TerminalHttps endpoint must use HTTPS and RequireCertificate outside Development.");
            }

            RequireCertificate(configuration, "TerminalHttps");
        }
    }

    private static void RequireCertificate(IConfiguration configuration, string endpoint)
    {
        string prefix = $"Kestrel:Endpoints:{endpoint}:Certificate:";
        string? path = configuration[$"{prefix}Path"];
        string? password = configuration[$"{prefix}Password"];
        if (string.IsNullOrWhiteSpace(path)
            || path.Contains("REPLACE_ME", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(password)
            || password.Contains("REPLACE_ME", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Kestrel:Endpoints:{endpoint}:Certificate path and password are required outside Development.");
        }
    }

    private static bool Is256BitBase64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            return Convert.FromBase64String(value).Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
