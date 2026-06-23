using System.Net.Http.Headers;

namespace PollyOpenAI.Tests;

/// <summary>
/// A controllable inner <see cref="HttpMessageHandler"/> for unit testing.
/// Each response in the queue is returned in order; the last response repeats.
/// </summary>
internal sealed class MockHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();
    public List<HttpRequestMessage> Requests { get; } = new();

    public void Enqueue(HttpResponseMessage response) => _responses.Enqueue(response);

    public void Enqueue(HttpStatusCode statusCode, string? body = null, TimeSpan? retryAfter = null)
    {
        var response = new HttpResponseMessage(statusCode);
        if (body is not null)
            response.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        if (retryAfter.HasValue)
            response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter.Value);
        _responses.Enqueue(response);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var response = _responses.Count > 0 ? _responses.Dequeue() : new HttpResponseMessage(HttpStatusCode.OK);
        return Task.FromResult(response);
    }
}

/// <summary>
/// Helper to build a test <see cref="HttpClient"/> with <see cref="PollyOpenAIHandler"/>
/// and a given <see cref="MockHandler"/> as the inner handler.
/// </summary>
internal static class TestClientFactory
{
    public static HttpClient Create(MockHandler mock, Action<OpenAIResilienceOptions>? configure = null)
    {
        var options = new OpenAIResilienceOptions
        {
            // Fast delays so tests don't take seconds
            BaseDelay = TimeSpan.Zero,
            MaxDelay = TimeSpan.Zero,
            RequestTimeout = TimeSpan.FromSeconds(10),
            CircuitBreakerSamplingDuration = TimeSpan.FromSeconds(10),
            CircuitBreakerBreakDuration = TimeSpan.FromMilliseconds(500), // Polly v8 minimum
        };
        configure?.Invoke(options);

        var handler = new PollyOpenAIHandler(options) { InnerHandler = mock };
        return new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/") };
    }
}
