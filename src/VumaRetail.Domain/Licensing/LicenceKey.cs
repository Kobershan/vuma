using System.Security.Cryptography;
using System.Text;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Licensing;

/// <summary>
/// The human-readable key a customer types once, at activation: <c>VUMA-XXXXX-XXXXX-XXXXX-XXXXX</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>LICENSING.md</c> §2 specifies Base32 with a checksum "so a typo is caught before it hits the
/// network". That is the whole design brief and it is worth being precise about why: the person typing
/// this is a shop owner on the phone to the vendor, reading a code off an email, and the difference
/// between "that key is not valid" arriving instantly and arriving after a five-second round trip is
/// the difference between a correction and a support ticket.
/// </para>
/// <para>
/// The alphabet is <b>Crockford Base32</b> — no <c>I</c>, <c>L</c>, <c>O</c> or <c>U</c> — and
/// <see cref="Parse"/> folds the characters people actually type instead: <c>O</c> to <c>0</c>,
/// <c>I</c> and <c>L</c> to <c>1</c>. A key is therefore case-insensitive and hyphen-insensitive, and
/// the three confusions that account for nearly every mis-typed code cannot happen at all.
/// </para>
/// <para>
/// The prefix is <c>VUMA</c>. <c>LICENSING.md</c> was written before the product was renamed
/// (ADR-042) and still showed <c>ZNTH</c>; nothing had ever issued a key, so the document was
/// corrected rather than the code carrying the old product's name for ever.
/// </para>
/// </remarks>
public readonly record struct LicenceKey
{
    /// <summary>The Crockford Base32 alphabet: digits and consonants, minus the ambiguous letters.</summary>
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>What every key starts with, so a wrong-product key fails before anything else does.</summary>
    public const string Prefix = "VUMA";

    /// <summary>How many Base32 characters a key body carries, checksum included.</summary>
    public const int BodyLength = 20;

    private LicenceKey(string normalised) => Value = normalised;

    /// <summary>The canonical form: <c>VUMA-XXXXX-XXXXX-XXXXX-XXXXX</c>, upper case, hyphenated.</summary>
    public string Value { get; }

    /// <summary>The 20 Base32 characters without the prefix or the hyphens.</summary>
    public string Body => Value[(Prefix.Length + 1)..].Replace("-", string.Empty, StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>
    /// Parses a typed key, folding the ambiguous characters and checking the checksum.
    /// </summary>
    /// <param name="value">What the customer typed.</param>
    /// <returns>The parsed key.</returns>
    /// <exception cref="InvalidLicenceKeyException">
    /// The key is the wrong shape, uses characters that are not in the alphabet, or fails its checksum.
    /// </exception>
    public static LicenceKey Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        string body = Normalise(value);

        if (body.Length != BodyLength)
        {
            throw new InvalidLicenceKeyException(
                $"A licence key is {BodyLength} characters after the {Prefix} prefix; this one has {body.Length}.");
        }

        int sum = 0;

        for (int index = 0; index < BodyLength - 1; index++)
        {
            int digit = Alphabet.IndexOf(body[index], StringComparison.Ordinal);

            if (digit < 0)
            {
                throw new InvalidLicenceKeyException(
                    $"'{body[index]}' is not a character a licence key can contain.");
            }

            // Positionally weighted, and the weights are odd on purpose. An odd weight is coprime
            // with 32, so a single wrong character can never leave the sum unchanged — every
            // substitution is caught, which is the commonest typing mistake. Even weights would let a
            // digit that is out by eight at the right position slip through unnoticed.
            //
            // Transpositions — the second commonest — change the sum by (di - dj) x 2(i - j), so an
            // adjacent swap is caught unless the two characters are exactly sixteen apart in the
            // alphabet. That residual case is the price of a five-line checksum, and the network still
            // catches it a moment later.
            sum += digit * ((2 * index) + 1);
        }

        char expected = Alphabet[sum % Alphabet.Length];

        if (body[^1] != expected)
        {
            throw new InvalidLicenceKeyException(
                "That licence key has a typo in it — the checksum does not match. Check it and try again.");
        }

        return new LicenceKey($"{Prefix}-{body[..5]}-{body[5..10]}-{body[10..15]}-{body[15..]}");
    }

    /// <summary>Parses a key without throwing.</summary>
    /// <param name="value">What the customer typed.</param>
    /// <param name="key">The parsed key, when it parsed.</param>
    /// <returns>True when the key is well formed and its checksum matches.</returns>
    public static bool TryParse(string? value, out LicenceKey key)
    {
        try
        {
            key = Parse(value ?? string.Empty);
            return true;
        }
        catch (InvalidLicenceKeyException)
        {
            key = default;
            return false;
        }
    }

    /// <summary>
    /// Mints a key from 19 random Base32 characters and appends the checksum.
    /// </summary>
    /// <returns>A fresh, well-formed key.</returns>
    /// <remarks>
    /// The vendor's control plane is what issues keys in production (Stage 30b). This exists so the
    /// device side can be tested and demonstrated end to end without one, and so <c>scripts/seed.sh</c>
    /// can build a demonstrable tenant.
    /// </remarks>
    public static LicenceKey NewKey()
    {
        char[] body = new char[BodyLength];
        int sum = 0;

        for (int index = 0; index < BodyLength - 1; index++)
        {
            int digit = RandomNumberGenerator.GetInt32(Alphabet.Length);
            body[index] = Alphabet[digit];
            sum += digit * ((2 * index) + 1);
        }

        body[^1] = Alphabet[sum % Alphabet.Length];

        return Parse(new string(body));
    }

    /// <summary>
    /// The key's SHA-256 digest, lower-case hex.
    /// </summary>
    /// <remarks>
    /// What gets persisted. A licence key is a bearer credential for one installation — anybody who can
    /// read it can activate somewhere else — so the database holds a digest, exactly as it does for
    /// passwords, PINs and refresh tokens (<c>docs/SECURITY.md</c> §1).
    /// </remarks>
    public string Digest()
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(Value));

        return Convert.ToHexStringLower(hash);
    }

    private static string Normalise(string value)
    {
        StringBuilder builder = new(BodyLength);

        bool prefixSeen = false;
        string upper = value.Trim().ToUpperInvariant();

        if (upper.StartsWith(Prefix, StringComparison.Ordinal))
        {
            upper = upper[Prefix.Length..];
            prefixSeen = true;
        }

        foreach (char character in upper)
        {
            if (character is '-' or ' ' or '\t')
            {
                continue;
            }

            builder.Append(character switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                _ => character,
            });
        }

        // A key without its prefix is accepted — people paste the body — but a key with the *wrong*
        // prefix is not, because that is somebody's key for a different product and telling them so is
        // more useful than a checksum failure.
        if (!prefixSeen && builder.Length > BodyLength)
        {
            throw new InvalidLicenceKeyException($"A licence key starts with '{Prefix}-'.");
        }

        return builder.ToString();
    }
}

/// <summary>The licence key is not one — wrong shape, wrong alphabet, or a failed checksum.</summary>
/// <param name="message">What is wrong with it, in words the person typing it can act on.</param>
public sealed class InvalidLicenceKeyException(string message)
    : DomainException("LICENCE_KEY_INVALID", message, DomainProblemKind.Malformed);
