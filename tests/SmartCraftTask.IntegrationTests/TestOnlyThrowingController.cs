using Microsoft.AspNetCore.Mvc;

namespace SmartCraftTask.IntegrationTests;

/// <summary>
/// A route that fails on purpose, so the unhandled-exception path can be tested at all. It lives in
/// the test assembly and is reachable only because <see cref="ApiFixture"/> registers that assembly
/// as an application part, so the application itself ships no endpoint whose purpose is to throw.
/// </summary>
/// <remarks>
/// Hidden from the API document, because it is scaffolding rather than surface — and because the
/// sweep in <see cref="AuthApiTests"/> holds every documented path to declaring a token requirement,
/// which this one has no business satisfying. It is still behind the fallback authorisation policy
/// like everything else, so it needs a token to reach.
/// </remarks>
[ApiController]
[Route("test-only")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class TestOnlyThrowingController : ControllerBase
{
    [HttpGet("throw")]
    public IActionResult Throw() =>
        throw new InvalidOperationException("Deliberate failure from the test-only controller.");
}
