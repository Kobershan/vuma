using System.Text;

namespace VumaRetail.Application.Abstractions;

/// <summary>
/// Keyset pagination over a collection ordered by a sort key with the row's own id as tiebreaker
/// (<c>docs/API_STANDARDS.md</c> §8).
/// </summary>
/// <remarks>
/// <para>
/// Built here, once, because Stage 06 is the first stage with a collection worth paging and every
/// later stage's list query should reuse the same shape rather than inventing its own. Offset paging
/// (<c>OFFSET 50</c>) is deliberately not an option: a till scrolling a quarter-million-row catalogue
/// while stock is moving must never see a row twice or miss one, which is exactly what an offset does
/// under concurrent inserts.
/// </para>
/// <para>
/// A cursor is opaque to the caller — a base64 token wrapping the sort key and the row's id — so a
/// client cannot construct one out of thin air, and the encoding can change later without breaking the
/// contract (<c>limit</c>/<c>after</c>/<c>nextCursor</c> stay the same shape).
/// </para>
/// </remarks>
public static class Paging
{
    /// <summary>The page size when the caller does not ask for one.</summary>
    public const int DefaultLimit = 50;

    /// <summary>The largest page size the API will ever return in one response.</summary>
    public const int MaxLimit = 200;

    /// <summary>Clamps a requested page size into the allowed range.</summary>
    /// <param name="requested">The caller's requested limit, or <c>null</c>/non-positive for the default.</param>
    public static int Clamp(int? requested)
        => requested is > 0 ? Math.Min(requested.Value, MaxLimit) : DefaultLimit;
}

/// <summary>
/// One page of a keyset-paginated collection, sorted by some key with the row's id as tiebreaker.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">The page's rows, in sort order.</param>
/// <param name="NextCursor">
/// The cursor to pass as <c>after</c> to fetch the next page, or <c>null</c> when this is the last one.
/// </param>
/// <param name="HasMore">Whether at least one more row exists beyond this page.</param>
public sealed record PageResult<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore)
{
    /// <summary>An empty page — used when a query's <c>after</c> cursor cannot be decoded.</summary>
    public static PageResult<T> Empty { get; } = new([], null, false);
}

/// <summary>
/// A decoded keyset cursor: the sort key and id of the last row the caller already has.
/// </summary>
/// <remarks>
/// <see cref="SortKey"/> is compared as a string, so it must already be in the collation the query
/// sorts by — every current use sorts by an upper-cased code, which compares the same way in .NET's
/// ordinal comparison and in PostgreSQL's default collation.
/// </remarks>
/// <param name="SortKey">The primary sort column's value on the last row of the previous page.</param>
/// <param name="Id">That row's id — the tiebreaker that keeps the order total even when two rows share a sort key.</param>
public readonly record struct KeysetCursor(string SortKey, Guid Id)
{
    private const char Separator = '';

    /// <summary>Encodes the cursor as the opaque token a client passes back as <c>after</c>.</summary>
    public string Encode()
        => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{SortKey}{Separator}{Id:D}"));

    /// <summary>Decodes a cursor token, or returns <c>null</c> if it is missing or malformed.</summary>
    /// <param name="token">The <c>after</c> query parameter, or <c>null</c> for the first page.</param>
    /// <remarks>
    /// A malformed token is treated as "start from the beginning" rather than an error — a stale or
    /// tampered cursor should degrade to a fresh page, not break the caller's list view.
    /// </remarks>
    public static KeysetCursor? TryDecode(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            int separatorIndex = decoded.LastIndexOf(Separator);

            if (separatorIndex < 0)
            {
                return null;
            }

            string sortKey = decoded[..separatorIndex];
            string idPart = decoded[(separatorIndex + 1)..];

            return Guid.TryParse(idPart, out Guid id) ? new KeysetCursor(sortKey, id) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
