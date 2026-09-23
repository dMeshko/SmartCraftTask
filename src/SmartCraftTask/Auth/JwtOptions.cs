using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace SmartCraftTask.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>HS256 needs at least 256 bits of key, which is checked at startup.</summary>
    public const int MinimumKeyBytes = 32;

    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    public string Key { get; init; } = string.Empty;

    public int AccessTokenMinutes { get; init; } = 60;

    public SymmetricSecurityKey SigningKey() => new(Encoding.UTF8.GetBytes(Key));

    public bool HasUsableKey() => Encoding.UTF8.GetByteCount(Key) >= MinimumKeyBytes;
}
