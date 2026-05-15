using System.Globalization;
using System.Text.Json;
using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.Dydx.Infrastructure;

namespace EasyTrading.Dydx.Modules;

/// <summary>
/// dYdX implementation of <see cref="IStreams"/>. Public market streams (trades / orderbook /
/// candles) work over the Indexer WebSocket. User-scoped streams (MyOrders / MyFills /
/// MyFundings) subscribe to the <c>v4_subaccounts</c> channel and require a populated
/// <see cref="DydxCredentials"/> on the client.
/// </summary>
internal sealed class Streams(WebSocketClient ws, DydxClientOptions options) : IStreams
{
    public IAsyncEnumerable<TradeUpdate> TradesAsync(string symbol, CancellationToken ct)
        => ws.SubscribeAsync<TradeUpdate>(
            subscribePayload: new { channel = "v4_trades", id = symbol },
            routingKey:       $"v4_trades:{symbol}",
            parser:           data => ParseTrades(data, symbol),
            ct:               ct);

    public IAsyncEnumerable<OrderBookUpdate> OrderBookAsync(string symbol, int depth = 20, CancellationToken ct = default)
        => ws.SubscribeAsync<OrderBookUpdate>(
            subscribePayload: new { channel = "v4_orderbook", id = symbol },
            routingKey:       $"v4_orderbook:{symbol}",
            parser:           data => ParseOrderBook(data, symbol, depth),
            ct:               ct);

    public IAsyncEnumerable<CandleUpdate> CandlesAsync(string symbol, Interval interval, CancellationToken ct = default)
    {
        var res = Mapper.ResolutionWire(interval);
        var id = $"{symbol}/{res}";
        return ws.SubscribeAsync<CandleUpdate>(
            subscribePayload: new { channel = "v4_candles", id },
            routingKey:       $"v4_candles:{id}",
            parser:           data => ParseCandle(data),
            ct:               ct);
    }

    public IAsyncEnumerable<MidUpdate> AllMidsAsync(CancellationToken ct)
        => ws.SubscribeAsync<MidUpdate>(
            subscribePayload: new { channel = "v4_markets" },
            routingKey:       "v4_markets",
            parser:           ParseMarketsAsMids,
            ct:               ct);

    public IAsyncEnumerable<BboUpdate> BestBidOfferAsync(string symbol, CancellationToken ct)
    {
        // dYdX's Indexer doesn't expose a dedicated BBO channel; the orderbook channel carries
        // the top-of-book via its incremental updates. We derive BBO client-side.
        return DeriveBboAsync(symbol, ct);
    }

    private async IAsyncEnumerable<BboUpdate> DeriveBboAsync(string symbol,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // Track running top-of-book from orderbook updates.
        decimal bestBidPx = 0m, bestAskPx = 0m, bestBidSz = 0m, bestAskSz = 0m;

        await foreach (var update in OrderBookAsync(symbol, depth: 1, ct))
        {
            if (update.Bids.Count > 0)
            {
                bestBidPx = update.Bids[0].Price;
                bestBidSz = update.Bids[0].Size;
            }
            if (update.Asks.Count > 0)
            {
                bestAskPx = update.Asks[0].Price;
                bestAskSz = update.Asks[0].Size;
            }
            if (bestBidPx > 0 && bestAskPx > 0)
                yield return new BboUpdate(symbol, bestBidPx, bestBidSz, bestAskPx, bestAskSz, DateTimeOffset.UtcNow);
        }
    }

    // ─── user streams (subaccount channel — address-bound, no signing) ──────

    public IAsyncEnumerable<OrderUpdate> MyOrdersAsync(CancellationToken ct)
    {
        var creds = RequireCreds();
        var id = $"{creds.Address}/{creds.SubaccountNumber}";
        return ws.SubscribeAsync<OrderUpdate>(
            subscribePayload: new { channel = "v4_subaccounts", id },
            routingKey:       $"v4_subaccounts:{id}",
            parser:           ParseSubaccountOrders,
            ct:               ct);
    }

    public IAsyncEnumerable<FillUpdate> MyFillsAsync(CancellationToken ct)
    {
        var creds = RequireCreds();
        var id = $"{creds.Address}/{creds.SubaccountNumber}";
        return ws.SubscribeAsync<FillUpdate>(
            subscribePayload: new { channel = "v4_subaccounts", id },
            routingKey:       $"v4_subaccounts:{id}",
            parser:           ParseSubaccountFills,
            ct:               ct);
    }

    public IAsyncEnumerable<FundingUpdate> MyFundingsAsync(CancellationToken ct)
    {
        // dYdX surfaces funding settlements via the subaccount channel's `fundingPayments` field
        // on the periodic snapshot. We surface them as FundingUpdate.
        var creds = RequireCreds();
        var id = $"{creds.Address}/{creds.SubaccountNumber}";
        return ws.SubscribeAsync<FundingUpdate>(
            subscribePayload: new { channel = "v4_subaccounts", id },
            routingKey:       $"v4_subaccounts:{id}",
            parser:           ParseSubaccountFundings,
            ct:               ct);
    }

    public IAsyncEnumerable<NotificationUpdate> MyNotificationsAsync(CancellationToken ct)
    {
        // dYdX doesn't carry a discrete notification event; status of liquidations / margin-calls
        // is implicit in subaccount updates. Return an empty enumerator so callers can compose.
        return EmptyAsync(ct);
    }

    private static async IAsyncEnumerable<NotificationUpdate> EmptyAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        try { await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { /* expected */ }
        yield break;
    }

    private DydxCredentials RequireCreds() => options.Credentials
        ?? throw new AuthenticationException(
            "DydxClientOptions.Credentials.Address is required for user-scoped streams.");

    // ─── parsers (one per channel) ───────────────────────────────────────────

    private static IEnumerable<TradeUpdate> ParseTrades(JsonElement data, string symbol)
    {
        // v4_trades contents: { trades: [ {id, side, size, price, createdAt, ...} ] }
        if (!data.TryGetProperty("trades", out var arr) || arr.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var t in arr.EnumerateArray())
        {
            var raw = t.Deserialize<PublicTradeRaw>(JsonOptions.Default);
            if (raw is null) continue;
            yield return new TradeUpdate(Mapper.MapTrade(symbol, raw));
        }
    }

    private static IEnumerable<OrderBookUpdate> ParseOrderBook(JsonElement data, string symbol, int depth)
    {
        // Initial subscribed payload is the full book; subsequent channel_data carries diffs
        // ("bids"/"asks" arrays of [price, size] where size=0 removes the level). We emit
        // every message as a non-snapshot update; downstream consumers can maintain state.
        var bids = ExtractLevels(data, "bids", depth);
        var asks = ExtractLevels(data, "asks", depth);
        yield return new OrderBookUpdate(symbol, DateTimeOffset.UtcNow, bids, asks, IsSnapshot: false);
    }

    private static List<OrderBookLevel> ExtractLevels(JsonElement msg, string property, int depth)
    {
        var result = new List<OrderBookLevel>();
        if (!msg.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return result;

        var i = 0;
        foreach (var level in arr.EnumerateArray())
        {
            if (i++ >= depth) break;

            // dYdX uses two shapes: array [price, size] for diff updates, object
            // {price, size} for snapshots. Handle both.
            decimal px = 0m, sz = 0m;
            if (level.ValueKind == JsonValueKind.Array && level.GetArrayLength() >= 2)
            {
                px = decimal.Parse(level[0].GetString() ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture);
                sz = decimal.Parse(level[1].GetString() ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture);
            }
            else if (level.ValueKind == JsonValueKind.Object)
            {
                if (level.TryGetProperty("price", out var pEl))
                    px = decimal.Parse(pEl.GetString() ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture);
                if (level.TryGetProperty("size", out var sEl))
                    sz = decimal.Parse(sEl.GetString() ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture);
            }
            else continue;

            result.Add(new OrderBookLevel(px, sz, OrderCount: 0));
        }
        return result;
    }

    private static IEnumerable<CandleUpdate> ParseCandle(JsonElement data)
    {
        // v4_candles contents object can be either {candles: [...]} (initial) or a single candle
        // ({ticker, resolution, ..., startedAt, ...}) on updates. Handle both.
        if (data.TryGetProperty("candles", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in arr.EnumerateArray())
            {
                var raw = c.Deserialize<CandleRaw>(JsonOptions.Default);
                if (raw is null) continue;
                yield return new CandleUpdate(Mapper.MapCandle(raw));
            }
        }
        else if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("startedAt", out _))
        {
            var raw = data.Deserialize<CandleRaw>(JsonOptions.Default);
            if (raw is not null)
                yield return new CandleUpdate(Mapper.MapCandle(raw));
        }
    }

    private static IEnumerable<OrderUpdate> ParseSubaccountOrders(JsonElement data)
    {
        // v4_subaccounts contents: { orders?: [...], fills?: [...], subaccount?: {...},
        //   perpetualPositions?: [...], assetPositions?: [...], fundingPayments?: [...] }.
        // We yield OrderUpdate entries for every element in `orders`.
        if (!data.TryGetProperty("orders", out var arr) || arr.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var o in arr.EnumerateArray())
        {
            var raw = o.Deserialize<OrderRaw>(JsonOptions.Default);
            if (raw is null) continue;
            yield return new OrderUpdate(Mapper.MapOrder(raw));
        }
    }

    private static IEnumerable<FillUpdate> ParseSubaccountFills(JsonElement data)
    {
        if (!data.TryGetProperty("fills", out var arr) || arr.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var f in arr.EnumerateArray())
        {
            var raw = f.Deserialize<UserFillRaw>(JsonOptions.Default);
            if (raw is null) continue;
            yield return new FillUpdate(Mapper.MapFill(raw));
        }
    }

    private static IEnumerable<FundingUpdate> ParseSubaccountFundings(JsonElement data)
    {
        if (!data.TryGetProperty("fundingPayments", out var arr) || arr.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var f in arr.EnumerateArray())
        {
            // dYdX funding payment shape (subaccount channel):
            //   { ticker, payment, rate, oraclePrice, size, side, createdAt, ... }
            if (!f.TryGetProperty("ticker", out var tEl)) continue;
            var ticker = tEl.GetString() ?? string.Empty;

            var amount = f.TryGetProperty("payment", out var p) ? p.GetString() : "0";
            var rate   = f.TryGetProperty("rate", out var r) ? r.GetString() : "0";
            var time   = f.TryGetProperty("createdAt", out var cEl) ? cEl.GetString() : null;

            yield return new FundingUpdate(
                Symbol: ticker,
                Amount: decimal.Parse(amount ?? "0", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture),
                Rate:   decimal.Parse(rate ?? "0", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture),
                Time:   time is null ? DateTimeOffset.UtcNow : Mapper.ParseIso(time));
        }
    }

    private static IEnumerable<MidUpdate> ParseMarketsAsMids(JsonElement data)
    {
        // v4_markets payloads are {markets: {ticker: {oraclePrice, ...}}} or single-market diffs.
        if (!data.TryGetProperty("markets", out var marketsEl) || marketsEl.ValueKind != JsonValueKind.Object)
            yield break;

        var now = DateTimeOffset.UtcNow;
        foreach (var prop in marketsEl.EnumerateObject())
        {
            if (!prop.Value.TryGetProperty("oraclePrice", out var pxEl)) continue;
            if (pxEl.ValueKind != JsonValueKind.String) continue;
            var pxStr = pxEl.GetString();
            if (string.IsNullOrEmpty(pxStr)) continue;
            yield return new MidUpdate(prop.Name, decimal.Parse(pxStr, NumberStyles.Float, CultureInfo.InvariantCulture), now);
        }
    }
}
