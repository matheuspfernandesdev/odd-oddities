namespace OddOddities.Domain.Interfaces;

/// <summary>
/// Port for token encryption and decryption operations using AES-256-GCM (ADR-006).
/// The encryption key is stored in an environment variable, never in the database.
/// </summary>
public interface ITokenEncryptionPort
{
    /// <summary>
    /// Encrypts a plaintext string using AES-256-GCM.
    /// </summary>
    /// <param name="plainText">The plaintext to encrypt.</param>
    /// <returns>Base64-encoded ciphertext containing nonce + tag + ciphertext.</returns>
    string Encrypt(string plainText);

    /// <summary>
    /// Decrypts a Base64-encoded ciphertext string using AES-256-GCM.
    /// </summary>
    /// <param name="cipherText">The Base64-encoded ciphertext to decrypt.</param>
    /// <returns>The decrypted plaintext.</returns>
    string Decrypt(string cipherText);
}
