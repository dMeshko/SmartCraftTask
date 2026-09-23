using Microsoft.AspNetCore.OpenApi;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi;

namespace SmartCraftTask.Infrastructure;

/// <summary>
/// Declares the <c>If-Match</c> header on the operations that require it, so Swagger UI offers a box
/// for it. The header is read from the request rather than bound as an action argument, which keeps
/// it out of the action signatures but leaves it invisible to the API explorer — hence this.
/// </summary>
/// <remarks>
/// Which operations need it is not guessed: an operation that declares a 428 response is by
/// definition one that demands a precondition, and that declaration already exists on each of them.
/// Nothing extra to remember when a new mutating action is added.
/// </remarks>
public sealed class IfMatchHeaderTransformer : IOpenApiOperationTransformer
{
    private const string PreconditionRequired = "428";

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (operation.Responses?.ContainsKey(PreconditionRequired) != true)
        {
            return Task.CompletedTask;
        }

        operation.Parameters ??= [];

        if (operation.Parameters.Any(parameter =>
                string.Equals(parameter.Name, HeaderNames.IfMatch, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.CompletedTask;
        }

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = HeaderNames.IfMatch,
            In = ParameterLocation.Header,
            // Left optional on purpose: the service answers 428 with an explanation, which is more
            // useful to see than a form Swagger refuses to submit.
            Required = false,
            Description =
                "The ETag from a previous read of this resource, quoted. Required: without it the "
                + "request is refused with 428, and if the resource has moved on since, with 412.",
            Example = "\"AAAAAAAAB9E=\"",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
        });

        return Task.CompletedTask;
    }
}