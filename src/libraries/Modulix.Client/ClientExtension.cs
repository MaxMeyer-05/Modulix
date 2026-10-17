using System.Net.Http.Json;
using System.Net.Http.Headers;

namespace Modulix.Client;

/// <summary>
/// Provides extension methods for the HttpClient to simplify sending HTTP requests 
/// with JSON content and optional authentication.
/// </summary>
public static class ClientExtension
{
    /// <summary>
    /// Sends an HTTP request without a body, optionally including a bearer token.
    /// </summary>
    /// <param name="httpClient">The HTTP client used to send the request.</param>
    /// <param name="method">The HTTP method to use for the request.</param>
    /// <param name="requestUri">The URI of the request.</param>
    /// <param name="authToken">Optional - authentication token to include in the request headers.</param>
    /// <param name="ct">Optional - a cancellation token that can be used to cancel the request.</param>
    /// <returns>The HTTP response message as the result.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue such as network connectivity, 
    /// DNS failure, server certificate validation, or timeout.
    /// </exception>
    public static async Task<HttpResponseMessage> SendRequestAsync(
        this HttpClient httpClient,
        HttpMethod method,
        string requestUri,
        string? authToken = null,
        CancellationToken ct = default)
    {
        using var request = CreateRequest(method, requestUri, authToken);
        return await httpClient.SendAsync(request, ct);
    }

    /// <summary>
    /// Sends an HTTP request with JSON content, optionally including a bearer token.
    /// </summary>
    /// <typeparam name="T">The type of the request body.</typeparam>
    /// <param name="httpClient">The HTTP client used to send the request.</param>
    /// <param name="method">The HTTP method to use for the request.</param>
    /// <param name="requestUri">The URI of the request.</param>
    /// <param name="requestContent">The content to serialize as JSON.</param>
    /// <param name="authToken">Optional - authentication token to include in the request headers.</param>
    /// <param name="ct">Optional - a cancellation token that can be used to cancel the request.</param>
    /// <returns>The HTTP response message as the result.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown when the request fails due to an underlying issue such as network connectivity, 
    /// DNS failure, server certificate validation, or timeout.
    /// </exception>
    public static async Task<HttpResponseMessage> SendRequestAsync<T>(
        this HttpClient httpClient,
        HttpMethod method,
        string requestUri,
        T requestContent,
        string? authToken = null,
        CancellationToken ct = default)
    {
        using var request = CreateRequest(method, requestUri, authToken);
        request.Content = JsonContent.Create(requestContent);
        return await httpClient.SendAsync(request, ct);
    }

    /// <summary>
    /// Creates an <see cref="HttpRequestMessage"/> with the specified HTTP method, request URI, and optional bearer token.
    /// </summary>
    /// <param name="method">The HTTP method to use for the request.</param>
    /// <param name="requestUri">The URI of the request.</param>
    /// <param name="authToken">Optional - authentication token to include in the request headers.</param>
    /// <returns>The created <see cref="HttpRequestMessage"/>.</returns>
    private static HttpRequestMessage CreateRequest(HttpMethod method, string requestUri, string? authToken = null)
    {
        var request = new HttpRequestMessage(method, requestUri);
        if (!string.IsNullOrWhiteSpace(authToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

        return request;
    }
}