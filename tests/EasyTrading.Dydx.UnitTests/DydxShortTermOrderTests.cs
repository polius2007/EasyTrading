using System.Net;
using System.Text;
using System.Text.Json;
using Cosmos.Tx.V1Beta1;
using EasyTrading.Abstractions;
using EasyTrading.Dydx.Infrastructure;
using Google.Protobuf;
using MsgPlaceOrder = Dydxprotocol.Clob.MsgPlaceOrder;

namespace EasyTrading.Dydx.UnitTests;

public sealed class DydxShortTermOrderTests
{
    private const string TestMnemonic =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

    private const string TestAddress = "dydx19rl4cm2hmr8afy4kldpxz3fka4jguq0a4erelz";

    [Fact]
    public async Task GetLatestBlockHeightAsync_reads_the_validator_height()
    {
        using var handler = new DydxHandler();
        using var http = new HttpClient(handler);
        var cosmos = new CosmosClient(
            http,
            new Uri("https://validator.test/"),
            new DydxRetryOptions { MaxAttempts = 1 });

        var height = await cosmos.GetLatestBlockHeightAsync(default);

        Assert.Equal(1_000U, height);
        Assert.Contains("/cosmos/base/tendermint/v1beta1/blocks/latest", handler.Paths);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not-json")]
    [InlineData("""{"block":{}}""")]
    [InlineData("""{"block":{"header":{"height":"0"}}}""")]
    [InlineData("""{"block":{"header":{"height":"-1"}}}""")]
    [InlineData("""{"block":{"header":{"height":"4294967296"}}}""")]
    [InlineData("""{"block":{"header":{"height":"not-a-number"}}}""")]
    public async Task GetLatestBlockHeightAsync_rejects_an_invalid_height(string response)
    {
        using var handler = new DydxHandler { LatestBlockResponse = response };
        using var http = new HttpClient(handler);
        var cosmos = new CosmosClient(
            http,
            new Uri("https://validator.test/"),
            new DydxRetryOptions { MaxAttempts = 1 });

        await Assert.ThrowsAsync<ExchangeApiException>(
            () => cosmos.GetLatestBlockHeightAsync(default));
    }

    [Fact]
    public async Task PlaceLimitAsync_encodes_ioc_as_a_short_term_order()
    {
        using var handler = new DydxHandler();
        using var http = new HttpClient(handler);
        await using var client = CreateClient(http);

        await client.Orders.PlaceLimitAsync(
            "BTC-USD",
            OrderSide.Buy,
            60_000m,
            0.001m,
            TimeInForce.Ioc,
            reduceOnly: false,
            clientOrderId: "42");

        var message = ParseBroadcastPlaceOrder(handler);
        Assert.Equal(0U, message.Order.OrderId.OrderFlags);
        Assert.Equal(1_010U, message.Order.GoodTilBlock);
        Assert.Equal(0U, message.Order.GoodTilBlockTime);
        Assert.Equal(Dydxprotocol.Clob.Order.Types.TimeInForce.Ioc, message.Order.TimeInForce);
        Assert.Equal(42U, message.Order.OrderId.ClientId);
        Assert.Equal(1, handler.LatestBlockRequestCount);
    }

    [Fact]
    public async Task PlaceLimitAsync_keeps_gtc_as_a_long_term_order()
    {
        using var handler = new DydxHandler();
        using var http = new HttpClient(handler);
        await using var client = CreateClient(http);

        await client.Orders.PlaceLimitAsync(
            "BTC-USD",
            OrderSide.Sell,
            60_000m,
            0.001m,
            TimeInForce.Gtc,
            reduceOnly: false,
            clientOrderId: "43");

        var message = ParseBroadcastPlaceOrder(handler);
        Assert.Equal(64U, message.Order.OrderId.OrderFlags);
        Assert.Equal(0U, message.Order.GoodTilBlock);
        Assert.True(message.Order.GoodTilBlockTime > 0U);
        Assert.Equal(Dydxprotocol.Clob.Order.Types.TimeInForce.Unspecified, message.Order.TimeInForce);
        Assert.Equal(0, handler.LatestBlockRequestCount);
    }

    [Fact]
    public async Task PlaceLimitAsync_rejects_reduce_only_without_ioc()
    {
        using var handler = new DydxHandler();
        using var http = new HttpClient(handler);
        await using var client = CreateClient(http);

        await Assert.ThrowsAsync<InvalidOrderException>(() => client.Orders.PlaceLimitAsync(
            "BTC-USD",
            OrderSide.Sell,
            60_000m,
            0.001m,
            TimeInForce.Gtc,
            reduceOnly: true,
            clientOrderId: "44"));
    }

    [Fact]
    public async Task PlaceLimitAsync_rejects_fok_instead_of_silently_using_ioc()
    {
        using var handler = new DydxHandler();
        using var http = new HttpClient(handler);
        await using var client = CreateClient(http);

        await Assert.ThrowsAsync<InvalidOrderException>(() => client.Orders.PlaceLimitAsync(
            "BTC-USD",
            OrderSide.Buy,
            60_000m,
            0.001m,
            TimeInForce.Fok,
            reduceOnly: false,
            clientOrderId: "45"));
    }

    [Fact]
    public async Task PlaceLimitAsync_rejects_an_invalid_supplied_client_id()
    {
        using var handler = new DydxHandler();
        using var http = new HttpClient(handler);
        await using var client = CreateClient(http);

        await Assert.ThrowsAsync<InvalidOrderException>(() => client.Orders.PlaceLimitAsync(
            "BTC-USD",
            OrderSide.Buy,
            60_000m,
            0.001m,
            TimeInForce.Ioc,
            reduceOnly: false,
            clientOrderId: "not-numeric"));

        Assert.Null(handler.BroadcastBody);
    }

    private static DydxClient CreateClient(HttpClient http) => new(http, new DydxClientOptions
    {
        Network = DydxNetwork.Testnet,
        IndexerRestUrl = new Uri("https://indexer.test/v4/"),
        ValidatorRestUrl = new Uri("https://validator.test/"),
        RetryPolicy = new DydxRetryOptions { MaxAttempts = 1 },
        Credentials = new DydxCredentials(TestAddress, TestMnemonic),
    });

    private static MsgPlaceOrder ParseBroadcastPlaceOrder(DydxHandler handler)
    {
        Assert.NotNull(handler.BroadcastBody);
        using var json = JsonDocument.Parse(handler.BroadcastBody);
        var txBytes = Convert.FromBase64String(
            json.RootElement.GetProperty("tx_bytes").GetString()!);
        var tx = TxRaw.Parser.ParseFrom(txBytes);
        var body = TxBody.Parser.ParseFrom(tx.BodyBytes);
        var message = Assert.Single(body.Messages);
        Assert.Equal("/dydxprotocol.clob.MsgPlaceOrder", message.TypeUrl);
        return MsgPlaceOrder.Parser.ParseFrom(message.Value);
    }

    private sealed class DydxHandler : HttpMessageHandler
    {
        public string LatestBlockResponse { get; init; } =
            """{"block":{"header":{"height":"1000"}}}""";

        public List<string> Paths { get; } = [];
        public int LatestBlockRequestCount { get; private set; }
        public string? BroadcastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            Paths.Add(path);

            if (path == "/v4/perpetualMarkets")
            {
                return Json("""
                    {"markets":{"BTC-USD":{"clobPairId":"0","ticker":"BTC-USD","status":"ACTIVE","oraclePrice":"60000","atomicResolution":-10,"quantumConversionExponent":-9,"tickSize":"1","stepSize":"0.0001"}}}
                    """);
            }

            if (path == "/cosmos/base/tendermint/v1beta1/blocks/latest")
            {
                LatestBlockRequestCount++;
                return Json(LatestBlockResponse);
            }

            if (path.StartsWith("/cosmos/auth/v1beta1/accounts/", StringComparison.Ordinal))
            {
                return Json("""
                    {"account":{"@type":"/cosmos.auth.v1beta1.BaseAccount","address":"dydx19rl4cm2hmr8afy4kldpxz3fka4jguq0a4erelz","account_number":"1","sequence":"0"}}
                    """);
            }

            if (path == "/cosmos/tx/v1beta1/txs" && request.Method == HttpMethod.Post)
            {
                BroadcastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return Json("""
                    {"tx_response":{"code":0,"txhash":"ABC123","raw_log":"","height":"1001"}}
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
