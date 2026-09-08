using System.Security.Cryptography;
using System.Text;

namespace VumaRetail.Infrastructure.Registry;

/// <summary>Deterministic document ids for mixed-basket legs (Stage 09b).</summary>
/// <remarks>
/// Same session, company and purpose mint the same id on every attempt (UUIDv5-shaped), so a
/// retried leg finds its rows instead of doubling them — the §4.11 lesson applied to every
/// document a leg writes, including sale lines (which is what lets a later return name the
/// exact till line it credits).
/// </remarks>
internal static class TradingLegIds
{
    /// <summary>Mints the deterministic id for one leg document.</summary>
    public static Guid DocumentId(Guid sessionId, Guid companyId, string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes($"{sessionId:N}:{companyId:N}:{purpose}"));
        byte[] guid = new byte[16];
        Array.Copy(hash, guid, 16);
        guid[6] = (byte)((guid[6] & 0x0F) | 0x50);
        guid[8] = (byte)((guid[8] & 0x3F) | 0x80);
        return new Guid(guid);
    }

    /// <summary>Mints the deterministic till line id for one session line.</summary>
    public static Guid SaleLineId(Guid sessionId, Guid companyId, Guid sessionLineId)
        => DocumentId(sessionId, companyId, $"sale-line-{sessionLineId:N}");
}
