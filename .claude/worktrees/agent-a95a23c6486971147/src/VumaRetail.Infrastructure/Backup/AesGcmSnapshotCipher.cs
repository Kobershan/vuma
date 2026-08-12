using System.Buffers.Binary;
using System.Security.Cryptography;
using VumaRetail.Application.Abstractions.Backup;

namespace VumaRetail.Infrastructure.Backup;

/// <summary>The snapshot encryption key.</summary>
public sealed class SnapshotEncryptionOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Vuma:Backup:Encryption";

    /// <summary>
    /// The 256-bit key, base64. Never committed; supplied as a secret.
    /// </summary>
    /// <remarks>
    /// Custody is the open question and it is deliberately left open here. The floor is that the key
    /// is configuration and is not the vendor's storage credential, so a compromise of the bucket is
    /// not a compromise of the data. Real custody — a KMS or an HSM — belongs with Stage 04b's
    /// licence signing key, which has the same problem and should get the same answer once.
    /// </remarks>
    public string Key { get; set; } = string.Empty;

    /// <summary>True while the key is unset, so the host can refuse to take a snapshot it cannot protect.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Key);
}

/// <summary>
/// AES-256-GCM over a snapshot, in framed chunks.
/// </summary>
/// <remarks>
/// <para>
/// Authenticated encryption, which is the requirement rather than a preference: a snapshot that had
/// been altered must fail to decrypt, not restore into plausible nonsense that a shop discovers at
/// the till. GCM's tag gives that; a bare cipher would not.
/// </para>
/// <para>
/// Chunked because a snapshot does not fit in memory. GCM is a one-shot construction — it will not
/// stream — so the file is framed: a header, then repeated
/// <c>[length][nonce][tag][ciphertext]</c> records of at most <see cref="ChunkSize"/> plaintext
/// bytes each.
/// </para>
/// <para>
/// The chunk index is authenticated as associated data. Without it, each chunk would be
/// independently valid and an attacker could reorder, drop or duplicate whole chunks while every
/// individual tag still verified — a restore of a database with a fortnight quietly missing from the
/// middle. The chunk count is checked at the end for the same reason, so truncation is detected too.
/// </para>
/// </remarks>
/// <param name="options">The key.</param>
public sealed class AesGcmSnapshotCipher(SnapshotEncryptionOptions options) : ISnapshotCipher
{
    /// <summary>Plaintext bytes per chunk. 1 MiB — well inside GCM's limits and cheap on memory.</summary>
    private const int ChunkSize = 1024 * 1024;

    /// <summary>Identifies the format, so a future change to it is detectable rather than a crash.</summary>
    private static readonly byte[] Magic = "VUMASNAP"u8.ToArray();

    /// <summary>Format version. Bumped if the framing ever changes.</summary>
    private const byte FormatVersion = 1;

    /// <inheritdoc />
    public async Task EncryptAsync(
        Stream plaintext,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(destination);

        byte[] key = ResolveKey();

        await destination.WriteAsync(Magic, cancellationToken).ConfigureAwait(false);
        await destination.WriteAsync(new[] { FormatVersion }, cancellationToken).ConfigureAwait(false);

        using AesGcm cipher = new(key, AesGcm.TagByteSizes.MaxSize);

        byte[] buffer = new byte[ChunkSize];
        byte[] nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
        byte[] tag = new byte[AesGcm.TagByteSizes.MaxSize];
        byte[] header = new byte[sizeof(int)];
        long chunkIndex = 0;

        while (true)
        {
            int read = await ReadFullyAsync(plaintext, buffer, cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            RandomNumberGenerator.Fill(nonce);

            byte[] ciphertext = new byte[read];
            cipher.Encrypt(nonce, buffer.AsSpan(0, read), ciphertext, tag, AssociatedData(chunkIndex));

            BinaryPrimitives.WriteInt32LittleEndian(header, read);

            await destination.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await destination.WriteAsync(nonce, cancellationToken).ConfigureAwait(false);
            await destination.WriteAsync(tag, cancellationToken).ConfigureAwait(false);
            await destination.WriteAsync(ciphertext, cancellationToken).ConfigureAwait(false);

            chunkIndex++;
        }

        // A zero-length frame terminates the file. It is authenticated like any other chunk, so a
        // truncated snapshot fails to decrypt rather than restoring whatever it managed to read.
        BinaryPrimitives.WriteInt32LittleEndian(header, 0);
        RandomNumberGenerator.Fill(nonce);
        cipher.Encrypt(nonce.AsSpan(), ReadOnlySpan<byte>.Empty, Span<byte>.Empty, tag, AssociatedData(chunkIndex));

        await destination.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await destination.WriteAsync(nonce, cancellationToken).ConfigureAwait(false);
        await destination.WriteAsync(tag, cancellationToken).ConfigureAwait(false);

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DecryptAsync(
        Stream ciphertext,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        ArgumentNullException.ThrowIfNull(destination);

        byte[] key = ResolveKey();

        byte[] preamble = new byte[Magic.Length + 1];

        if (await ReadFullyAsync(ciphertext, preamble, cancellationToken).ConfigureAwait(false) != preamble.Length
            || !preamble.AsSpan(0, Magic.Length).SequenceEqual(Magic)
            || preamble[^1] != FormatVersion)
        {
            throw new SnapshotDecryptionException();
        }

        using AesGcm cipher = new(key, AesGcm.TagByteSizes.MaxSize);

        byte[] header = new byte[sizeof(int)];
        byte[] nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
        byte[] tag = new byte[AesGcm.TagByteSizes.MaxSize];
        long chunkIndex = 0;

        while (true)
        {
            if (await ReadFullyAsync(ciphertext, header, cancellationToken).ConfigureAwait(false) != header.Length
                || await ReadFullyAsync(ciphertext, nonce, cancellationToken).ConfigureAwait(false) != nonce.Length
                || await ReadFullyAsync(ciphertext, tag, cancellationToken).ConfigureAwait(false) != tag.Length)
            {
                // The frame headers ran out before the terminator did. Truncated.
                throw new SnapshotDecryptionException();
            }

            int length = BinaryPrimitives.ReadInt32LittleEndian(header);

            if (length is < 0 or > ChunkSize)
            {
                throw new SnapshotDecryptionException();
            }

            byte[] chunk = new byte[length];

            if (length > 0
                && await ReadFullyAsync(ciphertext, chunk, cancellationToken).ConfigureAwait(false) != length)
            {
                throw new SnapshotDecryptionException();
            }

            byte[] plaintext = new byte[length];

            try
            {
                cipher.Decrypt(nonce, chunk, tag, plaintext, AssociatedData(chunkIndex));
            }
            catch (CryptographicException failure)
            {
                // Wrong key, altered ciphertext, or a chunk that has been moved — the associated
                // data is the chunk index, so a reordered chunk fails here even though its own tag
                // is intact.
                throw new SnapshotDecryptionException(failure);
            }

            if (length == 0)
            {
                break;
            }

            await destination.WriteAsync(plaintext, cancellationToken).ConfigureAwait(false);
            chunkIndex++;
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The chunk index, authenticated but not encrypted, so chunks cannot be shuffled.</summary>
    private static byte[] AssociatedData(long chunkIndex)
    {
        byte[] associated = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(associated, chunkIndex);

        return associated;
    }

    private byte[] ResolveKey()
    {
        if (!options.IsConfigured)
        {
            throw new InvalidOperationException(
                $"{SnapshotEncryptionOptions.SectionName}:Key is not configured. A snapshot is every "
                + "row a tenant has; it is not written unencrypted.");
        }

        byte[] key;

        try
        {
            key = Convert.FromBase64String(options.Key);
        }
        catch (FormatException malformed)
        {
            throw new InvalidOperationException(
                $"{SnapshotEncryptionOptions.SectionName}:Key is not valid base64.",
                malformed);
        }

        return key.Length == 32
            ? key
            : throw new InvalidOperationException(
                $"{SnapshotEncryptionOptions.SectionName}:Key must be 256 bits (32 bytes) of base64, "
                + $"not {key.Length} bytes.");
    }

    /// <summary>
    /// Reads until the buffer is full or the stream ends.
    /// </summary>
    /// <remarks>
    /// A plain <c>ReadAsync</c> may return fewer bytes than asked for at any time and routinely does
    /// over a network stream — which for a framed format means reading a nonce that is half a nonce
    /// and failing to decrypt a snapshot that is perfectly good.
    /// </remarks>
    private static async Task<int> ReadFullyAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        int total = 0;

        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer[total..], cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
