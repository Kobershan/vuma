using System.Text.Json;
using System.Text.Json.Serialization;

namespace VumaRetail.Domain.Licensing;

/// <summary>
/// What a plan permits, in quantities (<c>LICENSING.md</c> §6).
/// </summary>
/// <remarks>
/// <para>
/// Three of these are hard and three are soft, and the split is the whole design. Hard limits — stores,
/// terminals, named users — are checked when somebody <em>configures</em> something and refuse
/// clearly. Soft limits — transactions, storage, API calls — are metered and warned about and
/// <b>never block</b>, because a limit that can stop a shop trading on the last Saturday of the month
/// is a limit that will eventually stop a shop trading on the last Saturday of the month.
/// </para>
/// <para>
/// Every field is a count. There is no money here: what a tenant pays is the vendor's business and
/// lives in the control plane (ADR-025).
/// </para>
/// </remarks>
/// <param name="Stores">Trading locations. Hard.</param>
/// <param name="Terminals">Enrolled tills and back-office machines. Hard.</param>
/// <param name="NamedUsers">People who can sign in. Hard.</param>
/// <param name="TransactionsPerMonth">Transactions in a billing month. Soft.</param>
/// <param name="StorageBytes">Stored bytes. Soft.</param>
/// <param name="ApiCallsPerMonth">API calls in a billing month. Soft.</param>
public sealed record LicenceLimits(
    int Stores,
    int Terminals,
    int NamedUsers,
    long TransactionsPerMonth,
    long StorageBytes,
    long ApiCallsPerMonth)
{
    /// <summary>Everything permitted. What a trial and the development licence carry.</summary>
    public static LicenceLimits Unlimited { get; } = new(
        int.MaxValue,
        int.MaxValue,
        int.MaxValue,
        long.MaxValue,
        long.MaxValue,
        long.MaxValue);

    /// <summary>The ceiling for one kind of limit.</summary>
    /// <param name="kind">Which limit.</param>
    public long Ceiling(LimitKind kind) => kind switch
    {
        LimitKind.Stores => Stores,
        LimitKind.Terminals => Terminals,
        LimitKind.NamedUsers => NamedUsers,
        LimitKind.TransactionsPerMonth => TransactionsPerMonth,
        LimitKind.StorageBytes => StorageBytes,
        LimitKind.ApiCallsPerMonth => ApiCallsPerMonth,
        _ => long.MaxValue,
    };

    /// <summary>
    /// True for the limits that refuse, false for the ones that only meter.
    /// </summary>
    /// <param name="kind">Which limit.</param>
    /// <remarks>
    /// Expressed here rather than at each call site so that "which limits can stop somebody working"
    /// has exactly one answer, and so a new limit has to choose deliberately.
    /// </remarks>
    public static bool IsHard(LimitKind kind)
        => kind is LimitKind.Stores or LimitKind.Terminals or LimitKind.NamedUsers;
}

/// <summary>
/// Reads and writes the small JSON documents the licensing entities store inline.
/// </summary>
/// <remarks>
/// Entitlement sets, limit records and fingerprint component hashes are all naturally documents rather
/// than columns: they are read whole, written whole, and versioned by the licence that carried them.
/// Storing them as <c>jsonb</c> keeps them queryable in an investigation without a table per shape.
/// </remarks>
public static class FingerprintJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    /// <summary>Serialises the salted component hashes.</summary>
    /// <param name="hashes">The hashes.</param>
    public static string Write(IReadOnlyDictionary<FingerprintComponent, string> hashes)
        => JsonSerializer.Serialize(
            hashes.ToDictionary(entry => entry.Key.ToString(), entry => entry.Value, StringComparer.Ordinal),
            Options);

    /// <summary>Deserialises the salted component hashes, ignoring any component this build cannot name.</summary>
    /// <param name="json">The stored document.</param>
    public static IReadOnlyDictionary<FingerprintComponent, string> Read(string json)
    {
        Dictionary<string, string> raw = JsonSerializer
            .Deserialize<Dictionary<string, string>>(json, Options) ?? [];

        Dictionary<FingerprintComponent, string> hashes = [];

        foreach ((string name, string hash) in raw)
        {
            // An unknown component is skipped rather than thrown on. A fingerprint stored by a later
            // build with a sixth component must still score against this one — it scores lower, which
            // is the safe direction, and a hard failure here would be a store that cannot start after
            // a rollback.
            if (Enum.TryParse(name, out FingerprintComponent component) && Enum.IsDefined(component))
            {
                hashes[component] = hash;
            }
        }

        return hashes;
    }

    /// <summary>Serialises a set of entitlement flags.</summary>
    /// <param name="entitlements">The module flags this licence enables.</param>
    public static string WriteEntitlements(IEnumerable<string> entitlements)
        => JsonSerializer.Serialize(entitlements.Order(StringComparer.Ordinal).ToArray(), Options);

    /// <summary>Deserialises a set of entitlement flags.</summary>
    /// <param name="json">The stored document.</param>
    public static IReadOnlySet<string> ReadEntitlements(string json)
        => (JsonSerializer.Deserialize<string[]>(json, Options) ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Serialises a limit record.</summary>
    /// <param name="limits">The limits.</param>
    public static string WriteLimits(LicenceLimits limits) => JsonSerializer.Serialize(limits, Options);

    /// <summary>Deserialises a limit record, defaulting to unlimited if the document is empty.</summary>
    /// <param name="json">The stored document.</param>
    public static LicenceLimits ReadLimits(string json)
        => string.IsNullOrWhiteSpace(json)
            ? LicenceLimits.Unlimited
            : JsonSerializer.Deserialize<LicenceLimits>(json, Options) ?? LicenceLimits.Unlimited;

    /// <summary>Serialises the human-readable messages a lease carries for the licence screen.</summary>
    /// <param name="messages">The messages.</param>
    public static string WriteMessages(IEnumerable<string> messages)
        => JsonSerializer.Serialize(messages.ToArray(), Options);

    /// <summary>Deserialises the messages a lease carries.</summary>
    /// <param name="json">The stored document.</param>
    public static IReadOnlyList<string> ReadMessages(string json)
        => string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<string[]>(json, Options) ?? [];
}
