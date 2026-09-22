namespace SmartCraftTask.Dtos;

/// <summary>
/// A bearer token and what it grants. Send it as "Authorization: Bearer &lt;accessToken&gt;".
/// </summary>
public sealed record TokenResponse
{
    public string AccessToken { get; init; } = string.Empty;

    public string TokenType { get; init; } = "Bearer";

    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>The roles carried by this token.</summary>
    public IReadOnlyCollection<string> Roles { get; init; } = [];
}
