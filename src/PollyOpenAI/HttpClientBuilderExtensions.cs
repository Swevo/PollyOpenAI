namespace PollyOpenAI;

/// <summary>
/// Extension methods for registering <see cref="PollyOpenAIHandler"/> with <see cref="IHttpClientBuilder"/>.
/// </summary>
public static class HttpClientBuilderExtensions
{
    /// <summary>
    /// Adds Polly v8 resilience (retry, circuit breaker, timeout) to an <see cref="IHttpClientBuilder"/>
    /// for OpenAI API calls.
    /// </summary>
    /// <param name="builder">The HTTP client builder.</param>
    /// <param name="configure">Optional delegate to customise resilience options.</param>
    /// <returns>The original builder to allow chaining.</returns>
    public static IHttpClientBuilder AddPollyOpenAIResilience(
        this IHttpClientBuilder builder,
        Action<OpenAIResilienceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new OpenAIResilienceOptions();
        configure?.Invoke(options);

        return builder.AddHttpMessageHandler(() => new PollyOpenAIHandler(options));
    }
}
