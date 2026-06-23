namespace PollyOpenAI;

/// <summary>
/// Configuration options for Polly resilience applied to OpenAI HTTP calls.
/// </summary>
public sealed class OpenAIResilienceOptions
{
    /// <summary>Maximum number of retry attempts for transient failures (429, 5xx). Default: 3.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Base delay between retries when exponential backoff is used. Default: 1 second.</summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Maximum delay cap for exponential backoff. Default: 30 seconds.</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Minimum number of requests in the sampling window before the circuit breaker can trip. Default: 5.
    /// </summary>
    public int CircuitBreakerMinimumThroughput { get; set; } = 5;

    /// <summary>Failure ratio (0–1) at which the circuit breaker opens. Default: 0.5 (50%).</summary>
    public double CircuitBreakerFailureRatio { get; set; } = 0.5;

    /// <summary>Duration of the sliding window used to evaluate failure rate. Default: 30 seconds.</summary>
    public TimeSpan CircuitBreakerSamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long the circuit stays open before allowing a probe request. Default: 30 seconds.</summary>
    public TimeSpan CircuitBreakerBreakDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Per-request timeout. Default: 100 seconds (matches HttpClient default).</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>
    /// When <c>true</c>, the retry delay honours the <c>Retry-After</c> header returned by OpenAI on 429 responses.
    /// Default: <c>true</c>.
    /// </summary>
    public bool RespectRetryAfterHeader { get; set; } = true;

    /// <summary>
    /// HTTP status codes that are considered transient and eligible for retry.
    /// Defaults to 429, 500, 502, 503, 504.
    /// </summary>
    public ISet<HttpStatusCode> TransientStatusCodes { get; set; } = new HashSet<HttpStatusCode>
    {
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout,
    };
}
