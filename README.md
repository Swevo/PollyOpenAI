# PollyOpenAI

[![NuGet](https://img.shields.io/nuget/v/PollyOpenAI.svg)](https://www.nuget.org/packages/PollyOpenAI)
[![Downloads](https://img.shields.io/nuget/dt/PollyOpenAI.svg)](https://www.nuget.org/packages/PollyOpenAI)
[![CI](https://github.com/Swevo/PollyOpenAI/actions/workflows/build.yml/badge.svg)](https://github.com/Swevo/PollyOpenAI/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**Polly v8 resilience for OpenAI and Azure OpenAI API calls.**  
Automatic retry with exponential back-off, `Retry-After` header support, circuit breaker, and per-request timeout — wired up in one line via `IHttpClientBuilder`.

---

## Why PollyOpenAI?

OpenAI and Azure OpenAI APIs regularly return transient errors in production:

| Scenario | Without PollyOpenAI | With PollyOpenAI |
|---|---|---|
| **429 Rate limit** | Request fails immediately | Retries after `Retry-After` delay |
| **500 / 503 outage** | Request fails, user sees error | Retries with exponential back-off |
| **Sustained outage** | Every request waits for timeout | Circuit opens, fails fast immediately |
| **Slow response** | Request hangs for 100s+ | Times out per-attempt, retried quickly |
| **Request body** | Silently lost on retry | Buffered and re-sent on every attempt |

---

## Installation

```bash
dotnet add package PollyOpenAI
```

---

## Quick Start

```csharp
// Program.cs
builder.Services.AddHttpClient("openai", client =>
    {
        client.BaseAddress = new Uri("https://api.openai.com/");
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    })
    .AddPollyOpenAIResilience();

// Inject IHttpClientFactory and pass to the OpenAI SDK
var httpClient = httpClientFactory.CreateClient("openai");
var openAIClient = new OpenAIClient(new ApiKeyCredential(apiKey),
    new OpenAIClientOptions { Transport = new HttpClientTransport(httpClient) });
```

---

## Azure OpenAI

```csharp
builder.Services.AddHttpClient("azure-openai", client =>
    {
        client.BaseAddress = new Uri("https://my-resource.openai.azure.com/");
    })
    .AddPollyOpenAIResilience(options =>
    {
        options.MaxRetries = 5;
        options.BaseDelay = TimeSpan.FromSeconds(2);
        options.MaxDelay = TimeSpan.FromSeconds(60);
    });
```

---

## Named client shortcut

```csharp
// Registers IHttpClientFactory with a client named "PollyOpenAI"
builder.Services.AddPollyOpenAIHttpClient(
    baseAddress: new Uri("https://api.openai.com/"),
    configure: options => options.MaxRetries = 4);

// Resolve
var client = httpClientFactory.CreateClient("PollyOpenAI");
```

---

## Configuration

```csharp
.AddPollyOpenAIResilience(options =>
{
    // Retry
    options.MaxRetries = 3;                          // default: 3
    options.BaseDelay = TimeSpan.FromSeconds(1);     // default: 1 s
    options.MaxDelay = TimeSpan.FromSeconds(30);     // default: 30 s
    options.RespectRetryAfterHeader = true;          // default: true

    // Circuit breaker
    options.CircuitBreakerMinimumThroughput = 5;     // default: 5
    options.CircuitBreakerFailureRatio = 0.5;        // default: 0.5 (50%)
    options.CircuitBreakerSamplingDuration = TimeSpan.FromSeconds(30);
    options.CircuitBreakerBreakDuration = TimeSpan.FromSeconds(30);

    // Timeout (per request attempt)
    options.RequestTimeout = TimeSpan.FromSeconds(100);  // default: 100 s

    // Customise which status codes trigger retry
    options.TransientStatusCodes = new HashSet<HttpStatusCode>
    {
        HttpStatusCode.TooManyRequests,     // 429
        HttpStatusCode.InternalServerError, // 500
        HttpStatusCode.BadGateway,          // 502
        HttpStatusCode.ServiceUnavailable,  // 503
        HttpStatusCode.GatewayTimeout,      // 504
    };
})
```

---

## How it works

The pipeline order (outer → inner):

```
Request
  └─► Retry (exponential back-off, honours Retry-After)
        └─► Circuit Breaker (opens on sustained failures)
              └─► Timeout (per-attempt)
                    └─► HttpClient → OpenAI API
```

1. **Retry** — on `OpenAITransientException` (thrown for any status code in `TransientStatusCodes`), waits for the `Retry-After` delta if present, otherwise uses exponential back-off with jitter. The request body is buffered before the first send so it can be re-transmitted on every attempt.

2. **Circuit Breaker** — after enough failures within the sampling window the circuit opens, all subsequent calls fail-fast with `BrokenCircuitException` until the break duration elapses. Prevents hammering a degraded API.

3. **Timeout** — enforces a per-attempt deadline; throws `TimeoutRejectedException` if the OpenAI API doesn't respond in time.

---

## Exception types

| Exception | When thrown |
|---|---|
| `OpenAITransientException` | Transient HTTP status received (429, 5xx). Has `.StatusCode`, `.RetryAfter`, `.Response`. |
| `BrokenCircuitException` | Circuit is open; caught after retry is exhausted. |
| `TimeoutRejectedException` | Per-attempt timeout exceeded. |

---

## Comparison

| | Raw `HttpClient` | `Microsoft.Extensions.Http.Resilience` | **PollyOpenAI** |
|---|:---:|:---:|:---:|
| Retry on 429 | ❌ | ✅ (partial) | ✅ |
| Respects `Retry-After` header | ❌ | ❌ | ✅ |
| Retry on 5xx | ❌ | ✅ | ✅ |
| Circuit breaker | ❌ | ✅ | ✅ |
| Per-request timeout | ❌ | ✅ | ✅ |
| Request body re-sent on retry | ❌ | ❌ | ✅ |
| OpenAI-specific status defaults | ❌ | ❌ | ✅ |
| Zero config | ✅ | ❌ | ✅ |

---

## Related Packages

| Package | Downloads | Description |
|---|---|---|
| [PollyChaos](https://www.nuget.org/packages/PollyChaos) | [![Downloads](https://img.shields.io/nuget/dt/PollyChaos.svg)](https://www.nuget.org/packages/PollyChaos) | Chaos engineering / fault injection for Polly v8 |
| [PollyMediatR](https://www.nuget.org/packages/PollyMediatR) | [![Downloads](https://img.shields.io/nuget/dt/PollyMediatR.svg)](https://www.nuget.org/packages/PollyMediatR) | Polly v8 pipelines for MediatR request handlers |
| [PollyEFCore](https://www.nuget.org/packages/PollyEFCore) | [![Downloads](https://img.shields.io/nuget/dt/PollyEFCore.svg)](https://www.nuget.org/packages/PollyEFCore) | Polly v8 resilience for EF Core queries and SaveChanges |
| [PollyHealthChecks](https://www.nuget.org/packages/PollyHealthChecks) | [![Downloads](https://img.shields.io/nuget/dt/PollyHealthChecks.svg)](https://www.nuget.org/packages/PollyHealthChecks) | ASP.NET Core health checks for Polly v8 circuit breakers |
| [PollyElasticsearch](https://github.com/Swevo/PollyElasticsearch) | Polly v8 for Elastic.Clients.Elasticsearch |
| [PollyAzureKeyVault](https://github.com/Swevo/PollyAzureKeyVault) | Polly v8 for Azure Key Vault |
| [PollySendGrid](https://github.com/Swevo/PollySendGrid) | Polly v8 for SendGrid |
| [PollyMassTransit](https://github.com/Swevo/PollyMassTransit) | Polly v8 for MassTransit |
| [PollyBackoff](https://www.nuget.org/packages/PollyBackoff) | [![Downloads](https://img.shields.io/nuget/dt/PollyBackoff.svg)](https://www.nuget.org/packages/PollyBackoff) | Custom back-off strategies for Polly v8 |
| [PollyRedis](https://www.nuget.org/packages/PollyRedis) | [![Downloads](https://img.shields.io/nuget/dt/PollyRedis.svg)](https://www.nuget.org/packages/PollyRedis) | Polly v8 resilience for StackExchange.Redis — retry, circuit breaker, timeout |
| [PollySignalR](https://www.nuget.org/packages/PollySignalR) | [![Downloads](https://img.shields.io/nuget/dt/PollySignalR.svg)](https://www.nuget.org/packages/PollySignalR) | Polly v8 exponential back-off reconnect policy for SignalR HubConnection |
| [PollyGrpc](https://www.nuget.org/packages/PollyGrpc) | Polly v8 resilience (retry, CB, timeout) for gRPC .NET clients via Interceptor |
| [PollyKafka](https://www.nuget.org/packages/PollyKafka) | Polly v8 resilience (retry, CB, timeout) for Confluent.Kafka producers and consumers |
| [PollyAzureEventHub](https://github.com/Swevo/PollyAzureEventHub) | Polly v8 for Azure Event Hubs |
| [PollyAzureServiceBus](https://www.nuget.org/packages/PollyAzureServiceBus) | Polly v8 resilience (retry, CB, timeout) for Azure Service Bus senders and receivers |

---

| [PollyRabbitMQ](https://www.nuget.org/packages/PollyRabbitMQ) | Polly v8 resilience for RabbitMQ.Client channels |

## License

MIT
