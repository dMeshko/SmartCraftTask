using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using SmartCraftTask.Dtos;

namespace SmartCraftTask.Auth;

public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider timeProvider)
{
    private readonly JwtOptions _options = options.Value;

    public TokenResponse CreateToken(string username, string role)
    {
        var issuedAt = timeProvider.GetUtcNow();
        var expiresAt = issuedAt.AddMinutes(_options.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
                new Claim(ClaimNames.Role, role)
            ]),
            SigningCredentials = new SigningCredentials(_options.SigningKey(), SecurityAlgorithms.HmacSha256)
        };

        return new TokenResponse
        {
            AccessToken = new JsonWebTokenHandler().CreateToken(descriptor),
            ExpiresAt = expiresAt,
            Role = role
        };
    }
}

public static class ClaimNames
{
    /// <summary>
    /// The short claim name the token actually carries. Inbound claim mapping is switched off so
    /// it survives validation unchanged instead of being rewritten to the long WS-Federation URI.
    /// </summary>
    public const string Role = "role";
}
