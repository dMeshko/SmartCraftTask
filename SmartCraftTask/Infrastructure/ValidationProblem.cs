using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace SmartCraftTask.Infrastructure;

/// <summary>
/// Builds the 400 for a request that failed model binding, with the JSON deserializer's own
/// messages replaced.
/// </summary>
/// <remarks>
/// Left alone those messages reach the caller verbatim: "The JSON value could not be converted to
/// System.Int32. Path: $.capacityInPallets | LineNumber: 0 | BytePositionInLine: 57". That names a
/// .NET type and a byte offset into the body, and says nothing the caller can act on that "this
/// field is not valid" does not. Validation messages written by this project — the FluentValidation
/// rules — are deliberately left exactly as they are, because those were written to be read.
/// </remarks>
public static class ValidationProblem
{
    private const string BodyKey = "body";

    /// <summary>System.Text.Json reports the location it failed at as a JSON path: "$", "$.field".</summary>
    private const char JsonPathPrefix = '$';

    public static IActionResult Create(ActionContext context)
    {
        var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problemDetails = factory.CreateValidationProblemDetails(context.HttpContext, Sanitise(context.ModelState));

        return new BadRequestObjectResult(problemDetails);
    }

    private static ModelStateDictionary Sanitise(ModelStateDictionary modelState)
    {
        var deserializerFailed = modelState.Keys.Any(key => key.StartsWith(JsonPathPrefix));

        if (!deserializerFailed)
        {
            return modelState;
        }

        var sanitised = new ModelStateDictionary();

        foreach (var (key, entry) in modelState)
        {
            if (!key.StartsWith(JsonPathPrefix))
            {
                // A body that would not deserialize also reports the parameter it should have bound
                // to as missing. That is a consequence of the same failure, not a second problem,
                // and repeating it only makes the response harder to read.
                continue;
            }

            var field = key.Length > 2 && key[1] == '.' ? key[2..] : BodyKey;

            sanitised.AddModelError(
                field,
                field == BodyKey
                    ? "The request body is not valid JSON."
                    : "The value supplied is not valid for this field.");
        }

        return sanitised;
    }
}
