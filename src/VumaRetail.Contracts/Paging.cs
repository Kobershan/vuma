namespace VumaRetail.Contracts;

/// <summary>
/// One page of a keyset-paginated collection, as returned by the API (<c>docs/API_STANDARDS.md</c> §8).
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">The page's rows, in sort order.</param>
/// <param name="NextCursor">
/// The opaque cursor to pass as <c>after</c> on the next request, or <c>null</c> when this is the last page.
/// </param>
/// <param name="HasMore">Whether at least one more row exists beyond this page.</param>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore);
