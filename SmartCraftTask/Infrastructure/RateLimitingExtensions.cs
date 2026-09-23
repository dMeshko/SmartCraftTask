using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace SmartCraftTask.Infrastructure;

public static class RateLimitingExtensions
{
    private const string AnonymousPartition = "anonymous";

    /// <summary>
    /// Shared by the limiter and by the Retry-After it reports, so the advice cannot drift from the
    /// window it describes.
    /// </summary>
    private const int SegmentsPerWindow = 4;

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        // Bound rather than read here. Calling Get<T>() at this point snapshots whatever
        // configuration exists while Program is still running its top-level statements, which is
        // before a test host has had the chance to add its own sources — so the limits would ignore
        // them. Resolved through IOptions per request, the section is read when it is actually used.
        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.SectionName));

        services.AddRateLimiter(options =>
        {
            // A sliding window rather than a fixed one: a fixed window lets a caller spend its whole
            // budget at the end of one window and again at the start of the next, which is twice the
            // intended rate across the boundary.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var limits = Limits(context);

                return RateLimitPartition.GetSlidingWindowLimiter(
                    PartitionKey(context),
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = limits.PermitLimit,
                        Window = TimeSpan.FromSeconds(limits.WindowSeconds),
                        SegmentsPerWindow = SegmentsPerWindow,
                        QueueLimit = 0
                    });
            });

            // Credentials are guessed by trying them, so this one counts by address and does not
            // care who the caller claims to be. A fixed window is plenty when the budget is small.
            options.AddPolicy(RateLimitPolicies.TokenIssuance, context =>
            {
                var limits = Limits(context);

                return RateLimitPartition.GetFixedWindowLimiter(
                    ClientAddress(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.TokenPermitLimit,
                        Window = TimeSpan.FromSeconds(limits.TokenWindowSeconds),
                        QueueLimit = 0
                    });
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                // Derived from the configured window rather than read from the lease. The lease
                // lists RETRY_AFTER among its MetadataNames, but returns nothing for it through
                // either the typed or the string lookup, so reading it would have meant shipping a
                // branch that never runs and a header that never appears.
                context.HttpContext.Response.Headers.RetryAfter =
                    RetryAfterSeconds(context.HttpContext).ToString(CultureInfo.InvariantCulture);

                var problemDetailsService = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();

                await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Type = "https://tools.ietf.org/html/rfc6585#section-4",
                        Title = "Too many requests",
                        Detail = "This client has spent its request budget for the moment. Retry after the "
                                 + "interval in the Retry-After header.",
                        Status = StatusCodes.Status429TooManyRequests
                    }
                });
            };
        });

        return services;
    }

    /// <summary>
    /// When the caller should come back. The fixed window guarding token issuance has to roll over
    /// completely; the sliding window frees a permit as soon as its oldest segment expires.
    /// </summary>
    private static int RetryAfterSeconds(HttpContext context)
    {
        var limits = Limits(context);

        var policy = context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;

        return policy == RateLimitPolicies.TokenIssuance
            ? limits.TokenWindowSeconds
            : (int)Math.Ceiling(limits.WindowSeconds / (double)SegmentsPerWindow);
    }

    private static RateLimitOptions Limits(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;

    /// <summary>
    /// One budget per authenticated user, falling back to the address for callers who have not
    /// identified themselves. Partitioning by user is the fairer unit — several colleagues behind
    /// one office address should not eat each other's budget — and it is why UseRateLimiter comes
    /// after UseAuthentication: before it, there is no user to partition by.
    /// </summary>
    private static string PartitionKey(HttpContext context) =>
        context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value is { Length: > 0 } subject
            ? $"user:{subject}"
            : $"address:{ClientAddress(context)}";

    /// <summary>
    /// Null for in-process hosts such as the test server, which is why it has a name of its own
    /// rather than being left to collapse several callers into a "" partition by accident.
    /// </summary>
    private static string ClientAddress(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? AnonymousPartition;
}
