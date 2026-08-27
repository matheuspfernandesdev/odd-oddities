using Microsoft.Extensions.Logging;
using OddOddities.Application.DTOs;
using OddOddities.Domain.Interfaces;

namespace OddOddities.Application.Services;

/// <summary>
/// Application service for automatic Meta token renewal (RF-03, BR-010).
/// Checks token expiry and refreshes via Meta Graph API when less than 14 days remain.
/// The new token is encrypted with AES-256-GCM and persisted in SystemSettings.
/// </summary>
public sealed class TokenRenewalService : ITokenRenewalPort
{
    private readonly ISystemSettingRepository _repository;
    private readonly ITokenEncryptionPort _encryptionService;
    private readonly IInstagramPublishingPort _instagramPort;
    private readonly ILogger<TokenRenewalService> _logger;

    /// <summary>
    /// Threshold in days before expiry to trigger renewal (BR-010).
    /// </summary>
    private const int RenewalThresholdDays = 14;

    /// <summary>
    /// SystemSetting key for the encrypted Meta access token.
    /// </summary>
    private const string TokenKey = "META_ACCESS_TOKEN";

    /// <summary>
    /// SystemSetting key for the token expiry date.
    /// </summary>
    private const string ExpiresAtKey = "META_TOKEN_EXPIRES_AT";

    public TokenRenewalService(
        ISystemSettingRepository repository,
        ITokenEncryptionPort encryptionService,
        IInstagramPublishingPort instagramPort,
        ILogger<TokenRenewalService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
        _instagramPort = instagramPort ?? throw new ArgumentNullException(nameof(instagramPort));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> RenewTokenIfNeededAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Checking Meta token expiry status");

        // 1. Fetch current token and expiry from SystemSettings
        var tokenSetting = await _repository.GetByIdAsync(TokenKey, cancellationToken);
        var expiresAtSetting = await _repository.GetByIdAsync(ExpiresAtKey, cancellationToken);

        if (tokenSetting == null || expiresAtSetting == null)
        {
            _logger.LogWarning(
                "Meta token or expiry not found in SystemSettings (token={TokenFound}, expiresAt={ExpiresAtFound})",
                tokenSetting != null,
                expiresAtSetting != null);
            return false;
        }

        // 2. Parse expiry date and check threshold
        if (!DateTime.TryParse(expiresAtSetting.Value, out var expiresAt))
        {
            _logger.LogError(
                "Invalid token expiry date format in SystemSettings: {Value}",
                expiresAtSetting.Value);
            return false;
        }

        var daysUntilExpiry = (expiresAt - DateTime.UtcNow).TotalDays;

        if (daysUntilExpiry > RenewalThresholdDays)
        {
            _logger.LogInformation(
                "Meta token still valid for {DaysUntilExpiry:F1} days (threshold: {Threshold} days)",
                daysUntilExpiry,
                RenewalThresholdDays);
            return false;
        }

        _logger.LogInformation(
            "Meta token expires in {DaysUntilExpiry:F1} days, initiating renewal",
            daysUntilExpiry);

        // 3. Decrypt current token and call refresh endpoint
        var decryptedToken = _encryptionService.Decrypt(tokenSetting.Value);

        var newToken = await _instagramPort.RefreshAccessTokenAsync(decryptedToken, cancellationToken);

        if (string.IsNullOrEmpty(newToken.NewToken))
        {
            _logger.LogError("Failed to refresh Meta token: empty token returned");
            return false;
        }

        // 4. Encrypt and persist new token
        var encryptedNewToken = _encryptionService.Encrypt(newToken.NewToken);

        await _repository.UpsertAsync(
            new Domain.Entities.SystemSetting
            {
                Key = TokenKey,
                Value = encryptedNewToken,
                IsEncrypted = true,
                Description = "Meta Graph API access token (AES-256-GCM encrypted)",
                UpdatedAt = DateTime.UtcNow
            },
            cancellationToken);

        await _repository.UpsertAsync(
            new Domain.Entities.SystemSetting
            {
                Key = ExpiresAtKey,
                Value = newToken.ExpiresAt.ToString("O"),
                IsEncrypted = false,
                Description = "Meta token expiry date (ISO 8601 UTC)",
                UpdatedAt = DateTime.UtcNow
            },
            cancellationToken);

        _logger.LogInformation(
            "Meta token renewed successfully, expires at {ExpiresAt:O}",
            newToken.ExpiresAt);

        return true;
    }
}
