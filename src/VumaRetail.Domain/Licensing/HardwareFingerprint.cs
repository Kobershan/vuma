using System.Collections.Frozen;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace VumaRetail.Domain.Licensing;

/// <summary>One machine characteristic the hardware binding is composed from.</summary>
/// <remarks>
/// The set and the weights are <c>LICENSING.md</c> §3's table, verbatim. They are an enum rather than
/// strings so that adding a component is a compile-time event across every reader of a stored
/// fingerprint, and so a typo cannot silently create a sixth component worth nothing.
/// </remarks>
public enum FingerprintComponent
{
    /// <summary>Motherboard / system UUID. Weight 3.</summary>
    MotherboardUuid = 0,

    /// <summary>Windows machine GUID. Weight 3.</summary>
    MachineGuid = 1,

    /// <summary>Primary NIC MAC address. Weight 2.</summary>
    PrimaryMacAddress = 2,

    /// <summary>System volume serial. Weight 2.</summary>
    SystemVolumeSerial = 3,

    /// <summary>CPU signature. Weight 1.</summary>
    CpuSignature = 4,
}

/// <summary>
/// A machine's identity, stored as salted hashes and matched with tolerance
/// (<c>LICENSING.md</c> §3).
/// </summary>
/// <remarks>
/// <para>
/// Two properties matter and they pull in opposite directions. <b>Nothing raw is ever persisted or
/// transmitted</b>, because a database of customers' motherboard UUIDs and MAC addresses is a
/// liability with no upside. And <b>a replaced network card must not break a licence at 06:00 on a
/// Monday</b>, which means the comparison cannot be a single hash of everything — one changed byte
/// would change the digest and the machine would look like a different machine.
/// </para>
/// <para>
/// Both hold at once by hashing each component <em>separately</em> under a per-activation salt. A new
/// reading is hashed with the stored salt and compared component by component; the matching
/// components' weights are summed, and <see cref="MatchThreshold"/> out of <see cref="MaxScore"/>
/// means the same machine. Swap the NIC and the disk and the score is 7 — still bound. Move to a
/// different box entirely and it is 0 or 1, and a rebind is required.
/// </para>
/// </remarks>
public sealed record HardwareFingerprint
{
    /// <summary><c>LICENSING.md</c> §3's weights.</summary>
    private static readonly FrozenDictionary<FingerprintComponent, int> Weights =
        new Dictionary<FingerprintComponent, int>
        {
            [FingerprintComponent.MotherboardUuid] = 3,
            [FingerprintComponent.MachineGuid] = 3,
            [FingerprintComponent.PrimaryMacAddress] = 2,
            [FingerprintComponent.SystemVolumeSerial] = 2,
            [FingerprintComponent.CpuSignature] = 1,
        }.ToFrozenDictionary();

    /// <summary>The highest score a perfect match can reach: 3 + 3 + 2 + 2 + 1.</summary>
    public const int MaxScore = 11;

    /// <summary>
    /// The score at or above which two readings are the same machine.
    /// </summary>
    /// <remarks>
    /// Seven of eleven, from <c>LICENSING.md</c> §3. It is chosen so that the two components a
    /// business actually replaces — the network card and a data disk, 4 points together — leave a
    /// machine bound, while a wholesale move to different hardware does not.
    /// </remarks>
    public const int MatchThreshold = 7;

    private readonly FrozenDictionary<FingerprintComponent, string> _hashes;

    private HardwareFingerprint(string salt, FrozenDictionary<FingerprintComponent, string> hashes)
    {
        Salt = salt;
        _hashes = hashes;
    }

    /// <summary>The per-activation salt the component hashes were taken under, base64.</summary>
    public string Salt { get; }

    /// <summary>The salted component hashes, lower-case hex, keyed by component.</summary>
    public IReadOnlyDictionary<FingerprintComponent, string> ComponentHashes => _hashes;

    /// <summary>A fresh, random salt for a new activation.</summary>
    public static string NewSalt() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Hashes a set of raw machine readings under a salt.
    /// </summary>
    /// <param name="salt">The salt to hash under — a new one for an activation, the stored one to compare.</param>
    /// <param name="components">
    /// The raw readings. A component the machine cannot report is simply absent, and scores nothing —
    /// which is the correct behaviour on a virtual machine with no motherboard UUID.
    /// </param>
    /// <returns>The fingerprint.</returns>
    public static HardwareFingerprint Capture(
        string salt,
        IReadOnlyDictionary<FingerprintComponent, string> components)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(salt);
        ArgumentNullException.ThrowIfNull(components);

        byte[] key = Convert.FromBase64String(salt);

        Dictionary<FingerprintComponent, string> hashes = [];

        foreach ((FingerprintComponent component, string reading) in components)
        {
            if (string.IsNullOrWhiteSpace(reading))
            {
                continue;
            }

            // The component name goes into the hashed material, so the same serial appearing on two
            // different components cannot match across them.
            byte[] material = Encoding.UTF8.GetBytes(
                $"{component}:{reading.Trim().ToUpperInvariant()}");

            hashes[component] = Convert.ToHexStringLower(HMACSHA256.HashData(key, material));
        }

        return new HardwareFingerprint(salt, hashes.ToFrozenDictionary());
    }

    /// <summary>Rebuilds a fingerprint from what was stored.</summary>
    /// <param name="salt">The stored salt.</param>
    /// <param name="hashes">The stored component hashes.</param>
    /// <returns>The fingerprint.</returns>
    public static HardwareFingerprint FromStored(
        string salt,
        IReadOnlyDictionary<FingerprintComponent, string> hashes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(salt);
        ArgumentNullException.ThrowIfNull(hashes);

        return new HardwareFingerprint(salt, hashes.ToFrozenDictionary());
    }

    /// <summary>
    /// Scores another reading against this one: the summed weights of the components that match.
    /// </summary>
    /// <param name="candidate">A reading taken under this fingerprint's salt.</param>
    /// <returns>0 to <see cref="MaxScore"/>.</returns>
    public int Score(HardwareFingerprint candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        int score = 0;

        foreach ((FingerprintComponent component, string hash) in _hashes)
        {
            if (candidate._hashes.TryGetValue(component, out string? other)
                && string.Equals(hash, other, StringComparison.Ordinal))
            {
                score += Weights[component];
            }
        }

        return score;
    }

    /// <summary>
    /// The score this fingerprint requires to call a reading the same machine.
    /// </summary>
    /// <remarks>
    /// <see cref="MatchThreshold"/> when the machine reported everything, and the same
    /// <em>proportion</em> of whatever it could report when it did not. A machine with no motherboard
    /// UUID — a virtual machine, or a Windows box before Stage 31 supplies the WMI readings — captures
    /// eight points rather than eleven, and holding it to an absolute seven would mean a single
    /// replaced network card forced a rebind. That is the failure <c>LICENSING.md</c> §3's tolerance
    /// exists to prevent, so the rule scales with the evidence rather than pretending the evidence is
    /// there.
    /// </remarks>
    public int RequiredScore
    {
        get
        {
            int captured = _hashes.Keys.Sum(WeightOf);

            return captured >= MaxScore
                ? MatchThreshold
                : (int)Math.Ceiling(MatchThreshold * captured / (double)MaxScore);
        }
    }

    /// <summary>True when a reading scores at or above <see cref="RequiredScore"/> — the same machine.</summary>
    /// <param name="candidate">A reading taken under this fingerprint's salt.</param>
    public bool Matches(HardwareFingerprint candidate) => Score(candidate) >= RequiredScore;

    /// <summary>
    /// A single stable digest of the whole fingerprint, for the licence payload and the heartbeat.
    /// </summary>
    /// <remarks>
    /// Order-independent by construction — the components are sorted before hashing — so two readings
    /// of one machine produce one digest whatever order the provider enumerated them in. This is what
    /// the control plane compares across installs to spot a clone; it is <em>not</em> what the local
    /// tolerance check uses, because a digest cannot be partially equal.
    /// </remarks>
    public string Digest()
    {
        StringBuilder material = new();

        foreach (FingerprintComponent component in _hashes.Keys.Order())
        {
            material.Append(component.ToString())
                .Append('=')
                .Append(_hashes[component])
                .Append(';');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString())));
    }

    /// <summary>The weight a component contributes to the score.</summary>
    /// <param name="component">The component.</param>
    public static int WeightOf(FingerprintComponent component) => Weights.GetValueOrDefault(component);
}
