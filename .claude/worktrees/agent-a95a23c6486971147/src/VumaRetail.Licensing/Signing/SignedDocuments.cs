using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using VumaRetail.Application.Abstractions.Licensing;
using VumaRetail.Domain.Licensing;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Licensing.Signing;

/// <summary>What a signed document is, so one kind cannot be presented as another.</summary>
/// <remarks>
/// A lease and a licence carry overlapping fields, and an emergency code carries a tenant and an
/// expiry like both of them. Without a discriminator inside the signed material, a valid document of
/// one kind is a valid document of every kind whose fields it happens to satisfy — which is how a
/// 72-hour emergency code becomes a 30-day licence.
/// </remarks>
public enum SignedDocumentKind
{
    /// <summary>A monthly licence.</summary>
    Licence = 0,

    /// <summary>A 72-hour lease derived from a licence.</summary>
    Lease = 1,

    /// <summary>An emergency write code, redeemable with no connectivity.</summary>
    EmergencyCode = 2,
}

/// <summary>The signed body of a monthly licence (<c>LICENSING.md</c> §2).</summary>
/// <param name="Kind">Always <see cref="SignedDocumentKind.Licence"/>.</param>
/// <param name="TenantId">The tenant.</param>
/// <param name="StoreId">The store, where the licence is store-scoped.</param>
/// <param name="ActivationReference">The activation it is bound to.</param>
/// <param name="PlanCode">The vendor's plan code.</param>
/// <param name="Entitlements">The module flags it enables.</param>
/// <param name="Limits">The plan's quantities.</param>
/// <param name="IssuedAt">When it was issued, UTC.</param>
/// <param name="ExpiresAt">When it stops being current, UTC.</param>
/// <param name="FingerprintDigest">The hardware it is bound to.</param>
/// <param name="Nonce">The issuance nonce.</param>
/// <param name="IssuanceCounter">The monotonic issuance counter.</param>
public sealed record LicenceDocument(
    SignedDocumentKind Kind,
    Guid TenantId,
    Guid? StoreId,
    Guid ActivationReference,
    string PlanCode,
    IReadOnlyList<string> Entitlements,
    LicenceLimits Limits,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string FingerprintDigest,
    string Nonce,
    long IssuanceCounter);

/// <summary>The signed body of a lease (<c>LICENSING.md</c> §2).</summary>
/// <param name="Kind">Always <see cref="SignedDocumentKind.Lease"/>.</param>
/// <param name="LeaseId">The control plane's id for this lease.</param>
/// <param name="TenantId">The tenant.</param>
/// <param name="StoreId">The store, where the lease is store-scoped.</param>
/// <param name="ActivationReference">The activation it was issued to.</param>
/// <param name="Entitlements">The module flags in force.</param>
/// <param name="Limits">The quantities in force.</param>
/// <param name="EnforcementLevel">Where the control plane says the tenant sits.</param>
/// <param name="Reason">Why.</param>
/// <param name="IssuedAt">When it was issued, UTC.</param>
/// <param name="ExpiresAt">When it expires, UTC.</param>
/// <param name="IssuanceCounter">The licence counter it derives from.</param>
/// <param name="DunningCompletedAt">When dunning completed, if it has. Path B turns on this.</param>
/// <param name="WriteUnlockUntil">Until when a vendor write unlock is in force.</param>
public sealed record LeaseDocument(
    SignedDocumentKind Kind,
    Guid LeaseId,
    Guid TenantId,
    Guid? StoreId,
    Guid ActivationReference,
    IReadOnlyList<string> Entitlements,
    LicenceLimits Limits,
    EnforcementLevel EnforcementLevel,
    EnforcementReason Reason,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    long IssuanceCounter,
    DateTimeOffset? DunningCompletedAt = null,
    DateTimeOffset? WriteUnlockUntil = null);

/// <summary>The signed body of an emergency write code (<c>LICENSING.md</c> §5).</summary>
/// <param name="Kind">Always <see cref="SignedDocumentKind.EmergencyCode"/>.</param>
/// <param name="CodeId">The vendor's id for the code. Single-use, enforced by a unique index.</param>
/// <param name="TenantId">The one tenant it works for.</param>
/// <param name="IssuedAt">When it was issued, UTC.</param>
/// <param name="ExpiresAt">When it stops working, UTC. Enforced with no network involved.</param>
/// <param name="Reason">The vendor's reason, recorded and reported.</param>
public sealed record EmergencyCodeDocument(
    SignedDocumentKind Kind,
    Guid CodeId,
    Guid TenantId,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string Reason);

/// <summary>
/// Verifies signed licensing documents against the pinned public key (ADR-026).
/// </summary>
/// <remarks>
/// Verification is the only operation a store server ever performs. The private key lives in the
/// vendor's KMS and never leaves it; <see cref="LicenceSigner"/> exists for the control plane, for the
/// in-process fake, and for the tests — never on a customer's machine in production.
/// </remarks>
public interface ILicenceVerifier
{
    /// <summary>
    /// Verifies a document and returns its body.
    /// </summary>
    /// <typeparam name="TDocument">The expected body type.</typeparam>
    /// <param name="document">The compact <c>payload.signature</c> form.</param>
    /// <param name="kind">The kind the caller is expecting.</param>
    /// <returns>The verified body.</returns>
    /// <exception cref="LicenceSignatureException">
    /// The document is malformed, the signature does not verify against the pinned key, or the body is
    /// not of the expected kind.
    /// </exception>
    TDocument Verify<TDocument>(string document, SignedDocumentKind kind);

    /// <summary>
    /// True when this verifier is holding the development key pair that ships in the binaries.
    /// </summary>
    /// <remarks>
    /// The host refuses to start on it outside Development, exactly as it does for the placeholder JWT
    /// signing key. A shipped key that verifies licences anybody can mint is not a licence check.
    /// </remarks>
    bool UsesDevelopmentKey { get; }
}

/// <summary>The compact encoding both halves share: <c>base64url(body).base64url(signature)</c>.</summary>
/// <remarks>
/// Deliberately not a JWT. A JWT would bring a header the verifier has to police — an <c>alg</c> field
/// an attacker can set to <c>none</c> is the single most repeated signature bug in the industry —
/// and Vuma needs exactly one algorithm with exactly one key. There is nothing to negotiate, so there
/// is no negotiation.
/// </remarks>
public static class SignedDocument
{
    /// <summary>The JSON settings both signer and verifier use.</summary>
    internal static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };

    /// <summary>Splits the compact form, or throws if it is not one.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The body bytes and the signature bytes.</returns>
    /// <exception cref="LicenceSignatureException">It is not the compact form.</exception>
    internal static (byte[] Body, byte[] Signature) Split(string document)
    {
        ArgumentNullException.ThrowIfNull(document);

        int separator = document.IndexOf('.', StringComparison.Ordinal);

        if (separator <= 0 || separator == document.Length - 1)
        {
            throw new LicenceSignatureException("That is not a signed Vuma document.");
        }

        try
        {
            return (
                Base64Url.DecodeFromChars(document.AsSpan(0, separator)),
                Base64Url.DecodeFromChars(document.AsSpan(separator + 1)));
        }
        catch (FormatException failure)
        {
            throw new LicenceSignatureException($"That signed document is not readable: {failure.Message}");
        }
    }

    /// <summary>Joins a body and a signature into the compact form.</summary>
    /// <param name="body">The serialised body.</param>
    /// <param name="signature">The signature over it.</param>
    internal static string Join(byte[] body, byte[] signature)
        => $"{Base64Url.EncodeToString(body)}.{Base64Url.EncodeToString(signature)}";
}

/// <summary>Verifies documents against one Ed25519 public key.</summary>
/// <remarks>
/// The key is supplied by <c>LicensingOptions</c> and pinned into the binaries in a real deployment. A
/// modified public key fails the signature check on the <em>next</em> real licence, which is what
/// ADR-026 means by raising the cost of a crack rather than pretending to prevent one.
/// </remarks>
public sealed class Ed25519LicenceVerifier : ILicenceVerifier
{
    private readonly Ed25519PublicKeyParameters _publicKey;

    /// <summary>Builds a verifier over a 32-byte Ed25519 public key.</summary>
    /// <param name="publicKey">The key, base64.</param>
    /// <param name="isDevelopmentKey">Whether this is the key that ships in the binaries.</param>
    public Ed25519LicenceVerifier(string publicKey, bool isDevelopmentKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKey);

        _publicKey = new Ed25519PublicKeyParameters(Convert.FromBase64String(publicKey));
        UsesDevelopmentKey = isDevelopmentKey;
    }

    /// <inheritdoc />
    public bool UsesDevelopmentKey { get; }

    /// <inheritdoc />
    public TDocument Verify<TDocument>(string document, SignedDocumentKind kind)
    {
        (byte[] body, byte[] signature) = SignedDocument.Split(document);

        Ed25519Signer verifier = new();
        verifier.Init(forSigning: false, _publicKey);
        verifier.BlockUpdate(body, 0, body.Length);

        if (!verifier.VerifySignature(signature))
        {
            throw new LicenceSignatureException(
                "That licence was not signed by Vuma. If it was typed or pasted, check it; if it "
                + "came from us, contact support.");
        }

        TDocument? parsed;

        try
        {
            parsed = JsonSerializer.Deserialize<TDocument>(body, SignedDocument.Json);
        }
        catch (JsonException failure)
        {
            throw new LicenceSignatureException($"That signed document is not readable: {failure.Message}");
        }

        if (parsed is null)
        {
            throw new LicenceSignatureException("That signed document is empty.");
        }

        // The signature proves the vendor wrote it. This proves the vendor wrote it *for this
        // purpose* — without it, an emergency code and a licence are the same bytes to the caller
        // that asked for the wrong one.
        SignedDocumentKind actual = ReadKind(body);

        if (actual != kind)
        {
            throw new LicenceSignatureException(
                $"That is a {actual} document and a {kind} was expected.");
        }

        return parsed;
    }

    private static SignedDocumentKind ReadKind(byte[] body)
    {
        using JsonDocument json = JsonDocument.Parse(body);

        return json.RootElement.TryGetProperty("Kind", out JsonElement kind)
            && Enum.TryParse(kind.GetString(), out SignedDocumentKind parsed)
            && Enum.IsDefined(parsed)
                ? parsed
                : throw new LicenceSignatureException("That signed document does not say what it is.");
    }
}

/// <summary>
/// Signs licensing documents. The vendor's side of the pair.
/// </summary>
/// <remarks>
/// <para>
/// In production the private key lives in a KMS/HSM and the control plane asks it for signatures
/// (ADR-024) — this class is what Stage 30b will put behind that call, what
/// <c>InProcessControlPlane</c> uses to be a real control plane in tests, and what
/// <c>scripts/seed.sh</c> uses to produce a demonstrable tenant. It has no place on a store server.
/// </para>
/// <para>
/// <b>The development key pair is derived from a fixed seed compiled into this assembly.</b> That is
/// not a mistake and it is not a secret: it exists so the whole licensing path is exercisable on a
/// developer's machine and in CI, and the host refuses to start on it outside Development for exactly
/// the reason that makes it useful.
/// </para>
/// </remarks>
public sealed class LicenceSigner
{
    private readonly Ed25519PrivateKeyParameters _privateKey;

    /// <summary>
    /// The seed the development key pair is derived from. Public, deliberately, and useless in
    /// production because <c>LicensingOptions.RequireProductionKey</c> refuses to start on it.
    /// </summary>
    private const string DevelopmentSeed = "vuma-retail-development-licence-key-do-not-ship-0";

    /// <summary>Builds a signer over a 32-byte Ed25519 private key.</summary>
    /// <param name="privateKey">The key, base64.</param>
    public LicenceSigner(string privateKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKey);

        _privateKey = new Ed25519PrivateKeyParameters(Convert.FromBase64String(privateKey));
    }

    private LicenceSigner(Ed25519PrivateKeyParameters privateKey) => _privateKey = privateKey;

    /// <summary>The development signer, derived from the fixed seed.</summary>
    public static LicenceSigner Development { get; } = new(
        new Ed25519PrivateKeyParameters(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(DevelopmentSeed))));

    /// <summary>The public key that matches this signer, base64.</summary>
    public string PublicKey => Convert.ToBase64String(_privateKey.GeneratePublicKey().GetEncoded());

    /// <summary>The development public key, which is what <c>LicensingOptions</c> defaults to.</summary>
    public static string DevelopmentPublicKey => Development.PublicKey;

    /// <summary>Signs a document body.</summary>
    /// <typeparam name="TDocument">The body type.</typeparam>
    /// <param name="document">The body.</param>
    /// <returns>The compact <c>payload.signature</c> form.</returns>
    public string Sign<TDocument>(TDocument document)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(document, SignedDocument.Json);

        Ed25519Signer signer = new();
        signer.Init(forSigning: true, _privateKey);
        signer.BlockUpdate(body, 0, body.Length);

        return SignedDocument.Join(body, signer.GenerateSignature());
    }

    /// <summary>A fresh key pair, for a vendor provisioning its own control plane.</summary>
    /// <returns>The signer and its public key.</returns>
    public static (LicenceSigner Signer, string PublicKey) Generate()
    {
        Ed25519PrivateKeyParameters privateKey =
            new(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        LicenceSigner signer = new(privateKey);

        return (signer, signer.PublicKey);
    }
}

/// <summary>A signed licensing document did not verify.</summary>
/// <remarks>
/// Raised for a bad signature, a malformed document and a document of the wrong kind alike. The three
/// are not distinguished in the message on purpose: a caller who can tell them apart can probe the
/// verifier, and nobody at the keyboard can act differently on the difference.
/// </remarks>
/// <param name="message">What to tell the person at the keyboard.</param>
public sealed class LicenceSignatureException(string message)
    : DomainException("LICENCE_SIGNATURE_INVALID", message);
