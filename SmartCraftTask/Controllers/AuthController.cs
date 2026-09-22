using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartCraftTask.Auth;
using SmartCraftTask.Dtos;

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
