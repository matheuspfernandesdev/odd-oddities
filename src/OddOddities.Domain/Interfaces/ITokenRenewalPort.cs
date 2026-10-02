namespace OddOddities.Domain.Interfaces;

/// <summary>
/// Port for automatic Meta token renewal (RF-03, BR-010).
/// Checks token expiry and refreshes via Meta Graph API when needed.
/// </summary>
public interface ITokenRenewalPort
{
    /// <summary>
    /// Checks if the Meta token needs renewal (less than 14 days to expiry)
    /// and refreshes it if necessary. The new token is encrypted and persisted.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the token was renewed; false if still valid or on failure.</returns>
    Task<bool> RenewTokenIfNeededAsync(CancellationToken cancellationToken = default);
}
