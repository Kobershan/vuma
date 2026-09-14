using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using VumaRetail.Application.Assets;
using VumaRetail.Domain.Assets;

namespace VumaRetail.Infrastructure.Security;

/// <summary>Issues opaque, short-lived grants for checklist evidence references.</summary>
public sealed class ChecklistEvidenceAuthorizer(IConfiguration configuration) : IChecklistEvidenceAuthorizer
{
    public string Create(ChecklistExecution execution, DateTimeOffset expiresAtUtc)
    {
        ArgumentNullException.ThrowIfNull(execution);
        string payload = $"{execution.Id:N}.{expiresAtUtc.ToUnixTimeSeconds()}";
        return $"{payload}.{Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(GetSecret()), Encoding.UTF8.GetBytes(payload)))}";
    }

    public bool Validate(string token, Guid executionId, DateTimeOffset asOfUtc)
    {
        if (string.IsNullOrWhiteSpace(token) || executionId == Guid.Empty) return false;
        string[] parts = token.Split('.', StringSplitOptions.None);
        if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "N", out Guid id) || id != executionId ||
            !long.TryParse(parts[1], out long expiry) || expiry <= asOfUtc.ToUniversalTime().ToUnixTimeSeconds()) return false;
        try
        {
            byte[] expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(GetSecret()), Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}"));
            return CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(parts[2]));
        }
        catch (InvalidOperationException) { return false; }
        catch (FormatException) { return false; }
    }

    private string GetSecret()
    {
        string secret = configuration["Security:ChecklistEvidenceDownloadKey"]
            ?? throw new InvalidOperationException("Checklist evidence download signing key is not configured.");
        if (Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("Checklist evidence download signing key must contain at least 32 bytes.");
        return secret;
    }
}
