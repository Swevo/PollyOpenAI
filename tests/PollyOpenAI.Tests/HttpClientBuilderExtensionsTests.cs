namespace PollyOpenAI.Tests;

public class HttpClientBuilderExtensionsTests
{
    [Fact]
    public void AddPollyOpenAIResilience_RegistersHandler()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("openai")
            .AddPollyOpenAIResilience();

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        var client = factory.CreateClient("openai");
        client.Should().NotBeNull();
    }

    [Fact]
    public void AddPollyOpenAIResilience_WithOptions_AppliesConfiguration()
    {
        var services = new ServiceCollection();
        int configuredMaxRetries = 0;

        services.AddHttpClient("openai")
            .AddPollyOpenAIResilience(o =>
            {
                o.MaxRetries = 5;
                configuredMaxRetries = o.MaxRetries;
            });

        services.BuildServiceProvider()
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient("openai")
            .Should().NotBeNull();

        configuredMaxRetries.Should().Be(5);
    }

    [Fact]
    public void AddPollyOpenAIHttpClient_RegistersNamedClient()
    {
        var services = new ServiceCollection();
        services.AddPollyOpenAIHttpClient();

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("PollyOpenAI");

        client.Should().NotBeNull();
        client.BaseAddress.Should().Be(new Uri("https://api.openai.com/"));
    }

    [Fact]
    public void AddPollyOpenAIHttpClient_WithCustomBaseAddress_UsesIt()
    {
        var services = new ServiceCollection();
        var custom = new Uri("https://my-azure-openai.openai.azure.com/");
        services.AddPollyOpenAIHttpClient(baseAddress: custom);

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("PollyOpenAI");

        client.BaseAddress.Should().Be(custom);
    }

    [Fact]
    public void AddPollyOpenAIResilience_NullBuilder_Throws()
    {
        IHttpClientBuilder builder = null!;
        Action act = () => builder.AddPollyOpenAIResilience();
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddPollyOpenAIHttpClient_NullServices_Throws()
    {
        IServiceCollection services = null!;
        Action act = () => services.AddPollyOpenAIHttpClient();
        act.Should().Throw<ArgumentNullException>();
    }
}
