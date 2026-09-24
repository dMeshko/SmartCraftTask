using Microsoft.AspNetCore.OpenApi;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi;

namespace SmartCraftTask.Infrastructure;

/// <summary>
/// Declares the conditional-request headers on the operations that use them, so Swagger UI offers a
/// box for each. They are read from the request rather than bound as action arguments, which keeps
/// them out of the action signatures but leaves them invisible to the API explorer — hence this.
/// </summary>
/// <remarks>
/// Which operations need which header is derived rather than marked: an operation answering 428
/// demands a precondition, and one answering 304 is willing to skip a body the caller already has.
/// Both declarations already exist on the actions, so a new action gets the right header from the
/// response codes it documents, with nothing extra to remember.
/// </remarks>
public sealed class ConditionalRequestTransformer : IOpenApiOperationTransformer
{
    private const string ExampleETag = "\"AAAAAAAAB9E=\"";

    /// <summary>Response code that implies a header, and the header it implies.</summary>
    private static readonly (string ResponseCode, string HeaderName, string Description)[] Headers =
    [
        ("428", HeaderNames.IfMatch,
            "The ETag from a previous read of this resource, quoted. Required: without it the "
            + "request is refused with 428, and if the resource has moved on since, with 412."),
        ("304", HeaderNames.IfNoneMatch,
            "The ETag from a previous read. If it is still current the response is 304 with no "
            + "body; otherwise the resource comes back as usual. A list is allowed, and so is \"*\".")
    ];

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        foreach (var (responseCode, headerName, description) in Headers)
        {
            if (operation.Responses?.ContainsKey(responseCode) != true)
            {
                continue;
            }

            operation.Parameters ??= [];

            if (operation.Parameters.Any(parameter =>
                    string.Equals(parameter.Name, headerName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            operation.Parameters.Add(new OpenApiParameter
            {
                Name = headerName,
                In = ParameterLocation.Header,
                // Both are left optional on purpose. If-Match because the service answers 428 with
                // an explanation, which is more useful to see than a form Swagger refuses to
                // submit; If-None-Match because it genuinely is optional.
                Required = false,
                Description = description,
                Example = ExampleETag,
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            });
        }

        return Task.CompletedTask;
    }
}
