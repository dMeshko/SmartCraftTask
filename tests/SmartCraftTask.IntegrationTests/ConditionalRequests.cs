using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SmartCraftTask.IntegrationTests;

/// <summary>
/// Mutating endpoints require If-Match, so tests read the resource first and hand its ETag back.
/// </summary>
internal static class ConditionalRequests
{
    /// <summary>
    /// A well-formed ETag that matches nothing. For asserting a 404 on a resource that is gone:
    /// it gets past the If-Match check so the lookup is what answers.
    /// </summary>
    public const string AnyVersion = "\"AAAAAAAAB9E=\"";

    /// <summary>The ETag a GET publishes for this resource.</summary>
    public static async Task<string> GetETagAsync(this HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var etag = response.Headers.ETag?.ToString();
        Assert.False(string.IsNullOrWhiteSpace(etag), $"GET {url} published no ETag.");

        return etag!;
    }

    /// <summary>
    /// An ETag that is well formed but is definitely not the one supplied, derived rather than
    /// hard-coded: the test database is created fresh, so its row versions start low and a constant
    /// picked to look stale can turn out to be exactly what a row currently holds.
    /// </summary>
    public static string StaleVersionOf(string etag)
    {
        var payload = Convert.FromBase64String(etag.Trim('"'));
        payload[0] ^= 0xFF;

        return $"\"{Convert.ToBase64String(payload)}\"";
    }

    /// <summary>A read offering the caller's version, which the service may answer with 304.</summary>
    public static Task<HttpResponseMessage> GetIfNoneMatchAsync(this HttpClient client, string url, string etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);

        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> PutIfMatchAsync(
        this HttpClient client, string url, object body, string etag) =>
        client.SendAsync(Conditional(HttpMethod.Put, url, etag, body));

    public static Task<HttpResponseMessage> DeleteIfMatchAsync(this HttpClient client, string url, string etag) =>
        client.SendAsync(Conditional(HttpMethod.Delete, url, etag));

    /// <summary>Reads the resource and immediately writes it back, in one step.</summary>
    public static async Task<HttpResponseMessage> PutCurrentAsync(this HttpClient client, string url, object body) =>
        await client.PutIfMatchAsync(url, body, await client.GetETagAsync(url));

    public static async Task<HttpResponseMessage> DeleteCurrentAsync(this HttpClient client, string url) =>
        await client.DeleteIfMatchAsync(url, await client.GetETagAsync(url));

    private static HttpRequestMessage Conditional(HttpMethod method, string url, string etag, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }
}
