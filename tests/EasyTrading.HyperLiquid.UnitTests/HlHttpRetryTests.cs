using System.Net;
using System.Net.Http.Headers;
using EasyTrading.HyperLiquid;
using EasyTrading.HyperLiquid.Infrastructure;

namespace EasyTrading.HyperLiquid.UnitTests;

/// <summary>
/// Behaviour tests for the shared retry layer. Driven by a stub <see cref="HttpMessageHandler"/>
/// that returns a scripted sequence of statuses / exceptions per call.
/// </summary>
public sealed class HlHttpRetryTests
{
    private static readonly Uri TestUri = new("https://test.invalid/info");

    private static HyperLiquidRetryOptions FastRetry(int attempts = 3) => new()
    {
        MaxAttempts = attempts,
        InitialDelay = TimeSpan.FromMilliseconds(1),
        MaxDelay = TimeSpan.FromMilliseconds(5),
        BackoffMultiplier = 1.0, // no exponential growth in tests — keep them fast
    };

    [Fact]
    public async Task First_attempt_2xx_is_returned_without_retry()
    {
        var handler = new SequencedHandler(
            HandlerStep.Ok("hello"));
        using var client = new HttpClient(handler);

        var result = await HlHttp.PostJsonAsync(client, TestUri, "{}", FastRetry(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("hello", result.Body);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Network_error_is_retried_then_succeeds()
    {
        var handler = new SequencedHandler(
            HandlerStep.Throw(new HttpRequestException("DNS failure")),
            HandlerStep.Throw(new HttpRequestException("connection refused")),
            HandlerStep.Ok("recovered"));
        using var client = new HttpClient(handler);

        var result = await HlHttp.PostJsonAsync(client, TestUri, "{}", FastRetry(attempts: 3), CancellationToken.None);

        Assert.Equal("recovered", result.Body);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task Persistent_network_error_throws_after_max_attempts()
    {
        var handler = new SequencedHandler(
            HandlerStep.Throw(new HttpRequestException("e1")),
            HandlerStep.Throw(new HttpRequestException("e2")));
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            HlHttp.PostJsonAsync(client, TestUri, "{}", FastRetry(attempts: 2), CancellationToken.None));

        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Server_5xx_is_retried_then_succeeds()
    {
        var handler = new SequencedHandler(
            HandlerStep.WithStatus(HttpStatusCode.InternalServerError, "oops"),
            HandlerStep.Ok("recovered"));
        using var client = new HttpClient(handler);

        var result = await HlHttp.PostJsonAsync(client, TestUri, "{}", FastRetry(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal("recovered", result.Body);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Server_5xx_with_retry_disabled_returns_immediately()
    {
        var handler = new SequencedHandler(
            HandlerStep.WithStatus(HttpStatusCode.BadGateway, "down"));
        using var client = new HttpClient(handler);

        var retry = FastRetry(attempts: 3);
        retry.RetryOnServerError = false;

        var result = await HlHttp.PostJsonAsync(client, TestUri, "{}", retry, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadGateway, result.StatusCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Client_4xx_is_not_retried()
    {
        var handler = new SequencedHandler(
            HandlerStep.WithStatus(HttpStatusCode.BadRequest, "bad"));
        using var client = new HttpClient(handler);

        var result = await HlHttp.PostJsonAsync(client, TestUri, "{}", FastRetry(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Rate_limit_with_retry_after_is_respected()
    {
        var step429 = HandlerStep.WithStatus(HttpStatusCode.TooManyRequests, "slow down");
        step429.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(2));

        var handler = new SequencedHandler(
            step429,
            HandlerStep.Ok("recovered"));
        using var client = new HttpClient(handler);

        var result = await HlHttp.PostJsonAsync(client, TestUri, "{}", FastRetry(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task User_cancellation_is_not_swallowed_as_a_retryable_timeout()
    {
        var handler = new SequencedHandler(HandlerStep.Ok("never reached"));
        using var client = new HttpClient(handler);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            HlHttp.PostJsonAsync(client, TestUri, "{}", FastRetry(), cts.Token));
    }

    // ─── fake handler ────────────────────────────────────────────────────────

    private sealed class SequencedHandler(params HandlerStep[] steps) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Yield(); // suppress CS1998
            var index = CallCount;
            CallCount++;

            if (index >= steps.Length)
                throw new InvalidOperationException($"SequencedHandler ran out of scripted steps after {CallCount} call(s).");

            var step = steps[index];
            if (step.Exception is not null)
                throw step.Exception;

            var response = new HttpResponseMessage(step.Status!.Value)
            {
                Content = new StringContent(step.Body ?? ""),
                ReasonPhrase = step.Body,
            };
            if (step.RetryAfter is not null)
                response.Headers.RetryAfter = step.RetryAfter;
            return response;
        }
    }

    private sealed class HandlerStep
    {
        public HttpStatusCode? Status { get; init; }
        public string? Body { get; init; }
        public Exception? Exception { get; init; }
        public RetryConditionHeaderValue? RetryAfter { get; set; }

        public static HandlerStep Ok(string body) => new() { Status = HttpStatusCode.OK, Body = body };
        public static HandlerStep WithStatus(HttpStatusCode code, string body) => new() { Status = code, Body = body };
        public static HandlerStep Throw(Exception ex) => new() { Exception = ex };
    }
}
