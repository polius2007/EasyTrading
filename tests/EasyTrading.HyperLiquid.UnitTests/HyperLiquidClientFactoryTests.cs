using System.Net;
using System.Text;
using System.Text.Json;
using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.HyperLiquid;
using Microsoft.Extensions.DependencyInjection;

namespace EasyTrading.HyperLiquid.UnitTests;

public sealed class HyperLiquidClientFactoryTests
{
    private const string TestPrivateKey =
        "0x1111111111111111111111111111111111111111111111111111111111111111";

    [Fact]
    public async Task Parameterless_registration_adds_factory_without_default_client()
    {
        var services = new ServiceCollection();
        services.AddEasyTrading().AddHyperLiquid();

        await using var provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<IHyperLiquidMetadataCatalog>(),
            provider.GetRequiredService<IHyperLiquidMetadataCatalog>());
        Assert.Same(
            provider.GetRequiredService<IHyperLiquidClientFactory>(),
            provider.GetRequiredService<IHyperLiquidClientFactory>());
        Assert.Null(provider.GetService<HyperLiquidClient>());
        Assert.Null(provider.GetService<IHyperLiquidExchange>());
    }

    [Fact]
    public async Task Factory_creates_distinct_clients_without_mutating_source_options()
    {
        using var catalog = new HyperLiquidMetadataCatalog(new HttpClient(new MetadataHandler()));
        var factory = new HyperLiquidClientFactory(catalog);
        var options = new HyperLiquidClientOptions
        {
            Network = HyperLiquidNetwork.Testnet,
        };
        var firstCredentials = Credentials("0x1111111111111111111111111111111111111111");
        var secondCredentials = Credentials("0x2222222222222222222222222222222222222222");

        await using var first = factory.Create(firstCredentials, options);
        await using var second = factory.Create(secondCredentials, options);

        Assert.NotSame(first, second);
        Assert.Same(firstCredentials, first.Options.Credentials);
        Assert.Same(secondCredentials, second.Options.Credentials);
        Assert.Null(options.Credentials);
    }

    [Fact]
    public async Task Concurrent_warmup_loads_each_metadata_document_once()
    {
        var handler = new MetadataHandler();
        using var catalog = new HyperLiquidMetadataCatalog(new HttpClient(handler));
        var scope = new HyperLiquidMetadataScope(HyperLiquidNetwork.Mainnet);

        await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => catalog.WarmUpAsync(scope)));

        Assert.Equal(1, handler.MetaRequests);
        Assert.Equal(1, handler.SpotMetaRequests);
        Assert.True(catalog.TryGetAsset(scope, "btc", out var btc));
        Assert.Equal(0, btc.AssetId);
        Assert.Equal(4, btc.SzDecimals);
        Assert.False(btc.IsSpot);
        Assert.True(catalog.TryGetAsset(scope, "PURR/USDC", out var purr));
        Assert.Equal(10_000, purr.AssetId);
        Assert.True(purr.IsSpot);
    }

    [Fact]
    public async Task Factory_client_fails_before_http_when_catalog_is_not_warmed()
    {
        var handler = new CountingHandler();
        using var http = new HttpClient(handler);
        using var catalog = new HyperLiquidMetadataCatalog(new HttpClient(new MetadataHandler()));
        var factory = new HyperLiquidClientFactory(catalog);
        await using var client = factory.Create(
            http,
            Credentials("0x1111111111111111111111111111111111111111"),
            new HyperLiquidClientOptions
            {
                BuilderFee = new BuilderFee(
                    "0x0000000000000000000000000000000000000000",
                    0m),
            });

        var error = await Assert.ThrowsAsync<ExchangeApiException>(
            () => client.Orders.PlaceLimitAsync(
                "BTC",
                OrderSide.Buy,
                price: 10_000m,
                size: 0.001m));

        Assert.Contains("not loaded", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task Warmed_catalog_prevents_metadata_requests_from_factory_client()
    {
        var metadataHandler = new MetadataHandler();
        using var catalog = new HyperLiquidMetadataCatalog(new HttpClient(metadataHandler));
        var scope = new HyperLiquidMetadataScope(HyperLiquidNetwork.Mainnet);
        await catalog.WarmUpAsync(scope);

        var tradingHandler = new CountingHandler(
            """{"status":"ok","response":{"type":"order","data":{"statuses":[{"resting":{"oid":42}}]}}}""");
        using var tradingHttp = new HttpClient(tradingHandler);
        var factory = new HyperLiquidClientFactory(catalog);
        await using var client = factory.Create(
            tradingHttp,
            Credentials("0x1111111111111111111111111111111111111111"),
            new HyperLiquidClientOptions
            {
                BuilderFee = new BuilderFee(
                    "0x0000000000000000000000000000000000000000",
                    0m),
            });

        var result = await client.Orders.PlaceLimitAsync(
            "BTC",
            OrderSide.Buy,
            price: 10_000m,
            size: 0.001m);

        Assert.Equal(42, result.OrderId);
        Assert.Equal(1, tradingHandler.Requests);
        Assert.Equal("/exchange", tradingHandler.Paths.Single());
        Assert.Equal(1, metadataHandler.MetaRequests);
        Assert.Equal(1, metadataHandler.SpotMetaRequests);
    }

    private static HyperLiquidCredentials Credentials(string address)
        => new(address, TestPrivateKey, AgentName: "factory-test");

    private sealed class MetadataHandler : HttpMessageHandler
    {
        private int _metaRequests;
        private int _spotMetaRequests;

        public int MetaRequests => Volatile.Read(ref _metaRequests);

        public int SpotMetaRequests => Volatile.Read(ref _spotMetaRequests);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var type = document.RootElement.GetProperty("type").GetString();

            return type switch
            {
                "meta" => Json(
                    """{"universe":[{"name":"BTC","szDecimals":4,"maxLeverage":50}]}""",
                    Interlocked.Increment(ref _metaRequests)),
                "spotMeta" => Json(
                    """
                    {
                      "tokens": [
                        {
                          "name": "USDC",
                          "szDecimals": 6,
                          "weiDecimals": 6,
                          "index": 0,
                          "tokenId": "0x0",
                          "isCanonical": true
                        },
                        {
                          "name": "PURR",
                          "szDecimals": 2,
                          "weiDecimals": 5,
                          "index": 1,
                          "tokenId": "0x1",
                          "isCanonical": true
                        }
                      ],
                      "universe": [
                        {
                          "name": "PURR/USDC",
                          "tokens": [1, 0],
                          "index": 0,
                          "isCanonical": true
                        }
                      ]
                    }
                    """,
                    Interlocked.Increment(ref _spotMetaRequests)),
                _ => new HttpResponseMessage(HttpStatusCode.BadRequest),
            };
        }

        private static HttpResponseMessage Json(string body, int _)
            => new(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
    }

    private sealed class CountingHandler(string? response = null) : HttpMessageHandler
    {
        private int _requests;
        private readonly object _pathsLock = new();

        public int Requests => Volatile.Read(ref _requests);

        public List<string> Paths { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            lock (_pathsLock)
                Paths.Add(request.RequestUri!.AbsolutePath);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    response ?? "{}",
                    Encoding.UTF8,
                    "application/json"),
            });
        }
    }
}
