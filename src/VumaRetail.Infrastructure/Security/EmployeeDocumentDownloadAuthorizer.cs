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
}
