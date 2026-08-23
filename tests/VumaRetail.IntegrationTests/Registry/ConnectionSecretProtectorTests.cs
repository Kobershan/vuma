using VumaRetail.Infrastructure.Security;

namespace VumaRetail.IntegrationTests.Registry;

/// <summary>
/// No database involved — lives alongside <c>BackupTests</c>' cipher coverage rather than in
/// <c>VumaRetail.UnitTests</c> because <c>VumaRetail.UnitTests</c> does not reference
/// <c>VumaRetail.Infrastructure</c>, the same reason <c>AesGcmSnapshotCipher</c>'s tests live here.
/// </summary>
public sealed class ConnectionSecretProtectorTests
{
    private const string Key = "rm5/8twNVNEely7ojqq3kDXKPwS7b62MWgPUwJPNPLw=";

    private static AesGcmConnectionSecretProtector NewProtector(string key = Key)
        => new(new ConnectionSecretOptions { Key = key });

    [Fact]
    public void A_protected_connection_string_decrypts_back_to_the_original()
    {
        AesGcmConnectionSecretProtector protector = NewProtector();
        const string connectionString = "Host=10.0.4.12;Port=5432;Database=vuma_siyaya_hardware;Username=vuma;Password=s3cr3t";

        string ciphertext = protector.Protect(connectionString);
        string roundTripped = protector.Unprotect(ciphertext);

        roundTripped.Should().Be(connectionString);
        ciphertext.Should().NotContain("s3cr3t", "the connection secret must never appear in the stored form (R10)");
    }

    [Fact]
    public void The_same_connection_string_produces_a_different_ciphertext_each_time()
    {
        // A fresh random nonce per call — otherwise two companies provisioned back to back with
        // similar connection strings would leak a pattern.
        AesGcmConnectionSecretProtector protector = NewProtector();
        const string connectionString = "Host=10.0.4.12;Port=5432;Database=vuma_siyaya_hardware;Username=vuma;Password=s3cr3t";

        string first = protector.Protect(connectionString);
        string second = protector.Protect(connectionString);

        first.Should().NotBe(second);
    }

    [Fact]
    public void Decrypting_with_the_wrong_key_fails_rather_than_returning_garbage()
    {
        AesGcmConnectionSecretProtector writer = NewProtector(Key);
        AesGcmConnectionSecretProtector reader = NewProtector("fTBcugR2ENqvuF68zI5UF9lr65OcmdgYf0fq5lnT9sk=");

        string ciphertext = writer.Protect("Host=10.0.4.12;Password=s3cr3t");

        Action decrypt = () => reader.Unprotect(ciphertext);

        // AES-GCM's authentication tag makes a wrong key a decryption failure, not plausible nonsense —
        // the same property AesGcmSnapshotCipher relies on for a tampered backup.
        decrypt.Should().Throw<System.Security.Cryptography.CryptographicException>();
    }

    [Fact]
    public void An_unconfigured_key_refuses_to_protect_a_secret_rather_than_writing_it_unencrypted()
    {
        AesGcmConnectionSecretProtector protector = new(new ConnectionSecretOptions());

        Action protect = () => protector.Protect("Host=10.0.4.12;Password=s3cr3t");

        protect.Should().Throw<InvalidOperationException>();
    }
}
