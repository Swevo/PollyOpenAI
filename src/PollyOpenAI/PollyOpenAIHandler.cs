namespace PollyOpenAI;

/// <summary>
/// An <see cref="DelegatingHandler"/> that wraps every OpenAI HTTP request in a Polly v8
/// resilience pipeline: retry with exponential back-off (respecting <c>Retry-After</c>),
/// circuit breaker, and per-request timeout.
/// </summary>
public sealed class PollyOpenAIHandler : DelegatingHandler
{
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;
    private readonly OpenAIResilienceOptions _options;

    /// <summary>
    /// Initialises the handler with the given options, building the resilience pipeline.
    /// </summary>
    public PollyOpenAIHandler(OpenAIResilienceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _pipeline = BuildPipeline(options);
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Buffer the request body so it can be re-sent on retry.
        byte[]? bodyBytes = null;
        string? contentType = null;
        string? contentEncoding = null;

        if (request.Content is not null)
        {
            bodyBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            contentType = request.Content.Headers.ContentType?.ToString();
            contentEncoding = request.Content.Headers.ContentEncoding.FirstOrDefault();
        }

        return await _pipeline.ExecuteAsync(async ct =>
        {
            var clone = CloneRequest(request, bodyBytes, contentType, contentEncoding);
            var response = await base.SendAsync(clone, ct).ConfigureAwait(false);

            if (_options.TransientStatusCodes.Contains(response.StatusCode))
            {
                var retryAfter = ParseRetryAfter(response);
                throw new OpenAITransientException(response, retryAfter);
            }

            return response;
        }, cancellationToken).ConfigureAwait(false);
    }

    private static HttpRequestMessage CloneRequest(
        HttpRequestMessage original, byte[]? body, string? contentType, string? contentEncoding)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);
        clone.Version = original.Version;

        foreach (var (key, values) in original.Headers)
            clone.Headers.TryAddWithoutValidation(key, values);

        if (body is not null)
        {
            clone.Content = new ByteArrayContent(body);
            if (contentType is not null)
                clone.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            if (contentEncoding is not null)
                clone.Content.Headers.TryAddWithoutValidation("Content-Encoding", contentEncoding);
        }

        return clone;
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is { } retryAfter)
        {
            if (retryAfter.Delta.HasValue)
                return retryAfter.Delta.Value;

            if (retryAfter.Date.HasValue)
            {
                var delay = retryAfter.Date.Value - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                    return delay;
            }
        }

        return null;
    }

    private static ResiliencePipeline<HttpResponseMessage> BuildPipeline(OpenAIResilienceOptions options)
    {
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();

        if (options.MaxRetries >= 1)
        {
            builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<OpenAITransientException>(),
                MaxRetryAttempts = options.MaxRetries,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = options.BaseDelay,
                MaxDelay = options.MaxDelay,
                DelayGenerator = args =>
                {
                    if (options.RespectRetryAfterHeader &&
                        args.Outcome.Exception is OpenAITransientException { RetryAfter: { } retryAfter })
                    {
                        return new ValueTask<TimeSpan?>(retryAfter);
                    }
                    return new ValueTask<TimeSpan?>(default(TimeSpan?));
                },
            });
        }

        builder
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<OpenAITransientException>(),
                FailureRatio = options.CircuitBreakerFailureRatio,
                MinimumThroughput = options.CircuitBreakerMinimumThroughput,
                SamplingDuration = options.CircuitBreakerSamplingDuration,
                BreakDuration = options.CircuitBreakerBreakDuration,
            })
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = options.RequestTimeout,
            });

        return builder.Build();
    }
}
