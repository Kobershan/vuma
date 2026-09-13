using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using VumaRetail.Application.Hr;
using VumaRetail.Domain.HrManagement;

namespace VumaRetail.Infrastructure.Security;

/// <summary>Issues opaque, short-lived document download grants.</summary>
public sealed class EmployeeDocumentDownloadAuthorizer(IConfiguration configuration) : IEmployeeDocumentDownloadAuthorizer
{
    public string Create(EmployeeDocument document, DateTimeOffset expiresAtUtc)
    {
        ArgumentNullException.ThrowIfNull(document);
        string secret = configuration["Security:EmployeeDocumentDownloadKey"]
            ?? throw new InvalidOperationException("Employee document download signing key is not configured.");
        if (Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("Employee document download signing key must contain at least 32 bytes.");
        string payload = $"{document.Id:N}.{expiresAtUtc.ToUnixTimeSeconds()}";
        byte[] signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload));
        return $"{payload}.{Convert.ToHexStringLower(signature)}";
    }

    public bool Validate(string token, Guid documentId, DateTimeOffset asOfUtc)
    {
        if (string.IsNullOrWhiteSpace(token) || documentId == Guid.Empty)
            return false;
        string[] parts = token.Split('.', StringSplitOptions.None);
        if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "N", out Guid tokenDocument) ||
            tokenDocument != documentId || !long.TryParse(parts[1], out long expirySeconds) ||
            expirySeconds <= asOfUtc.ToUniversalTime().ToUnixTimeSeconds())
            return false;
        string secret = configuration["Security:EmployeeDocumentDownloadKey"] ?? string.Empty;
        if (Encoding.UTF8.GetByteCount(secret) < 32)
            return false;
        string payload = $"{parts[0]}.{parts[1]}";
        byte[] expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload));
        try
        {
            return CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(parts[2]));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
