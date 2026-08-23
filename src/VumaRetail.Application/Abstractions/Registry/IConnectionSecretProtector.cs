namespace VumaRetail.Application.Abstractions.Registry;

/// <summary>
/// Encrypts and decrypts a company database's connection string for storage on
/// <c>RegistryCompany.ConnectionCiphertext</c> (ADR-118, R10).
/// </summary>
/// <remarks>
/// A connection string is a credential — decrypting it is not something a report, a support export or a
/// log line should ever be able to do by accident, which is why this is a narrow port rather than a
/// generic string encryptor: the only two callers that should exist are the provisioning handler that
/// writes it and the connection resolver that reads it back to open a connection.
/// </remarks>
public interface IConnectionSecretProtector
{
    /// <summary>Encrypts a connection string for storage.</summary>
    /// <param name="plainConnectionString">The connection string in the clear.</param>
    /// <returns>The AES-256-GCM ciphertext, base64.</returns>
    string Protect(string plainConnectionString);

    /// <summary>Decrypts a stored ciphertext back to a usable connection string.</summary>
    /// <param name="ciphertext">The value from <see cref="Protect"/>.</param>
    /// <returns>The connection string in the clear.</returns>
    string Unprotect(string ciphertext);
}
