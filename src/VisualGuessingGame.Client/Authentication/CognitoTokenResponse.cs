using System.Text.Json.Serialization;

namespace VisualGuessingGame.Client.Authentication;

public class CognitoTokenResponse
{
    [JsonPropertyName("id_token")]
    public required string IdToken { get; init; }
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }
    [JsonPropertyName("expires_in")]
    public required int ExpiresIn { get; init; }
    [JsonPropertyName("token_type")]
    public required string TokenType { get; init; }
}