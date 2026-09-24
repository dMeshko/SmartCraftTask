using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;

namespace SmartCraftTask.Infrastructure;

/// <summary>
/// Declares <c>429</c> on every operation the rate limiter is able to refuse.
/// </summary>
/// <remarks>
/// Derived rather than annotated. Writing <c>[ProducesResponseType(429)]</c> on each action would be
/// eleven copies of the same line and one more thing to remember on the twelfth, which is the trap
/// <see cref="ConditionalRequestTransformer"/> exists to avoid. Every documented operation is a
/// controller action behind the limiter, so the rule is "all of them except the ones that opted out",
/// and an action that opts out later takes the document with it without anyone editing this.
/// </remarks>
public sealed class RateLimitResponseTransformer : IOpenApiOperationTransformer
{
    private const string TooManyRequests = "429";

    /// <summary>The schema the framework already registers for every other failure response.</summary>
    private const string ProblemDetailsSchema = "ProblemDetails";

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var exempt = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<DisableRateLimitingAttribute>()
            .Any();

        if (exempt)
        {
            return Task.CompletedTask;
        }

        operation.Responses ??= [];

        if (operation.Responses.ContainsKey(TooManyRequests))
        {
            return Task.CompletedTask;
        }

        operation.Responses[TooManyRequests] = new OpenApiResponse
        {
            Description = "Too many requests. The Retry-After header gives the interval to wait.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                // The one the limiter actually writes, rather than the three content types MVC
                // advertises for responses that go through its output formatters.
                ["application/problem+json"] = new()
                {
                    // The host document is passed so the reference can resolve its target, not
                    // because serialisation needs it: unlike OpenApiSecuritySchemeReference, which
                    // emits an empty object without one, this type writes a $ref either way. Removing
                    // the argument fails no test, so the test pins the $ref rather than this line.
                    Schema = new OpenApiSchemaReference(ProblemDetailsSchema, context.Document)
                }
            }
        };

        return Task.CompletedTask;
    }
}
