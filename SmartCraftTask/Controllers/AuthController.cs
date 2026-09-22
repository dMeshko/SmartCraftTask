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
    /// Exchanges credentials for a bearer token. Two development accounts exist:
    /// "manager" (WarehouseManager) and "operator" (StockOperator).
    /// </summary>
    [HttpPost("token")]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<TokenResponse> IssueToken(TokenRequest request)
    {
        var role = users.FindRole(request.Username, request.Password);

        if (role is null)
        {
            // Deliberately vague: which half was wrong is not the caller's business.
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid credentials",
                detail: "The username or password is incorrect.");
        }

        return Ok(tokens.CreateToken(request.Username, role));
    }
}
