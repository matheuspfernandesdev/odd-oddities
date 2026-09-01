using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using OddOddities.Domain.Interfaces;

namespace OddOddities.Infrastructure.Adapters;

/// <summary>
/// Service for validating source URLs (RF-08).
/// Implements hexagonal port ISourceValidationPort with HttpClient-based validation.
/// </summary>
public sealed class SourceValidationService : ISourceValidationPort
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SourceValidationService> _logger;

    public SourceValidationService(
        IHttpClientFactory httpClientFactory,
        ILogger<SourceValidationService> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> ValidateSourceUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            _logger.LogWarning("Source URL validation failed: URL is null or empty");
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            _logger.LogWarning("Source URL validation failed: malformed URL '{Url}'", url);
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            _logger.LogWarning(
                "Source URL validation failed: unsupported scheme '{Scheme}' for URL '{Url}'",
                uri.Scheme,
                url);
            return false;
        }

        if (IsInternalHost(uri.Host))
        {
            _logger.LogWarning(
                "Source URL validation failed: internal host '{Host}' blocked for URL '{Url}'",
                uri.Host,
                url);
            return false;
        }

        try
        {
            var client = _httpClientFactory.CreateClient(nameof(SourceValidationService));

            using var request = new HttpRequestMessage(HttpMethod.Head, uri);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

            var statusCode = (int)response.StatusCode;
            var isValid = statusCode >= 200 && statusCode < 400;

            if (isValid)
            {
                _logger.LogInformation(
                    "Source URL validated successfully: '{Url}' returned {StatusCode}",
                    url,
                    statusCode);
            }
            else
            {
                _logger.LogWarning(
                    "Source URL validation failed: '{Url}' returned {StatusCode}",
                    url,
                    statusCode);
            }

            return isValid;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "Source URL validation failed: HTTP error for '{Url}'",
                url);
            return false;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning(
                "Source URL validation failed: request timed out or cancelled for '{Url}'",
                url);
            return false;
        }
    }

    private static bool IsInternalHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        if (IPAddress.TryParse(host, out var ipAddress))
        {
            return IsInternalIpAddress(ipAddress);
        }

        try
        {
            var addresses = Dns.GetHostAddresses(host);
            return addresses.Length > 0 && addresses.All(IsInternalIpAddress);
        }
        catch (SocketException)
        {
            return true;
        }
    }

    private static bool IsInternalIpAddress(IPAddress ipAddress)
    {
        if (IPAddress.IsLoopback(ipAddress))
            return true;

        if (ipAddress.IsIPv4MappedToIPv6)
            ipAddress = ipAddress.MapToIPv4();

        if (ipAddress.IsIPv6UniqueLocal)
            return true;

        if (ipAddress.IsIPv6LinkLocal)
            return true;

        if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ipAddress.GetAddressBytes();
            return bytes[0] switch
            {
                10 => true,
                172 => bytes[1] >= 16 && bytes[1] <= 31,
                192 when bytes[1] == 168 => true,
                169 when bytes[1] == 254 => true,
                _ => false
            };
        }

        return false;
    }
}
