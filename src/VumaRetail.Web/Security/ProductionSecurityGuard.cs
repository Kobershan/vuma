using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using VumaRetail.Infrastructure.Security.Identity;

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

        if (CompanyExemptionTelemetry.Count > 0)
        {
            Console.Error.WriteLine(
                $"WARNING: {CompanyExemptionTelemetry.Count} pre-registry company exemption requests "
                + "were observed before this process restarted; see ADR-157 and Stage 31.");
        }

        string? allowedHosts = configuration["AllowedHosts"];
        if (string.IsNullOrWhiteSpace(allowedHosts)
            || allowedHosts == "*"
            || allowedHosts.Contains("REPLACE_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("AllowedHosts must be set to the real hostname(s) outside Development.");
        }

        if (cloudHost && string.IsNullOrWhiteSpace(configuration["Vuma:Loyalty:Public:WebhookSecret"]))
        {
            throw new InvalidOperationException(
                "Vuma:Loyalty:Public:WebhookSecret must be configured on the cloud tier outside Development.");
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

        string? signingKey = configuration["Vuma:Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey)
            || signingKey.Contains("development-only", StringComparison.OrdinalIgnoreCase)
            || signingKey.Contains("replace", StringComparison.OrdinalIgnoreCase)
            || signingKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Vuma:Jwt:SigningKey must be a non-placeholder secret of at least 32 characters outside Development.");
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
