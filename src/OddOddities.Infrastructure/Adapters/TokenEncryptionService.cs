using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OddOddities.Domain.Interfaces;
using OddOddities.Domain.ValueObjects;

namespace OddOddities.Infrastructure.Adapters;

/// <summary>
/// AES-256-GCM implementation of ITokenEncryptionPort (ADR-006).
/// Encrypts and decrypts Meta tokens for secure storage in PostgreSQL.
/// The encryption key is loaded from environment variable TOKEN_ENCRYPTION_KEY.
/// </summary>
public sealed class TokenEncryptionService : ITokenEncryptionPort
{
    private readonly byte[] _key;
    private readonly ILogger<TokenEncryptionService> _logger;

    public TokenEncryptionService(
        IOptions<TokenEncryptionConfiguration> options,
        ILogger<TokenEncryptionService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var config = options?.Value ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(config.Key))
        {
            throw new ArgumentException(
                "Token encryption key is not configured. Set TOKEN_ENCRYPTION_KEY environment variable.",
                nameof(config));
        }

        _key = Convert.FromBase64String(config.Key);

        if (_key.Length != 32) // 256 bits
        {
            throw new ArgumentException(
                $"Token encryption key must be 32 bytes (256 bits). Got {_key.Length} bytes.",
                nameof(config));
        }
    }

    /// <inheritdoc />
    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            throw new ArgumentException("Plain text cannot be null or empty.", nameof(plainText));

        var plaintext = Encoding.UTF8.GetBytes(plainText);

        // AES-256-GCM nonce and tag sizes
        byte[] nonce = new byte[AesGcm.NonceByteSizes.MaxSize]; // 12 bytes
        byte[] tag = new byte[AesGcm.TagByteSizes.MaxSize];     // 16 bytes

        // Generate cryptographically secure random nonce
        RandomNumberGenerator.Fill(nonce);

        byte[] ciphertext = new byte[plaintext.Length];

        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Combine nonce + tag + ciphertext for storage
        var combined = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, combined, nonce.Length + tag.Length, ciphertext.Length);

        _logger.LogDebug(
            "Token encrypted: plaintextLength={PlaintextLength}, ciphertextLength={CiphertextLength}",
            plainText.Length,
            ciphertext.Length);

        return Convert.ToBase64String(combined);
    }

    /// <inheritdoc />
    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            throw new ArgumentException("Cipher text cannot be null or empty.", nameof(cipherText));

        var combined = Convert.FromBase64String(cipherText);

        // Extract nonce, tag, and ciphertext
        var nonceSize = AesGcm.NonceByteSizes.MaxSize;
        var tagSize = AesGcm.TagByteSizes.MaxSize;

        if (combined.Length < nonceSize + tagSize)
        {
            throw new CryptographicException("Invalid cipher text: too short to contain nonce and tag.");
        }

        var nonce = combined.AsSpan(0, nonceSize).ToArray();
        var tag = combined.AsSpan(nonceSize, tagSize).ToArray();
        var ciphertext = combined.AsSpan(nonceSize + tagSize).ToArray();

        byte[] plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_key, tag.Length);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        var result = Encoding.UTF8.GetString(plaintext);

        _logger.LogDebug(
            "Token decrypted: ciphertextLength={CiphertextLength}, plaintextLength={PlaintextLength}",
            cipherText.Length,
            result.Length);

        return result;
    }
}
