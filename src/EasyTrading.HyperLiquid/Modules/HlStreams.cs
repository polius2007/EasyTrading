using System.Globalization;
using System.Text.Json;
using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.HyperLiquid.Infrastructure;

namespace EasyTrading.HyperLiquid.Modules;

/// <summary>HyperLiquid implementation of <see cref="IStreams"/>. Multiplexes all subscriptions over a single shared <see cref="HlWebSocketClient"/>.</summary>
internal sealed class HlStreams(HlWebSocketClient ws, HyperLiquidClientOptions options) : IStreams
{
    // ─── Public channels ─────────────────────────────────────────────────────

    public IAsyncEnumerable<TradeUpdate> TradesAsync(string symbol, CancellationToken ct)
        => ws.SubscribeAsync<TradeUpdate>(
            subscribePayload: new HlMap().Add("type", "trades").Add("coin", symbol),
            subscriptionKey:  $"trades:{symbol}",
            parser:           ParseTrades,
            ct:               ct);

    public IAsyncEnumerable<OrderBookUpdate> OrderBookAsync(string symbol, int depth = 20, CancellationToken ct = default)
        => ws.SubscribeAsync<OrderBookUpdate>(
            subscribePayload: new HlMap().Add("type", "l2Book").Add("coin", symbol),
            subscriptionKey:  $"l2Book:{symbol}",
            parser:           data => ParseOrderBook(data, depth),
            ct:               ct);

    public IAsyncEnumerable<CandleUpdate> CandlesAsync(string symbol, Interval interval, CancellationToken ct = default)
        => ws.SubscribeAsync<CandleUpdate>(
            subscribePayload: new HlMap().Add("type", "candle").Add("coin", symbol).Add("interval", HlMapper.SerializeInterval(interval)),
            subscriptionKey:  $"candle:{symbol}:{HlMapper.SerializeInterval(interval)}",
            parser:           ParseCandle,
            ct:               ct);

    public IAsyncEnumerable<MidUpdate> AllMidsAsync(CancellationToken ct)
        => ws.SubscribeAsync<MidUpdate>(
            subscribePayload: new HlMap().Add("type", "allMids"),
            subscriptionKey:  "allMids",
            parser:           ParseAllMids,
            ct:               ct);

    public IAsyncEnumerable<BboUpdate> BestBidOfferAsync(string symbol, CancellationToken ct)
        => ws.SubscribeAsync<BboUpdate>(
            subscribePayload: new HlMap().Add("type", "bbo").Add("coin", symbol),
            subscriptionKey:  $"bbo:{symbol}",
            parser:           ParseBbo,
            ct:               ct);

    // ─── User-scoped channels (credentials taken from the client) ────────────

    public IAsyncEnumerable<OrderUpdate> MyOrdersAsync(CancellationToken ct)
    {
        var user = RequireUser();
        return ws.SubscribeAsync<OrderUpdate>(
            subscribePayload: new HlMap().Add("type", "orderUpdates").Add("user", user),
            subscriptionKey:  "orderUpdates",
            parser:           ParseOrderUpdates,
            ct:               ct);
    }

    public IAsyncEnumerable<FillUpdate> MyFillsAsync(CancellationToken ct)
    {
        var user = RequireUser();
        return ws.SubscribeAsync<FillUpdate>(
            subscribePayload: new HlMap().Add("type", "userFills").Add("user", user),
            subscriptionKey:  "userFills",
            parser:           ParseUserFills,
            ct:               ct);
    }

    public IAsyncEnumerable<FundingUpdate> MyFundingsAsync(CancellationToken ct)
    {
        var user = RequireUser();
        return ws.SubscribeAsync<FundingUpdate>(
            subscribePayload: new HlMap().Add("type", "userFundings").Add("user", user),
            subscriptionKey:  "userFundings",
            parser:           ParseUserFundings,
            ct:               ct);
    }

    public IAsyncEnumerable<NotificationUpdate> MyNotificationsAsync(CancellationToken ct)
    {
        var user = RequireUser();
        return ws.SubscribeAsync<NotificationUpdate>(
            subscribePayload: new HlMap().Add("type", "notification").Add("user", user),
            subscriptionKey:  "notification",
            parser:           ParseNotifications,
            ct:               ct);
    }

    // ─── Parsers (one per channel; called inside the WS dispatcher) ──────────

    private static IEnumerable<TradeUpdate> ParseTrades(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Array) yield break;
        foreach (var t in data.EnumerateArray())
        {
            var trade = new PublicTrade(
                Symbol:  t.GetProperty("coin").GetString()!,
                Price:   ParseDec(t.GetProperty("px").GetString()!),
                Size:    ParseDec(t.GetProperty("sz").GetString()!),
                Side:    HlMapper.ParseSide(t.GetProperty("side").GetString()!),
                Time:    HlMapper.T(t.GetProperty("time").GetInt64()),
                TradeId: t.GetProperty("tid").GetInt64());
            yield return new TradeUpdate(trade);
        }
    }

    private static IEnumerable<OrderBookUpdate> ParseOrderBook(JsonElement data, int depth)
    {
        var coin = data.GetProperty("coin").GetString() ?? string.Empty;
        var time = HlMapper.T(data.GetProperty("time").GetInt64());
        var levels = data.GetProperty("levels");
        var bids = ExtractLevels(levels[0], depth);
        var asks = levels.GetArrayLength() > 1 ? ExtractLevels(levels[1], depth) : new List<OrderBookLevel>();
        yield return new OrderBookUpdate(coin, time, bids, asks, IsSnapshot: true);
    }

    private static List<OrderBookLevel> ExtractLevels(JsonElement side, int depth)
    {
        var result = new List<OrderBookLevel>(Math.Min(depth, 20));
        var i = 0;
        foreach (var lvl in side.EnumerateArray())
        {
            if (i++ >= depth) break;
            result.Add(new OrderBookLevel(
                Price: ParseDec(lvl.GetProperty("px").GetString()!),
                Size:  ParseDec(lvl.GetProperty("sz").GetString()!),
                OrderCount: lvl.TryGetProperty("n", out var n) ? n.GetInt32() : 0));
        }
        return result;
    }

    private static IEnumerable<CandleUpdate> ParseCandle(JsonElement data)
    {
        // single-candle object
        var candle = new Candle(
            Symbol:     data.GetProperty("s").GetString()!,
            Interval:   HlMapper.ParseInterval(data.GetProperty("i").GetString()!),
            OpenTime:   HlMapper.T(data.GetProperty("t").GetInt64()),
            CloseTime:  HlMapper.T(data.GetProperty("T").GetInt64()),
            Open:       ParseDec(data.GetProperty("o").GetString()!),
            High:       ParseDec(data.GetProperty("h").GetString()!),
            Low:        ParseDec(data.GetProperty("l").GetString()!),
            Close:      ParseDec(data.GetProperty("c").GetString()!),
            Volume:     ParseDec(data.GetProperty("v").GetString()!),
            TradeCount: data.GetProperty("n").GetInt32());
        yield return new CandleUpdate(candle);
    }

    private static IEnumerable<MidUpdate> ParseAllMids(JsonElement data)
    {
        // HL pushes `{ "mids": { "BTC": "60000", "ETH": "3000", … } }` — but some snapshots have the
        // mids inlined at the top level. Handle both shapes defensively.
        var mids = data.TryGetProperty("mids", out var nested) ? nested : data;
        if (mids.ValueKind != JsonValueKind.Object) yield break;

        var now = DateTimeOffset.UtcNow;
        foreach (var prop in mids.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.String) continue;
            var px = prop.Value.GetString();
            if (string.IsNullOrEmpty(px)) continue;
            yield return new MidUpdate(prop.Name, ParseDec(px), now);
        }
    }

    private static IEnumerable<BboUpdate> ParseBbo(JsonElement data)
    {
        var coin = data.GetProperty("coin").GetString() ?? string.Empty;
        var time = HlMapper.T(data.GetProperty("time").GetInt64());
        var bbo = data.GetProperty("bbo");
        var bid = bbo[0];
        var ask = bbo[1];

        yield return new BboUpdate(
            Symbol:    coin,
            BidPrice:  ParseDec(bid.GetProperty("px").GetString()!),
            BidSize:   ParseDec(bid.GetProperty("sz").GetString()!),
            AskPrice:  ParseDec(ask.GetProperty("px").GetString()!),
            AskSize:   ParseDec(ask.GetProperty("sz").GetString()!),
            Timestamp: time);
    }

    private static IEnumerable<OrderUpdate> ParseOrderUpdates(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Array) yield break;
        foreach (var entry in data.EnumerateArray())
        {
            // Each entry: { "order": {...frontendOpenOrder}, "status": "...", "statusTimestamp": 123 }
            if (!entry.TryGetProperty("order", out var orderEl)) continue;
            var raw = orderEl.Deserialize<OpenOrderRaw>(HlJsonOptions.Default);
            if (raw is null) continue;

            var status = entry.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String
                ? HlMapper.ParseOrderStatus(s.GetString()!)
                : OrderStatus.Open;
            var statusTs = entry.TryGetProperty("statusTimestamp", out var ts) && ts.ValueKind == JsonValueKind.Number
                ? ts.GetInt64()
                : 0;

            yield return new OrderUpdate(HlMapper.Map(raw, status, statusTs));
        }
    }

    private static IEnumerable<FillUpdate> ParseUserFills(JsonElement data)
    {
        // data = { "user": "...", "isSnapshot": bool, "fills": [...] }
        if (!data.TryGetProperty("fills", out var fillsEl) || fillsEl.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var f in fillsEl.EnumerateArray())
        {
            var raw = f.Deserialize<UserFillRaw>(HlJsonOptions.Default);
            if (raw is null) continue;
            yield return new FillUpdate(HlMapper.Map(raw));
        }
    }

    private static IEnumerable<FundingUpdate> ParseUserFundings(JsonElement data)
    {
        // data = { "user": "...", "isSnapshot": bool, "fundings": [...] }
        if (!data.TryGetProperty("fundings", out var arr) || arr.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var f in arr.EnumerateArray())
        {
            var coin = f.GetProperty("coin").GetString() ?? string.Empty;
            var amount = ParseDec(f.GetProperty("usdc").GetString() ?? "0");
            var rate = ParseDec(f.GetProperty("fundingRate").GetString() ?? "0");
            var time = HlMapper.T(f.GetProperty("time").GetInt64());
            yield return new FundingUpdate(coin, amount, rate, time);
        }
    }

    private static IEnumerable<NotificationUpdate> ParseNotifications(JsonElement data)
    {
        // data = { "notification": "..." }
        if (!data.TryGetProperty("notification", out var note) || note.ValueKind != JsonValueKind.String)
            yield break;

        yield return new NotificationUpdate(note.GetString() ?? string.Empty, DateTimeOffset.UtcNow);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static decimal ParseDec(string s) =>
        decimal.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private string RequireUser() => options.Credentials?.MasterAddress
        ?? throw new AuthenticationException(
            "HyperLiquidClientOptions.Credentials.MasterAddress is required for user-scoped streams.");
}
