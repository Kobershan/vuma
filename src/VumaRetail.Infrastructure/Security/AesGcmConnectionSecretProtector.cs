using System.Security.Cryptography;
using VumaRetail.Application.Abstractions.Registry;

namespace VumaRetail.Infrastructure.Security;

/// <summary>The registry's connection-secret encryption key.</summary>
public sealed class ConnectionSecretOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Vuma:Registry:ConnectionEncryption";

    /// <summary>
    /// The 256-bit key, base64. Never committed; supplied as a secret, separate from
    /// <c>SnapshotEncryptionOptions</c>'s key — a compromise of one must not be a compromise of the
    /// other.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>True while the key is unset, so provisioning can refuse to write a secret it cannot protect.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Key);
}

/// <summary>
/// AES-256-GCM over a single short string — a company database's connection string — never a stream.
/// </summary>
/// <remarks>
/// Deliberately not <c>AesGcmSnapshotCipher</c> reused: that class frames and chunks a stream of
/// arbitrary length for a backup, which is the wrong shape for one line of configuration, and sharing an
/// implementation between "protects a whole tenant's data" and "protects one credential" would make a
/// change to one silently change the blast radius of the other.
/// </remarks>
/// <param name="options">The key.</param>
public sealed class AesGcmConnectionSecretProtector(ConnectionSecretOptions options) : IConnectionSecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <inheritdoc />
    public string Protect(string plainConnectionString)
    {
        ArgumentException.ThrowIfNullOrEmpty(plainConnectionString);

        byte[] key = ResolveKey();
        byte[] plaintext = System.Text.Encoding.UTF8.GetBytes(plainConnectionString);

        byte[] nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        using (AesGcm cipher = new(key, TagSize))
        {
            cipher.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        // nonce || tag || ciphertext, base64 — self-contained, so decryption needs nothing but the key.
        byte[] framed = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, framed, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, framed, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, framed, nonce.Length + tag.Length, ciphertext.Length);

        return Convert.ToBase64String(framed);
    }

    /// <inheritdoc />
    public string Unprotect(string ciphertext)
    {
        ArgumentException.ThrowIfNullOrEmpty(ciphertext);

        byte[] key = ResolveKey();
        byte[] framed;

        try
        {
            framed = Convert.FromBase64String(ciphertext);
        }
        catch (FormatException malformed)
        {
            throw new InvalidOperationException("Connection ciphertext is not valid base64.", malformed);
        }

        if (framed.Length < NonceSize + TagSize)
        {
            throw new InvalidOperationException("Connection ciphertext is too short to be genuine.");
        }

        ReadOnlySpan<byte> span = framed;
        ReadOnlySpan<byte> nonce = span[..NonceSize];
        ReadOnlySpan<byte> tag = span.Slice(NonceSize, TagSize);
        ReadOnlySpan<byte> body = span[(NonceSize + TagSize)..];

        byte[] plaintext = new byte[body.Length];

        using (AesGcm cipher = new(key, TagSize))
        {
            cipher.Decrypt(nonce, body, tag, plaintext);
        }

        return System.Text.Encoding.UTF8.GetString(plaintext);
    }

    private byte[] ResolveKey()
    {
        if (!options.IsConfigured)
        {
            throw new InvalidOperationException(
                $"{ConnectionSecretOptions.SectionName}:Key is not configured. A company's connection "
                + "string is a credential; it is not written unencrypted (R10).");
        }

        byte[] key;

        try
        {
            key = Convert.FromBase64String(options.Key);
        }
        catch (FormatException malformed)
        {
            throw new InvalidOperationException(
                $"{ConnectionSecretOptions.SectionName}:Key is not valid base64.",
                malformed);
        }

        return key.Length == 32
            ? key
            : throw new InvalidOperationException(
                $"{ConnectionSecretOptions.SectionName}:Key must be 256 bits (32 bytes) of base64, "
                + $"not {key.Length} bytes.");
    }
}
