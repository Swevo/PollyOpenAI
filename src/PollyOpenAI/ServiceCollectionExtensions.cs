namespace PollyOpenAI;

/// <summary>
/// Extension methods for registering a resilient OpenAI <see cref="HttpClient"/>
/// directly on <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a named <see cref="HttpClient"/> called <c>"PollyOpenAI"</c> configured with
    /// Polly v8 resilience (retry, circuit breaker, timeout). Inject via
    /// <c>IHttpClientFactory.CreateClient("PollyOpenAI")</c> or use a typed client.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="baseAddress">Optional base address for the OpenAI API (defaults to https://api.openai.com/).</param>
    /// <param name="configure">Optional delegate to customise resilience options.</param>
    /// <returns>The <see cref="IHttpClientBuilder"/> for further configuration.</returns>
    public static IHttpClientBuilder AddPollyOpenAIHttpClient(
        this IServiceCollection services,
        Uri? baseAddress = null,
        Action<OpenAIResilienceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services
            .AddHttpClient("PollyOpenAI", client =>
            {
                client.BaseAddress = baseAddress ?? new Uri("https://api.openai.com/");
            })
            .AddPollyOpenAIResilience(configure);
    }
}
