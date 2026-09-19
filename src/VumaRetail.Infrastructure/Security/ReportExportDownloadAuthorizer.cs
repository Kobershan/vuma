using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using VumaRetail.Application.Reporting;
using VumaRetail.Domain.Reporting;

namespace VumaRetail.Infrastructure.Security;

/// <summary>Issues opaque, short-lived grants for completed report artifacts.</summary>
public sealed class ReportExportDownloadAuthorizer(IConfiguration configuration) : IReportExportDownloadAuthorizer
{
    public string Create(ReportExport export, DateTimeOffset expiresAtUtc)
    {
        ArgumentNullException.ThrowIfNull(export);
        string secret = GetSecret();
        string payload = $"{export.Id:N}.{expiresAtUtc.ToUnixTimeSeconds()}";
        return $"{payload}.{Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload)))}";
    }

    public bool Validate(string token, Guid exportId, DateTimeOffset asOfUtc)
    {
        if (string.IsNullOrWhiteSpace(token) || exportId == Guid.Empty)
        {
            return false;
        }

        string[] parts = token.Split('.', StringSplitOptions.None);
        if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "N", out Guid tokenId) || tokenId != exportId ||
            !long.TryParse(parts[1], out long expiry) || expiry <= asOfUtc.ToUniversalTime().ToUnixTimeSeconds())
        {
            return false;
        }

        string secret;
        try { secret = GetSecret(); }
        catch (InvalidOperationException) { return false; }
        byte[] expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}"));
        try { return CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(parts[2])); }
        catch (FormatException) { return false; }
    }

    private string GetSecret()
    {
        string secret = configuration["Security:ReportExportDownloadKey"]
            ?? throw new InvalidOperationException("Report export download signing key is not configured.");
        if (Encoding.UTF8.GetByteCount(secret) < 32)
        {
            throw new InvalidOperationException("Report export download signing key must contain at least 32 bytes.");
        }

        return secret;
    }
}
