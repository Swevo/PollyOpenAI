namespace PollyOpenAI.Tests;

public class PollyOpenAIHandlerTests
{
    // ── Success path ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SuccessResponse_ReturnsDirectly()
    {
        var mock = new MockHandler();
        mock.Enqueue(HttpStatusCode.OK, """{"id":"chatcmpl-1"}""");
        var client = TestClientFactory.Create(mock);

        var response = await client.GetAsync("/v1/chat/completions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        mock.Requests.Should().HaveCount(1);
    }

    // ── Retry on 429 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task On429_RetriesAndSucceeds()
    {
        var mock = new MockHandler();
        mock.Enqueue(HttpStatusCode.TooManyRequests);
        mock.Enqueue(HttpStatusCode.TooManyRequests);
        mock.Enqueue(HttpStatusCode.OK, """{"id":"chatcmpl-2"}""");
        var client = TestClientFactory.Create(mock, o => o.MaxRetries = 3);

        var response = await client.GetAsync("/v1/chat/completions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        mock.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task On429_ExhaustsRetries_ThrowsOpenAITransientException()
    {
        var mock = new MockHandler();
        for (int i = 0; i < 5; i++)
            mock.Enqueue(HttpStatusCode.TooManyRequests);
        var client = TestClientFactory.Create(mock, o =>
        {
            o.MaxRetries = 3;
            o.CircuitBreakerMinimumThroughput = 100; // prevent circuit breaker from interfering
        });

        var act = () => client.GetAsync("/v1/chat/completions");

        await act.Should().ThrowAsync<OpenAITransientException>()
            .Where(ex => ex.StatusCode == HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task On429_WithRetryAfterHeader_WaitsCorrectDelay()
    {
        var mock = new MockHandler();
        mock.Enqueue(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromMilliseconds(50));
        mock.Enqueue(HttpStatusCode.OK);
        var client = TestClientFactory.Create(mock, o =>
        {
            o.MaxRetries = 2;
            o.RespectRetryAfterHeader = true;
            o.CircuitBreakerMinimumThroughput = 100;
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = await client.GetAsync("/v1/chat/completions");
        sw.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeGreaterThan(40); // waited ~50 ms
        mock.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task On429_WithRetryAfterDisabled_UsesBaseDelay()
    {
        var mock = new MockHandler();
        mock.Enqueue(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromSeconds(60));
        mock.Enqueue(HttpStatusCode.OK);
        var client = TestClientFactory.Create(mock, o =>
        {
            o.MaxRetries = 2;
            o.BaseDelay = TimeSpan.Zero;
            o.RespectRetryAfterHeader = false;
            o.CircuitBreakerMinimumThroughput = 100;
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = await client.GetAsync("/v1/chat/completions");
        sw.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sw.ElapsedMilliseconds.Should().BeLessThan(5000); // did NOT wait 60 s
    }

    // ── Retry on 5xx ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task On5xx_RetriesAndSucceeds(HttpStatusCode code)
    {
        var mock = new MockHandler();
        mock.Enqueue(code);
        mock.Enqueue(HttpStatusCode.OK);
        var client = TestClientFactory.Create(mock, o =>
        {
            o.MaxRetries = 2;
            o.CircuitBreakerMinimumThroughput = 100;
        });

        var response = await client.GetAsync("/v1/chat/completions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        mock.Requests.Should().HaveCount(2);
    }

    // ── Non-transient status codes ────────────────────────────────────────────

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task NonTransientStatus_NotRetried(HttpStatusCode code)
    {
        var mock = new MockHandler();
        mock.Enqueue(code);
        var client = TestClientFactory.Create(mock);

        var response = await client.GetAsync("/v1/chat/completions");

        response.StatusCode.Should().Be(code);
        mock.Requests.Should().HaveCount(1); // no retry
    }

    // ── Request body re-sent on retry ─────────────────────────────────────────

    [Fact]
    public async Task RequestBody_IsResentOnEachRetry()
    {
        const string body = """{"model":"gpt-4o","messages":[{"role":"user","content":"Hello"}]}""";
        var mock = new MockHandler();
        mock.Enqueue(HttpStatusCode.TooManyRequests);
        mock.Enqueue(HttpStatusCode.OK);
        var client = TestClientFactory.Create(mock, o =>
        {
            o.MaxRetries = 2;
            o.CircuitBreakerMinimumThroughput = 100;
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        await client.SendAsync(request);

        mock.Requests.Should().HaveCount(2);
        for (int i = 0; i < mock.Requests.Count; i++)
        {
            var sentBody = await mock.Requests[i].Content!.ReadAsStringAsync();
            sentBody.Should().Be(body, because: $"request {i + 1} should carry the full body");
        }
    }

    // ── Circuit breaker ───────────────────────────────────────────────────────

    [Fact]
    public async Task CircuitBreaker_OpensAfterThreshold()
    {
        var mock = new MockHandler();
        // Enough 500s to trip the circuit
        for (int i = 0; i < 20; i++)
            mock.Enqueue(HttpStatusCode.InternalServerError);

        var client = TestClientFactory.Create(mock, o =>
        {
            o.MaxRetries = 0;
            o.CircuitBreakerMinimumThroughput = 3;
            o.CircuitBreakerFailureRatio = 0.5;
            o.CircuitBreakerSamplingDuration = TimeSpan.FromSeconds(10);
            o.CircuitBreakerBreakDuration = TimeSpan.FromSeconds(10);
        });

        // Send enough requests to trip the breaker
        var exceptions = new List<Exception>();
        for (int i = 0; i < 10; i++)
        {
            try { await client.GetAsync("/v1/chat/completions"); }
            catch (Exception ex) { exceptions.Add(ex); }
        }

        exceptions.Should().Contain(e => e is Polly.CircuitBreaker.BrokenCircuitException);
    }

    // ── Timeout ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Timeout_ThrowsWhenExceeded()
    {
        var mock = new MockHandler();
        // Slow handler
        var slowMock = new SlowMockHandler(TimeSpan.FromSeconds(5));
        var options = new OpenAIResilienceOptions
        {
            MaxRetries = 0,
            RequestTimeout = TimeSpan.FromMilliseconds(50),
            CircuitBreakerMinimumThroughput = 100,
        };
        var handler = new PollyOpenAIHandler(options) { InnerHandler = slowMock };
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/") };

        var act = () => client.GetAsync("/v1/chat/completions");

        await act.Should().ThrowAsync<Polly.Timeout.TimeoutRejectedException>();
    }

    // ── Exception propagation ─────────────────────────────────────────────────

    [Fact]
    public async Task NetworkException_PropagatesAfterRetries()
    {
        var errorMock = new ExceptionMockHandler(new HttpRequestException("Connection refused"));
        var options = new OpenAIResilienceOptions
        {
            MaxRetries = 2,
            BaseDelay = TimeSpan.Zero,
            MaxDelay = TimeSpan.Zero,
            CircuitBreakerMinimumThroughput = 100,
            RequestTimeout = TimeSpan.FromSeconds(10),
        };
        // Add HttpRequestException to retry predicates via custom options — but by default HttpRequestException is NOT handled.
        // Confirm it propagates without retry.
        var handler = new PollyOpenAIHandler(options) { InnerHandler = errorMock };
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/") };

        var act = () => client.GetAsync("/v1/chat/completions");

        await act.Should().ThrowAsync<HttpRequestException>();
        errorMock.CallCount.Should().Be(1); // no retry for non-transient exceptions
    }

    // ── OpenAITransientException properties ──────────────────────────────────

    [Fact]
    public async Task OpenAITransientException_HasCorrectProperties()
    {
        var mock = new MockHandler();
        mock.Enqueue(HttpStatusCode.TooManyRequests, retryAfter: TimeSpan.FromSeconds(5));
        var client = TestClientFactory.Create(mock, o =>
        {
            o.MaxRetries = 0;
            o.CircuitBreakerMinimumThroughput = 100;
        });

        var act = () => client.GetAsync("/v1/chat/completions");

        var ex = await act.Should().ThrowAsync<OpenAITransientException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        ex.Which.RetryAfter.Should().BeCloseTo(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));
        ex.Which.Response.Should().NotBeNull();
    }

    // ── Custom transient status codes ─────────────────────────────────────────

    [Fact]
    public async Task CustomTransientStatusCode_IsRetried()
    {
        var mock = new MockHandler();
        mock.Enqueue((HttpStatusCode)418); // I'm a teapot — add to transient codes
        mock.Enqueue(HttpStatusCode.OK);
        var client = TestClientFactory.Create(mock, o =>
        {
            o.MaxRetries = 2;
            o.TransientStatusCodes = new HashSet<HttpStatusCode> { (HttpStatusCode)418 };
            o.CircuitBreakerMinimumThroughput = 100;
        });

        var response = await client.GetAsync("/v1/chat/completions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        mock.Requests.Should().HaveCount(2);
    }
}

// ── Test doubles ──────────────────────────────────────────────────────────────

internal sealed class SlowMockHandler : HttpMessageHandler
{
    private readonly TimeSpan _delay;
    public SlowMockHandler(TimeSpan delay) => _delay = delay;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(_delay, cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK);
    }
}

internal sealed class ExceptionMockHandler : HttpMessageHandler
{
    private readonly Exception _exception;
    public int CallCount { get; private set; }
    public ExceptionMockHandler(Exception exception) => _exception = exception;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        throw _exception;
    }
}
