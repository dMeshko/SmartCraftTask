namespace SmartCraftTask.Dtos;

/// <summary>
/// Payload for POST /auth/token.
/// </summary>
// Validation rules live in TokenRequestValidator.
public sealed record TokenRequest
{
    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;
}
