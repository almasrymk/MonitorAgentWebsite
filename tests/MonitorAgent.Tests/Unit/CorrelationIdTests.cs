using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MonitorAgent.BuildingBlocks.Web;

namespace MonitorAgent.Tests.Unit;

/// <summary>The correlation id travels from the visitor's request to LicensingPlatform and Monitor Cloud.</summary>
public class CorrelationIdTests
{
    private sealed class Recorder : HttpMessageHandler
    {
        public List<string?> Ids { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Ids.Add(request.Headers.TryGetValues(CorrelationId.HeaderName, out var v) ? v.Single() : null);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static (HttpClient Client, Recorder Recorder, IHttpContextAccessor Accessor) Build()
    {
        var recorder = new Recorder();
        var services = new ServiceCollection();
        services.AddHttpClient("peer", c => c.BaseAddress = new Uri("https://peer.test/"))
            .AddCorrelationId()
            .ConfigurePrimaryHttpMessageHandler(() => recorder);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IHttpClientFactory>().CreateClient("peer"), recorder,
            provider.GetRequiredService<IHttpContextAccessor>());
    }

    [Fact]
    public async Task The_current_request_id_is_forwarded()
    {
        var (client, recorder, accessor) = Build();
        var context = new DefaultHttpContext();
        context.Items[CorrelationId.ItemKey] = "req-123";
        accessor.HttpContext = context;

        await client.GetAsync("api/v1/anything");

        Assert.Equal(["req-123"], recorder.Ids);
    }

    [Fact]
    public async Task A_call_outside_a_request_gets_a_new_id_each_time()
    {
        var (client, recorder, accessor) = Build();
        accessor.HttpContext = null;

        await client.GetAsync("a");
        await client.GetAsync("b");

        Assert.All(recorder.Ids, id => Assert.True(CorrelationId.IsValid(id)));
        Assert.NotEqual(recorder.Ids[0], recorder.Ids[1]);
    }

    [Fact]
    public async Task An_explicit_id_on_the_request_is_kept()
    {
        var (client, recorder, _) = Build();
        using var request = new HttpRequestMessage(HttpMethod.Get, "a");
        request.Headers.Add(CorrelationId.HeaderName, "explicit-1");

        await client.SendAsync(request);

        Assert.Equal(["explicit-1"], recorder.Ids);
    }

    [Theory]
    [InlineData("abc-123", true)]
    [InlineData("0196f0c2e1b47a3c9d2e5f6a7b8c9d0e", true)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData("line\nbreak", false)]
    public void Only_short_safe_ids_are_accepted(string value, bool valid) =>
        Assert.Equal(valid, CorrelationId.IsValid(value));

    [Fact]
    public void Ids_longer_than_64_characters_are_refused() =>
        Assert.False(CorrelationId.IsValid(new string('a', 65)));
}
