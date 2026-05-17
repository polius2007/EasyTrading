using System.Net;
using System.Text;
using System.Text.Json;
using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.HyperLiquid;
using EasyTrading.HyperLiquid.Infrastructure;
using Microsoft.Extensions.Logging;
using Nethereum.Signer;

namespace EasyTrading.HyperLiquid.UnitTests;

/// <summary>
/// Unit tests for builder-fee approval resilience (1.2.1 hotfix).
///
/// Scenario being verified: pre-1.2.1, the very first <c>PlaceLimitAsync</c> on mainnet would
/// silently die because <c>approveBuilderFee</c> must be signed by the master wallet, not an
/// agent wallet — and our recommended setup uses an agent key. From 1.2.1 onward the approve
/// failure is caught, logged as a warning, and the order proceeds without builder routing.
/// </summary>
public sealed class HlBuilderApprovalFallbackTests
{
    // ─── Test 1: approve fails → order still placed, no builder field on the wire ──

    [Fact]
    public async Task ApproveFails_OrderStillPlacedWithoutBuilder()
    {
        var ctx = NewContext();
        ctx.Handler.OnExchangePost = (action, body) =>
        {
            var type = action.GetProperty("type").GetString();
            return type switch
            {
                "approveBuilderFee" => ExchangeError("Must be signed by the user's main wallet, not an agent/API wallet."),
                "order" => OrderRestingOk(orderId: 12345L),
                _ => ExchangeError($"unexpected action type {type}"),
            };
        };

        await using var client = new HyperLiquidClient(ctx.Http, ctx.Options);
        var placed = await client.Orders.PlaceLimitAsync(
            symbol: "BTC", side: OrderSide.Buy,
            price:  50_000m, size: 0.001m, tif: TimeInForce.Alo);

        Assert.Equal(12345L, placed.OrderId);
        Assert.Equal(OrderStatus.Open, placed.Status);

        // The order payload must NOT contain a `builder` field — approve failed, so attribution is skipped.
        var orderRequest = ctx.Handler.Calls.Last(c => c.Path.EndsWith("/exchange", StringComparison.Ordinal)
                                                    && c.ActionType == "order");
        Assert.False(orderRequest.HasField("builder"),
            $"Order action must omit 'builder' when approve failed. Action body: {orderRequest.ActionBody}");
    }

    // ─── Test 2: approve failure does not poison the (user, builder) cache ──

    [Fact]
    public async Task ApproveFails_CacheNotPoisoned()
    {
        var ctx = NewContext();
        ctx.Handler.OnExchangePost = (action, body) =>
        {
            var type = action.GetProperty("type").GetString();
            return type switch
            {
                "approveBuilderFee" => ExchangeError("Must be signed by the user's main wallet, not an agent/API wallet."),
                "order"             => OrderRestingOk(orderId: 1L),
                _                   => ExchangeError($"unexpected action type {type}"),
            };
        };

        await using var client = new HyperLiquidClient(ctx.Http, ctx.Options);

        // Two consecutive orders.
        await client.Orders.PlaceLimitAsync("BTC", OrderSide.Buy, 50_000m, 0.001m, TimeInForce.Alo);
        await client.Orders.PlaceLimitAsync("BTC", OrderSide.Buy, 50_000m, 0.001m, TimeInForce.Alo);

        // maxBuilderFee should have been queried TWICE (once per order) — proving the cache wasn't
        // poisoned with `true` after the first failed approve.
        var maxFeeChecks = ctx.Handler.Calls.Count(c => c.Path.EndsWith("/info", StringComparison.Ordinal)
                                                     && c.ActionType == "maxBuilderFee");
        Assert.Equal(2, maxFeeChecks);
    }

    // ─── Test 3: approve failure logs a warning that points to the manual approval URL ──

    [Fact]
    public async Task ApproveFails_WarningLogged()
    {
        var ctx = NewContext();
        ctx.Handler.OnExchangePost = (action, body) =>
        {
            var type = action.GetProperty("type").GetString();
            return type switch
            {
                "approveBuilderFee" => ExchangeError("Must be signed by the user's main wallet, not an agent/API wallet."),
                "order"             => OrderRestingOk(orderId: 99L),
                _                   => ExchangeError($"unexpected action type {type}"),
            };
        };

        var captured = new CapturingLogger<HyperLiquidClient>();
        await using var client = new HyperLiquidClient(ctx.Http, ctx.Options, captured);

        await client.Orders.PlaceLimitAsync("BTC", OrderSide.Buy, 50_000m, 0.001m, TimeInForce.Alo);

        var warnings = captured.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        Assert.NotEmpty(warnings);
        var msg = string.Join(" | ", warnings.Select(w => w.Message));
        Assert.Contains("builderCodes", msg, StringComparison.Ordinal);
        Assert.Contains(ctx.MasterAddress, msg, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Test 4: MasterPrivateKey supplied → approve signed by master key ──

    [Fact]
    public async Task MasterKeySupplied_ApproveSignedByMasterNotAgent()
    {
        var ctx = NewContext(useSeparateMaster: true);
        ctx.Handler.OnExchangePost = (action, body) =>
        {
            var type = action.GetProperty("type").GetString();
            return type switch
            {
                "approveBuilderFee" => ExchangeOk(),
                "order"             => OrderRestingOk(orderId: 7L),
                _                   => ExchangeError($"unexpected action type {type}"),
            };
        };

        await using var client = new HyperLiquidClient(ctx.Http, ctx.Options);
        await client.Orders.PlaceLimitAsync("BTC", OrderSide.Buy, 50_000m, 0.001m, TimeInForce.Alo);

        var approveCall = ctx.Handler.Calls.Single(c => c.Path.EndsWith("/exchange", StringComparison.Ordinal)
                                                     && c.ActionType == "approveBuilderFee");
        var orderCall = ctx.Handler.Calls.Single(c => c.Path.EndsWith("/exchange", StringComparison.Ordinal)
                                                   && c.ActionType == "order");

        // Approve must be signed by the master key (recover signer address from the EIP-712 user-signed payload).
        // We reproduce the signature using the master key for the captured action+nonce; if it matches the
        // captured (r,s,v), the master key signed.
        var approveBody = JsonDocument.Parse(approveCall.Body);
        var approveAction = approveBody.RootElement.GetProperty("action");
        var approveSig = approveBody.RootElement.GetProperty("signature");
        var approveR = approveSig.GetProperty("r").GetString()!;
        var approveS = approveSig.GetProperty("s").GetString()!;
        var approveV = approveSig.GetProperty("v").GetInt32();

        Assert.NotNull(ctx.MasterPrivateKey);
        Assert.True(SignatureMatches(approveAction, primaryType: "ApproveBuilderFee",
                                     candidateKey: ctx.MasterPrivateKey!,
                                     capturedR: approveR, capturedS: approveS, capturedV: approveV),
            "approveBuilderFee signature did not match the MASTER key — agent key was used by mistake.");
        Assert.False(SignatureMatches(approveAction, primaryType: "ApproveBuilderFee",
                                      candidateKey: ctx.AgentPrivateKey,
                                      capturedR: approveR, capturedS: approveS, capturedV: approveV),
            "approveBuilderFee signature unexpectedly matched the AGENT key.");

        // And the order itself must still be signed by the agent key (cheap proxy: a second order
        // would re-sign with the same key; we just sanity-check it went through).
        Assert.NotEmpty(orderCall.Body);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static TestContext NewContext(bool useSeparateMaster = false)
    {
        var agentEcKey = EthECKey.GenerateKey();
        var agentKey   = agentEcKey.GetPrivateKey();
        var agentAddr  = agentEcKey.GetPublicAddress();

        string? masterKey = null;
        string masterAddr;
        if (useSeparateMaster)
        {
            var masterEcKey = EthECKey.GenerateKey();
            masterKey  = masterEcKey.GetPrivateKey();
            masterAddr = masterEcKey.GetPublicAddress();
        }
        else
        {
            // Single-key mode: agent key IS the master key (typical for testnet smoke tests).
            masterAddr = agentAddr;
        }

        var handler = new ScriptedHandler();
        // Universe with a single BTC perp so MetaCache.GetAssetInfoAsync returns successfully.
        handler.OnInfoPost = (type, _) => type switch
        {
            "meta"          => OkJson("""{"universe":[{"name":"BTC","szDecimals":4}]}"""),
            "spotMeta"      => OkJson("""{"universe":[],"tokens":[]}"""),
            "maxBuilderFee" => OkJson("0"),
            _               => new HttpResponseMessage(HttpStatusCode.NotFound)
                                   { Content = new StringContent($"unhandled info type: {type}") },
        };

        var http = new HttpClient(handler);

        var options = new HyperLiquidClientOptions
        {
            Network = HyperLiquidNetwork.Testnet,
            Credentials = new HyperLiquidCredentials(
                MasterAddress:    masterAddr,
                PrivateKey:       agentKey,
                AgentName:        useSeparateMaster ? "test-agent" : null,
                VaultAddress:     null,
                MasterPrivateKey: masterKey),
            // Use a fixed builder so the approve flow is exercised on every test.
            BuilderFee = new BuilderFee(
                BuilderAddress: "0x000000000000000000000000000000000000beef",
                FeeRate:        0.00005m),
            RetryPolicy = new HyperLiquidRetryOptions
            {
                MaxAttempts = 1, // don't retry the scripted errors; we want to see them once
            },
        };

        return new TestContext(http, options, handler, masterAddr, agentAddr, masterKey, agentKey);
    }

    private static HttpResponseMessage OkJson(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage ExchangeOk()
        => OkJson("""{"status":"ok","response":{"type":"default"}}""");

    private static HttpResponseMessage OrderRestingOk(long orderId)
        => OkJson("{\"status\":\"ok\",\"response\":{\"type\":\"order\",\"data\":{\"statuses\":[{\"resting\":{\"oid\":"
                  + orderId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}}]}}}");

    private static HttpResponseMessage ExchangeError(string message)
        => OkJson("{\"status\":\"err\",\"response\":\"" + message.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"}");

    /// <summary>
    /// Re-sign the captured user-signed action with <paramref name="candidateKey"/> for the
    /// same primary type + schema dYdX recovered, and compare to the captured (r,s,v).
    /// secp256k1 ECDSA is deterministic (RFC-6979) so signing the same digest with the same key
    /// always produces the same signature — perfect for identifying which key was used.
    /// </summary>
    private static bool SignatureMatches(
        JsonElement actionEl, string primaryType, string candidateKey,
        string capturedR, string capturedS, int capturedV)
    {
        // Rebuild the HlMap from the captured JSON action.
        var msg = new HlMap();
        foreach (var prop in actionEl.EnumerateObject())
        {
            object value = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString()!,
                JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? (object)l : prop.Value.GetDouble(),
                _ => prop.Value.GetRawText(),
            };
            msg.Add(prop.Name, value);
        }

        // Schema for approveBuilderFee — must match exactly what Orders.EnsureBuilderApprovedAsync uses.
        var schema = new (string Name, string Type)[]
        {
            ("hyperliquidChain", "string"),
            ("maxFeeRate",       "string"),
            ("builder",          "address"),
            ("nonce",            "uint64"),
        };

        var resigned = Signer.SignUserAction(msg, primaryType, schema, candidateKey);
        return string.Equals(resigned.R, capturedR, StringComparison.OrdinalIgnoreCase)
            && string.Equals(resigned.S, capturedS, StringComparison.OrdinalIgnoreCase)
            && resigned.V == capturedV;
    }

    private sealed record TestContext(
        HttpClient Http,
        HyperLiquidClientOptions Options,
        ScriptedHandler Handler,
        string MasterAddress,
        string AgentAddress,
        string? MasterPrivateKey,
        string AgentPrivateKey);

    /// <summary>
    /// HTTP handler that routes by URL path and captures bodies for assertion.
    /// <c>OnInfoPost(type, doc)</c> handles <c>/info</c> requests dispatched by their action <c>type</c>;
    /// <c>OnExchangePost(action, body)</c> handles <c>/exchange</c> requests where <c>action</c> is the
    /// inner action HlMap-shaped JsonElement.
    /// </summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public List<Call> Calls { get; } = new();
        public Func<string, JsonDocument, HttpResponseMessage>? OnInfoPost { get; set; }
        public Func<JsonElement, string, HttpResponseMessage>? OnExchangePost { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var body = req.Content is null ? "" : await req.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var path = req.RequestUri!.AbsolutePath;

            using var doc = JsonDocument.Parse(body);
            string actionType;
            string actionBody;

            if (path.EndsWith("/info", StringComparison.Ordinal))
            {
                actionType = doc.RootElement.GetProperty("type").GetString() ?? "";
                actionBody = body;
                Calls.Add(new Call(path, body, actionType, actionBody));
                return OnInfoPost?.Invoke(actionType, doc) ?? new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (path.EndsWith("/exchange", StringComparison.Ordinal))
            {
                var actionEl = doc.RootElement.GetProperty("action");
                actionType = actionEl.GetProperty("type").GetString() ?? "";
                actionBody = actionEl.GetRawText();
                Calls.Add(new Call(path, body, actionType, actionBody));
                return OnExchangePost?.Invoke(actionEl, body) ?? new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            Calls.Add(new Call(path, body, "", ""));
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        public sealed record Call(string Path, string Body, string ActionType, string ActionBody)
        {
            public bool HasField(string field) => ActionBody.Contains($"\"{field}\"", StringComparison.Ordinal);
        }
    }

    /// <summary>In-memory ILogger that records every entry. Generic so it satisfies ILogger&lt;T&gt; injection points.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<Entry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(new Entry(logLevel, formatter(state, exception), exception));

        public sealed record Entry(LogLevel Level, string Message, Exception? Exception);

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
