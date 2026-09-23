namespace SmartCraftTask.Infrastructure;

/// <summary>
/// Limits live in configuration rather than in code, because the right number depends on where the
/// service is deployed and who is in front of it. The defaults here are the ones the container runs.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Applies to every endpoint that has not opted out.</summary>
    public int PermitLimit { get; set; } = 100;

    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// Token issuance is the endpoint worth guessing at, so it gets its own, much smaller budget.
    /// </summary>
    public int TokenPermitLimit { get; set; } = 10;

    public int TokenWindowSeconds { get; set; } = 60;
}

/// <summary>Named so an action can ask for the policy by name rather than by repeating a string.</summary>
public static class RateLimitPolicies
{
    public const string TokenIssuance = "token-issuance";
}
