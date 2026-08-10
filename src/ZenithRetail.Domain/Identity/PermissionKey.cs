using ZenithRetail.Domain.Primitives;

namespace ZenithRetail.Domain.Identity;

/// <summary>
/// A permission, shaped <c>module.entity.action</c> — for example <c>inventory.stocktake.approve</c>.
/// </summary>
/// <remarks>
/// <para>
/// ADR-013. Permissions are strings everywhere they are used: in a role's grant list, in an endpoint
/// attribute, in a seed script, in the JSON the Android app drives its navigation from. A string
/// validated only where it happens to be used is a string that will eventually be misspelled in a
/// seed script and grant nothing at all, silently. This type is where the shape is decided once.
/// </para>
/// <para>
/// Three segments, not two and not four. The module segment is what lets the catalogue report who
/// owns a permission and what lets a module be extracted (ADR-010); the entity and action segments
/// are what let a role be described to a shop owner in words they recognise.
/// </para>
/// </remarks>
public readonly record struct PermissionKey
{
    /// <summary>The separator between segments.</summary>
    public const char Separator = '.';

    /// <summary>The number of segments a permission has: module, entity, action.</summary>
    public const int SegmentCount = 3;

    /// <summary>The longest a permission may be, matching the database column.</summary>
    public const int MaxLength = 128;

    private PermissionKey(string module, string entity, string action)
    {
        Module = module;
        EntityName = entity;
        Action = action;
    }

    /// <summary>The owning module, which is also its database schema (ADR-010).</summary>
    public string Module { get; }

    /// <summary>What the permission is about — <c>stocktake</c>, <c>user</c>, <c>price</c>.</summary>
    public string EntityName { get; }

    /// <summary>What may be done to it — <c>view</c>, <c>create</c>, <c>approve</c>.</summary>
    public string Action { get; }

    /// <summary>The full <c>module.entity.action</c> string, which is how it is stored and transported.</summary>
    public string Value => $"{Module}{Separator}{EntityName}{Separator}{Action}";

    /// <summary>Parses a permission string, throwing if it is not well-formed.</summary>
    /// <param name="value">The candidate <c>module.entity.action</c> string.</param>
    /// <returns>The parsed permission.</returns>
    /// <exception cref="InvalidPermissionKeyException">The string is not a valid permission.</exception>
    public static PermissionKey Parse(string value)
        => TryParse(value, out PermissionKey key)
            ? key
            : throw new InvalidPermissionKeyException(value);

    /// <summary>Parses a permission string without throwing.</summary>
    /// <param name="value">The candidate <c>module.entity.action</c> string.</param>
    /// <param name="key">The parsed permission, if it is well-formed.</param>
    /// <returns>True if the string is a valid permission.</returns>
    public static bool TryParse(string? value, out PermissionKey key)
    {
        key = default;

        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
        {
            return false;
        }

        string[] segments = value.Split(Separator);

        if (segments.Length != SegmentCount || !segments.All(IsWellFormedSegment))
        {
            return false;
        }

        key = new PermissionKey(segments[0], segments[1], segments[2]);
        return true;
    }

    /// <summary>The full <c>module.entity.action</c> string.</summary>
    /// <returns>The permission as it is stored.</returns>
    public override string ToString() => Value;

    /// <summary>
    /// A segment is lower-case ASCII letters, digits and hyphens, starting with a letter.
    /// </summary>
    /// <remarks>
    /// Lower case is not tidiness. A permission is compared as a string in a hot path — every request
    /// — and a case-insensitive comparison there is both slower and an invitation to store
    /// <c>Inventory.Stocktake.Approve</c> and <c>inventory.stocktake.approve</c> as two grants that
    /// look identical in the admin UI.
    /// </remarks>
    private static bool IsWellFormedSegment(string segment)
        => segment.Length > 0
            && char.IsAsciiLetterLower(segment[0])
            && segment.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-');
}

/// <summary>Thrown when a string is used as a permission but is not shaped like one.</summary>
/// <param name="value">The offending string.</param>
public sealed class InvalidPermissionKeyException(string? value)
    : DomainException(
        "PERMISSION_KEY_MALFORMED",
        $"'{value}' is not a permission. Permissions are lower-case module.entity.action (ADR-013).");
