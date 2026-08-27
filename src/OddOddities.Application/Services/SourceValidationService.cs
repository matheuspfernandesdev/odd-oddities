using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using OddOddities.Domain.Interfaces;

namespace OddOddities.Application.Services;

/// <summary>
/// Service for validating source URLs (RF-08).
/// Implements hexagonal port ISourceValidationPort with HttpClient-based validation.
/// </summary>
public sealed class SourceValidationService : ISourceValidationPort
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SourceValidationService> _logger;

    private const int TimeoutSeconds = 10;
    private const int MaxRedirects = 3;

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

        // 1. Validate URL format (must be absolute HTTP/HTTPS)
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

        // 2. Block internal IPs (SSRF prevention)
        if (IsInternalHost(uri.Host))
        {
            _logger.LogWarning(
                "Source URL validation failed: internal host '{Host}' blocked for URL '{Url}'",
                uri.Host,
                url);
            return false;
        }

        // 3. Send HEAD request with timeout and redirect limit
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
            // Cancellation can be either timeout or caller cancellation
            _logger.LogWarning(
                "Source URL validation failed: request timed out or cancelled for '{Url}'",
                url);
            return false;
        }
    }

    /// <summary>
    /// Checks if the host is an internal IP that should be blocked (SSRF prevention).
    /// Blocks: localhost, RFC1918 (10/8, 172.16/12, 192.168/16), link-local (169.254/16),
    /// loopback, and IPv6 unique-local addresses.
    /// </summary>
    private static bool IsInternalHost(string host)
    {
        // Block localhost variants
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        // Try to parse as IP address
        if (IPAddress.TryParse(host, out var ipAddress))
        {
            return IsInternalIpAddress(ipAddress);
        }

        // For hostnames, resolve and check all addresses
        try
        {
            var addresses = Dns.GetHostAddresses(host);
            return addresses.Length > 0 && addresses.All(IsInternalIpAddress);
        }
        catch (SocketException)
        {
            // DNS resolution failed — treat as invalid (can't determine if internal)
            return true;
        }
    }

    /// <summary>
    /// Checks if an IP address is internal (RFC1918, loopback, link-local, or IPv6 unique-local).
    /// </summary>
    private static bool IsInternalIpAddress(IPAddress ipAddress)
    {
        // Loopback (127.x.x.x, ::1)
        if (IPAddress.IsLoopback(ipAddress))
            return true;

        // IPv4 mapped to IPv6 — check inner
        if (ipAddress.IsIPv4MappedToIPv6)
            ipAddress = ipAddress.MapToIPv4();

        // IPv6 unique-local (fc00::/7)
        if (ipAddress.IsIPv6UniqueLocal)
            return true;

        // IPv6 link-local (fe80::/10)
        if (ipAddress.IsIPv6LinkLocal)
            return true;

        // Check RFC1918 private ranges for IPv4
        if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ipAddress.GetAddressBytes();
            return bytes[0] switch
            {
                // 10.0.0.0/8
                10 => true,
                // 172.16.0.0/12 (172.16.x.x – 172.31.x.x)
                172 => bytes[1] >= 16 && bytes[1] <= 31,
                // 192.168.0.0/16
                192 when bytes[1] == 168 => true,
                // 169.254.0.0/16 (link-local)
                169 when bytes[1] == 254 => true,
                _ => false
            };
        }

        return false;
    }
}
