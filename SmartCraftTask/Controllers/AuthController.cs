using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartCraftTask.Auth;
using SmartCraftTask.Dtos;
using SmartCraftTask.Infrastructure;

namespace SmartCraftTask.Controllers;

[ApiController]
[Route("auth")]
[AllowAnonymous]
public class AuthController(DevUserStore users, TokenService tokens) : ControllerBase
{
    /// <summary>
    /// Exchanges credentials for a bearer token. This is the only endpoint open to anonymous
    /// callers. Four development accounts exist: "manager" (everything), "operator" (stock plus
    /// reading warehouses), "warehouse-viewer" and "stock-viewer" (read-only, one area each).
    /// </summary>
    [HttpPost("token")]
    // Its own budget, far smaller than the global one and counted by address rather than by user:
    // a caller guessing passwords has no user yet, which is the whole point of guessing.
    [EnableRateLimiting(RateLimitPolicies.TokenIssuance)]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<TokenResponse> IssueToken(TokenRequest request)
    {
        var roles = users.FindRoles(request.Username, request.Password);

        if (roles is null)
        {
            // Deliberately vague: which half was wrong is not the caller's business.
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid credentials",
                detail: "The username or password is incorrect.");
        }

        return Ok(tokens.CreateToken(request.Username, roles));
    }
}
