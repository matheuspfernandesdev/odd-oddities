using System.Text.Json.Serialization;

namespace OddOddities.Application.DTOs;

/// <summary>
/// Response from Meta Graph API /refresh_access_token endpoint.
/// </summary>
public sealed class RefreshTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

/// <summary>
/// Result of a token refresh operation.
/// </summary>
public sealed class RefreshTokenResult
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}
