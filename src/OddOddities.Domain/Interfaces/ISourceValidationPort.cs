namespace OddOddities.Domain.Interfaces;

/// <summary>
/// Port for validating source URLs (RF-08).
/// Ensures the URL is well-formed, responds successfully, and is not an internal IP.
/// </summary>
public interface ISourceValidationPort
{
    /// <summary>
    /// Validates a source URL against RF-08 requirements:
    /// - Well-formed HTTP/HTTPS URL
    /// - Not pointing to an internal IP (RFC1918, localhost, link-local)
    /// - Returns 2xx or 3xx status code
    /// - Timeout: 10 seconds
    /// - Max 3 redirects
    /// </summary>
    /// <param name="url">The source URL to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the URL is valid; otherwise, false.</returns>
    Task<bool> ValidateSourceUrlAsync(string url, CancellationToken cancellationToken = default);
}
