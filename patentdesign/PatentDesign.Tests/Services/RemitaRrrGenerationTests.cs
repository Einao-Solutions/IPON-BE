using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using patentdesign.Models;
using patentdesign.Utils;
using Xunit;

namespace patentdesign.Tests.Services;

public class RemitaRrrGenerationTests
{
    private sealed class Handler(Func<CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return send(ct);
        }
    }

    private sealed class Factory(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, false);
    }

    private static PaymentUtils Create(Handler h, int timeoutSeconds = 5) =>
        new(Options.Create(new PaymentInfo { RemitaTimeoutSeconds = timeoutSeconds }),
            NullLogger<PaymentUtils>.Instance, new Factory(h));

    private static Task<string?> Generate(PaymentUtils p, CancellationToken ct = default) =>
        p.GenerateRemitaPaymentId("1000", "100", "svc", "d", "n", "e@x.com", "1", ct);

    private static HttpResponseMessage Ok(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body) };

    [Fact]
    public async Task Success_ReturnsRrr()
    {
        var h = new Handler(_ => Task.FromResult(Ok("jsonp ({\"RRR\":\"280007654321\",\"status\":\"00\"})")));
        Assert.Equal("280007654321", await Generate(Create(h)));
    }

    [Fact]
    public async Task NonSuccessStatus_ReturnsNull_WithoutRetry()
    {
        var h = new Handler(_ => Task.FromResult(Ok("{\"RRR\":\"123\"}", HttpStatusCode.InternalServerError)));
        Assert.Null(await Generate(Create(h)));
        Assert.Equal(1, h.Calls);
    }

    [Fact]
    public async Task MissingOrInvalidRrr_ReturnsNull()
    {
        Assert.Null(await Generate(Create(new Handler(_ => Task.FromResult(Ok("{\"statuscode\":\"021\"}"))))));
        Assert.Null(await Generate(Create(new Handler(_ => Task.FromResult(Ok("not json"))))));
    }

    [Fact]
    public async Task Timeout_ThrowsRemitaUnavailable()
    {
        var h = new Handler(async ct => { await Task.Delay(Timeout.Infinite, ct); return Ok("{}"); });
        await Assert.ThrowsAsync<RemitaUnavailableException>(() => Generate(Create(h, 1)));
        Assert.Equal(1, h.Calls);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        var h = new Handler(async ct => { await Task.Delay(Timeout.Infinite, ct); return Ok("{}"); });
        using var cts = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Generate(Create(h), cts.Token));
    }
}
