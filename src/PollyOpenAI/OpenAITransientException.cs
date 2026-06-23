namespace PollyOpenAI;

/// <summary>
/// Exception thrown by <see cref="PollyOpenAIHandler"/> when the OpenAI API returns
/// a transient HTTP status code (e.g. 429 or 5xx). Polly uses this to trigger retry
/// and circuit-breaker logic.
/// </summary>
public sealed class OpenAITransientException : Exception
{
    /// <summary>The HTTP status code returned by the API.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// The delay extracted from the <c>Retry-After</c> response header, if present.
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>The raw <see cref="HttpResponseMessage"/> that triggered this exception.</summary>
    public HttpResponseMessage Response { get; }

    internal OpenAITransientException(HttpResponseMessage response, TimeSpan? retryAfter)
        : base($"OpenAI API returned transient status {(int)response.StatusCode} {response.StatusCode}.")
    {
        StatusCode = response.StatusCode;
        RetryAfter = retryAfter;
        Response = response;
    }
}
