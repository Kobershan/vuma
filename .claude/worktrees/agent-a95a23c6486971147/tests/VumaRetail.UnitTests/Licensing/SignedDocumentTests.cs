using System.Buffers.Text;
using System.Text;
using FluentAssertions;
using VumaRetail.Domain.Licensing;
using VumaRetail.Licensing.Signing;

namespace VumaRetail.UnitTests.Licensing;

/// <summary>
/// The signature that makes a licence a licence (ADR-026, <c>LICENSING.md</c> §2).
/// </summary>
public sealed class SignedDocumentTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly LicenceSigner Signer = LicenceSigner.Development;

    private static readonly Ed25519LicenceVerifier Verifier =
        new(LicenceSigner.DevelopmentPublicKey, isDevelopmentKey: true);

    [Fact]
    public void A_licence_signed_by_the_right_key_verifies_and_round_trips()
    {
        LicenceDocument issued = Licence();

        LicenceDocument read = Verifier.Verify<LicenceDocument>(
            Signer.Sign(issued),
            SignedDocumentKind.Licence);

        read.Should().BeEquivalentTo(issued);
    }

    [Fact]
    public void A_licence_signed_by_the_wrong_key_is_rejected()
    {
        (LicenceSigner impostor, _) = LicenceSigner.Generate();

        Action verify = () => Verifier.Verify<LicenceDocument>(
            impostor.Sign(Licence()),
            SignedDocumentKind.Licence);

        verify.Should().Throw<LicenceSignatureException>();
    }

    [Fact]
    public void A_tampered_payload_is_rejected()
    {
        // The attack this is actually for: take a real licence, extend its expiry, keep the
        // signature. Ed25519 over the exact body bytes is what makes that a rejected document rather
        // than a free month.
        string document = Signer.Sign(Licence());

        int separator = document.IndexOf('.', StringComparison.Ordinal);
        byte[] body = Base64Url.DecodeFromChars(document.AsSpan(0, separator));

        string edited = Encoding.UTF8.GetString(body).Replace("2026-07-01", "2027-07-01", StringComparison.Ordinal);

        string forged = $"{Base64Url.EncodeToString(Encoding.UTF8.GetBytes(edited))}{document[separator..]}";

        Action verify = () => Verifier.Verify<LicenceDocument>(forged, SignedDocumentKind.Licence);

        verify.Should().Throw<LicenceSignatureException>();
    }

    [Fact]
    public void A_document_of_one_kind_cannot_be_presented_as_another()
    {
        // Without the discriminator inside the signed material, a 72-hour emergency code is a valid
        // licence to anybody who asked for a licence — the fields overlap enough to deserialise.
        string code = Signer.Sign(new EmergencyCodeDocument(
            SignedDocumentKind.EmergencyCode,
            Guid.NewGuid(),
            Tenant,
            Now,
            Now.AddHours(72),
            "support"));

        Action verify = () => Verifier.Verify<LicenceDocument>(code, SignedDocumentKind.Licence);

        verify.Should().Throw<LicenceSignatureException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-document")]
    [InlineData(".")]
    [InlineData("abc.")]
    [InlineData("!!!.!!!")]
    public void Anything_that_is_not_the_compact_form_is_rejected(string candidate)
    {
        Action verify = () => Verifier.Verify<LeaseDocument>(candidate, SignedDocumentKind.Lease);

        verify.Should().Throw<LicenceSignatureException>();
    }

    [Fact]
    public void The_development_key_pair_is_stable_across_processes()
    {
        // It has to be: a lease signed by yesterday's build must verify against today's, or every
        // developer's database and every CI run starts from an unverifiable licence.
        LicenceSigner.DevelopmentPublicKey.Should().Be(LicenceSigner.Development.PublicKey);
        LicenceSigner.DevelopmentPublicKey.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_generated_key_pair_verifies_its_own_documents_and_nobody_elses()
    {
        (LicenceSigner signer, string publicKey) = LicenceSigner.Generate();

        Ed25519LicenceVerifier verifier = new(publicKey, isDevelopmentKey: false);

        verifier.Verify<LicenceDocument>(signer.Sign(Licence()), SignedDocumentKind.Licence)
            .Should().NotBeNull();

        Action foreign = () => verifier.Verify<LicenceDocument>(
            Signer.Sign(Licence()),
            SignedDocumentKind.Licence);

        foreign.Should().Throw<LicenceSignatureException>();
        verifier.UsesDevelopmentKey.Should().BeFalse();
    }

    private static LicenceDocument Licence()
        => new(
            SignedDocumentKind.Licence,
            Tenant,
            null,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "standard",
            ["pos", "inventory"],
            LicenceLimits.Unlimited,
            Now,
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            "fingerprint",
            "nonce",
            7);
}
