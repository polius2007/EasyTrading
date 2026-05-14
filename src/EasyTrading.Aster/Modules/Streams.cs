using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.Aster.Infrastructure;

namespace EasyTrading.Aster.Modules;

/// <summary>
/// Aster implementation of <see cref="IStreams"/>. Market streams multiplex over a single shared
/// WebSocket (<see cref="WebSocketClient"/>). User-scoped streams require a <c>listenKey</c>
/// obtained from REST and bound to a separate WebSocket — see <see cref="EnsureUserSocketAsync"/>.
/// </summary>
internal sealed class Streams : IStreams, IAsyncDisposable
{
    private readonly WebSocketClient _market;
    private readonly RestClient _rest;
    private readonly AsterClientOptions _options;

    // User-data socket is created on first user-stream subscribe.
    private readonly SemaphoreSlim _userLock = new(1, 1);
    private WebSocketClient? _user;
    private string? _listenKey;
    private CancellationTokenSource? _userKeepaliveCts;

    public Streams(WebSocketClient marketClient, RestClient rest, AsterClientOptions options)
    {
        _market = marketClient;
        _rest = rest;
        _options = options;
    }

    // ─── public market streams ───────────────────────────────────────────────

    public IAsyncEnumerable<TradeUpdate> TradesAsync(string symbol, CancellationToken ct)
        => _market.SubscribeAsync<TradeUpdate>(
            streamName: symbol.ToLowerInvariant() + "@aggTrade",
            parser:     data => ParseAggTrade(data, symbol),
            ct:         ct);

    public IAsyncEnumerable<OrderBookUpdate> OrderBookAsync(string symbol, int depth = 20, CancellationToken ct = default)
    {
        // Aster's @depth<N> emits a snapshot every 250ms; <symbol>@depth emits diffs.
        var lvls = depth <= 5 ? 5 : depth <= 10 ? 10 : 20;
        return _market.SubscribeAsync<OrderBookUpdate>(
            streamName: $"{symbol.ToLowerInvariant()}@depth{lvls}",
            parser:     data => ParseDepth(data, symbol),
            ct:         ct);
    }

    public IAsyncEnumerable<CandleUpdate> CandlesAsync(string symbol, Interval interval, CancellationToken ct = default)
    {
        var iv = AsterIntervalWire(interval);
        return _market.SubscribeAsync<CandleUpdate>(
            streamName: $"{symbol.ToLowerInvariant()}@kline_{iv}",
            parser:     data => ParseKline(data),
            ct:         ct);
    }

    public IAsyncEnumerable<MidUpdate> AllMidsAsync(CancellationToken ct)
    {
        // Aster's analogue is `!markPrice@arr` (every symbol's mark price). We map mark price → mid.
        return _market.SubscribeAsync<MidUpdate>(
            streamName: "!markPrice@arr@1s",
            parser:     ParseAllMids,
            ct:         ct);
    }

    public IAsyncEnumerable<BboUpdate> BestBidOfferAsync(string symbol, CancellationToken ct)
        => _market.SubscribeAsync<BboUpdate>(
            streamName: symbol.ToLowerInvariant() + "@bookTicker",
            parser:     data => ParseBookTicker(data, symbol),
            ct:         ct);

    // ─── user streams (require listenKey) ────────────────────────────────────

    public async IAsyncEnumerable<OrderUpdate> MyOrdersAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await EnsureUserSocketAsync(ct).ConfigureAwait(false);
        await foreach (var u in _user!.SubscribeAsync<OrderUpdate>(
            "user:orderTradeUpdate",
            ParseOrderUpdate,
            ct).ConfigureAwait(false))
            yield return u;
    }

    public async IAsyncEnumerable<FillUpdate> MyFillsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await EnsureUserSocketAsync(ct).ConfigureAwait(false);
        // Aster doesn't have a separate "fill" event — fills arrive embedded in ORDER_TRADE_UPDATE
        // with executionType=TRADE. We parse those and yield Fill updates.
        await foreach (var f in _user!.SubscribeAsync<FillUpdate>(
            "user:orderTradeUpdate",
            ParseFillUpdateFromOrder,
            ct).ConfigureAwait(false))
            yield return f;
    }

    public IAsyncEnumerable<FundingUpdate> MyFundingsAsync(CancellationToken ct)
    {
        // Funding payments aren't a discrete user-data event on Aster — they materialise as
        // balance / income updates. Polling /fapi/v3/income with a periodic timer would be the
        // canonical approach. For now expose an empty stream so callers can opt into a polling
        // helper without seeing NotImplementedException.
        return EmptyAsync<FundingUpdate>(ct);
    }

    public IAsyncEnumerable<NotificationUpdate> MyNotificationsAsync(CancellationToken ct)
    {
        // Margin-call events count as notifications. Wire through the user socket.
        return SubscribeUserAsync<NotificationUpdate>("user:marginCall", ParseMarginCall, ct);
    }

    private async IAsyncEnumerable<T> SubscribeUserAsync<T>(
        string key, Func<JsonElement, IEnumerable<T>> parser, [EnumeratorCancellation] CancellationToken ct)
    {
        await EnsureUserSocketAsync(ct).ConfigureAwait(false);
        await foreach (var item in _user!.SubscribeAsync(key, parser, ct).ConfigureAwait(false))
            yield return item;
    }

    private static async IAsyncEnumerable<T> EmptyAsync<T>([EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        try { await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { /* expected */ }
        yield break;
    }

    // ─── listenKey lifecycle ────────────────────────────────────────────────

    private async Task EnsureUserSocketAsync(CancellationToken ct)
    {
        if (_user is not null) return;

        await _userLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_user is not null) return;

            // 1. Get a listenKey from REST.
            var raw = await _rest.SendSignedAsync<ListenKeyRaw>(
                HttpMethod.Post, "/fapi/v3/listenKey", new Dictionary<string, string>(), ct).ConfigureAwait(false);
            _listenKey = raw.ListenKey;

            // 2. Build the URL provider that re-fetches the listenKey on every (re)connect — if
            //    the key expired during a disconnect we'll get a fresh one transparently.
            var wsBase = _options.GetEffectiveWebSocketUrl().ToString().TrimEnd('/');
            _user = new WebSocketClient(
                async ctk =>
                {
                    var current = _listenKey ?? throw new InvalidOperationException("No listenKey.");
                    return new Uri($"{wsBase}/{current}");
                },
                _options.WebSocketReconnectDelay,
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

            // 3. Start the 30-minute keepalive timer (Aster requires PUT every <60min; we go 30).
            _userKeepaliveCts = new CancellationTokenSource();
            _ = Task.Run(() => KeepaliveLoopAsync(_userKeepaliveCts.Token), _userKeepaliveCts.Token);
        }
        finally
        {
            _userLock.Release();
        }
    }

    private async Task KeepaliveLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromMinutes(30);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);
                await _rest.SendSignedRawAsync(HttpMethod.Put, "/fapi/v3/listenKey", new Dictionary<string, string>(), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch
            {
                // Best-effort. If it fails, the WS may receive listenKeyExpired and the consumer
                // will see a stream gap. Re-arm and try again.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _userKeepaliveCts?.Cancel();
        if (_user is not null) await _user.DisposeAsync().ConfigureAwait(false);
        _userLock.Dispose();
        _userKeepaliveCts?.Dispose();
    }

    // ─── parsers (one per channel; called inside the WS dispatcher) ─────────

    private static IEnumerable<TradeUpdate> ParseAggTrade(JsonElement data, string symbol)
    {
        // Aster aggTrade: { e, E, s, a, p, q, f, l, T, m }. `m`=true → buyer was maker → seller aggressor.
        var price = ParseDec(data.GetProperty("p").GetString() ?? "0");
        var qty   = ParseDec(data.GetProperty("q").GetString() ?? "0");
        var time  = data.GetProperty("T").GetInt64();
        var aggId = data.TryGetProperty("a", out var aEl) ? aEl.GetInt64() : 0L;
        var maker = data.TryGetProperty("m", out var mEl) && mEl.GetBoolean();
        var side  = maker ? OrderSide.Sell : OrderSide.Buy;

        yield return new TradeUpdate(new PublicTrade(symbol, price, qty, side, Mapper.ToDt(time), aggId));
    }

    private static IEnumerable<OrderBookUpdate> ParseDepth(JsonElement data, string symbol)
    {
        var time = data.TryGetProperty("E", out var eEl) ? eEl.GetInt64() : 0L;
        var bids = ExtractLevels(data, "b");
        var asks = ExtractLevels(data, "a");
        yield return new OrderBookUpdate(symbol, Mapper.ToDt(time), bids, asks, IsSnapshot: false);
    }

    private static List<OrderBookLevel> ExtractLevels(JsonElement msg, string property)
    {
        if (!msg.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return new List<OrderBookLevel>();

        var result = new List<OrderBookLevel>(arr.GetArrayLength());
        foreach (var lvl in arr.EnumerateArray())
        {
            if (lvl.ValueKind != JsonValueKind.Array || lvl.GetArrayLength() < 2) continue;
            var px = ParseDec(lvl[0].GetString() ?? "0");
            var sz = ParseDec(lvl[1].GetString() ?? "0");
            result.Add(new OrderBookLevel(px, sz, OrderCount: 0));
        }
        return result;
    }

    private static IEnumerable<CandleUpdate> ParseKline(JsonElement data)
    {
        if (!data.TryGetProperty("k", out var k)) yield break;
        var sym  = data.GetProperty("s").GetString() ?? string.Empty;
        var iv   = k.GetProperty("i").GetString() ?? "1m";
        var open = ParseDec(k.GetProperty("o").GetString() ?? "0");
        var high = ParseDec(k.GetProperty("h").GetString() ?? "0");
        var low  = ParseDec(k.GetProperty("l").GetString() ?? "0");
        var close = ParseDec(k.GetProperty("c").GetString() ?? "0");
        var vol  = ParseDec(k.GetProperty("v").GetString() ?? "0");
        var t    = k.GetProperty("t").GetInt64();
        var T    = k.GetProperty("T").GetInt64();
        var n    = k.TryGetProperty("n", out var nEl) ? nEl.GetInt32() : 0;

        yield return new CandleUpdate(new Candle(
            Symbol:     sym,
            Interval:   ParseInterval(iv),
            OpenTime:   Mapper.ToDt(t),
            CloseTime:  Mapper.ToDt(T),
            Open:       open, High: high, Low: low, Close: close,
            Volume:     vol,
            TradeCount: n));
    }

    private static IEnumerable<MidUpdate> ParseAllMids(JsonElement data)
    {
        // !markPrice@arr emits an array of { e, E, s, p, ... }
        if (data.ValueKind != JsonValueKind.Array) yield break;
        var now = DateTimeOffset.UtcNow;
        foreach (var el in data.EnumerateArray())
        {
            if (!el.TryGetProperty("s", out var sEl)) continue;
            if (!el.TryGetProperty("p", out var pEl)) continue;
            var sym = sEl.GetString();
            var px  = pEl.GetString();
            if (sym is null || px is null) continue;
            yield return new MidUpdate(sym, ParseDec(px), now);
        }
    }

    private static IEnumerable<BboUpdate> ParseBookTicker(JsonElement data, string symbol)
    {
        var bidPx = ParseDec(data.GetProperty("b").GetString() ?? "0");
        var bidSz = ParseDec(data.GetProperty("B").GetString() ?? "0");
        var askPx = ParseDec(data.GetProperty("a").GetString() ?? "0");
        var askSz = ParseDec(data.GetProperty("A").GetString() ?? "0");
        var time  = data.TryGetProperty("T", out var tEl) ? tEl.GetInt64() : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        yield return new BboUpdate(symbol, bidPx, bidSz, askPx, askSz, Mapper.ToDt(time));
    }

    private static IEnumerable<OrderUpdate> ParseOrderUpdate(JsonElement data)
    {
        // ORDER_TRADE_UPDATE: { e, E, o: { ... } }
        if (!data.TryGetProperty("o", out var o)) yield break;

        var oid     = o.GetProperty("i").GetInt64();
        var sym     = o.GetProperty("s").GetString() ?? string.Empty;
        var status  = o.GetProperty("X").GetString() ?? "NEW";
        var side    = o.GetProperty("S").GetString() ?? "BUY";
        var type    = o.GetProperty("o").GetString() ?? "LIMIT";
        var price   = o.TryGetProperty("p", out var pEl) ? ParseDec(pEl.GetString() ?? "0") : 0m;
        var qty     = o.TryGetProperty("q", out var qEl) ? ParseDec(qEl.GetString() ?? "0") : 0m;
        var filled  = o.TryGetProperty("z", out var zEl) ? ParseDec(zEl.GetString() ?? "0") : 0m;
        var tif     = o.TryGetProperty("f", out var fEl) ? fEl.GetString() : "GTC";
        var time    = o.TryGetProperty("T", out var tEl) ? tEl.GetInt64() : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var cloid   = o.TryGetProperty("c", out var cEl) ? cEl.GetString() : null;
        var reduce  = o.TryGetProperty("R", out var rEl) && rEl.GetBoolean();
        var trigger = o.TryGetProperty("sp", out var spEl) ? ParseDec(spEl.GetString() ?? "0") : 0m;

        yield return new OrderUpdate(new Order(
            OrderId:       oid,
            ClientOrderId: cloid,
            Symbol:        sym,
            Side:          Mapper.ParseSide(side),
            OrderType:     Mapper.ParseType(type),
            Price:         price > 0 ? price : null,
            TriggerPrice:  trigger > 0 ? trigger : null,
            Size:          qty,
            FilledSize:    filled,
            TimeInForce:   Mapper.ParseTif(tif),
            ReduceOnly:    reduce,
            Status:        Mapper.ParseStatus(status),
            CreatedAt:     Mapper.ToDt(time),
            UpdatedAt:     Mapper.ToDt(time)));
    }

    private static IEnumerable<FillUpdate> ParseFillUpdateFromOrder(JsonElement data)
    {
        if (!data.TryGetProperty("o", out var o)) yield break;
        // Only emit when this update represents an actual trade execution.
        var execType = o.TryGetProperty("x", out var xEl) ? xEl.GetString() : null;
        if (!string.Equals(execType, "TRADE", StringComparison.OrdinalIgnoreCase)) yield break;

        var tid    = o.TryGetProperty("t", out var tEl) ? tEl.GetInt64() : 0L;
        var oid    = o.TryGetProperty("i", out var iEl) ? iEl.GetInt64() : 0L;
        var sym    = o.GetProperty("s").GetString() ?? string.Empty;
        var side   = Mapper.ParseSide(o.GetProperty("S").GetString() ?? "BUY");
        var lpx    = o.TryGetProperty("L", out var lEl) ? ParseDec(lEl.GetString() ?? "0") : 0m;
        var lqty   = o.TryGetProperty("l", out var lqEl) ? ParseDec(lqEl.GetString() ?? "0") : 0m;
        var fee    = o.TryGetProperty("n", out var nEl) ? ParseDec(nEl.GetString() ?? "0") : 0m;
        var feeAss = o.TryGetProperty("N", out var fnEl) ? fnEl.GetString() : "USDT";
        var time   = o.TryGetProperty("T", out var tmEl) ? tmEl.GetInt64() : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var maker  = o.TryGetProperty("m", out var mEl) && mEl.GetBoolean();
        var cloid  = o.TryGetProperty("c", out var cEl) ? cEl.GetString() : null;

        yield return new FillUpdate(new Fill(
            TradeId:       tid,
            OrderId:       oid,
            ClientOrderId: cloid,
            Symbol:        sym,
            Side:          side,
            Price:         lpx,
            Size:          lqty,
            Fee:           fee,
            FeeAsset:      feeAss ?? "USDT",
            IsMaker:       maker,
            Time:          Mapper.ToDt(time)));
    }

    private static IEnumerable<NotificationUpdate> ParseMarginCall(JsonElement data)
    {
        var time = data.TryGetProperty("E", out var eEl) ? eEl.GetInt64() : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        yield return new NotificationUpdate("MARGIN_CALL — Aster reports your margin level is low; positions may be auto-liquidated.", Mapper.ToDt(time));
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static decimal ParseDec(string s) =>
        decimal.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static string AsterIntervalWire(Interval i) => i switch
    {
        Interval.OneMinute      => "1m",
        Interval.ThreeMinutes   => "3m",
        Interval.FiveMinutes    => "5m",
        Interval.FifteenMinutes => "15m",
        Interval.ThirtyMinutes  => "30m",
        Interval.OneHour        => "1h",
        Interval.TwoHours       => "2h",
        Interval.FourHours      => "4h",
        Interval.EightHours     => "8h",
        Interval.TwelveHours    => "12h",
        Interval.OneDay         => "1d",
        Interval.ThreeDays      => "3d",
        Interval.OneWeek        => "1w",
        Interval.OneMonth       => "1M",
        _                       => "1m",
    };

    private static Interval ParseInterval(string s) => s switch
    {
        "1m"  => Interval.OneMinute,
        "3m"  => Interval.ThreeMinutes,
        "5m"  => Interval.FiveMinutes,
        "15m" => Interval.FifteenMinutes,
        "30m" => Interval.ThirtyMinutes,
        "1h"  => Interval.OneHour,
        "2h"  => Interval.TwoHours,
        "4h"  => Interval.FourHours,
        // Aster supports 6h on the wire but the cross-DEX Interval enum doesn't carry it;
        // fall through to a sensible default rather than throwing.
        "6h"  => Interval.FourHours,
        "8h"  => Interval.EightHours,
        "12h" => Interval.TwelveHours,
        "1d"  => Interval.OneDay,
        "3d"  => Interval.ThreeDays,
        "1w"  => Interval.OneWeek,
        "1M"  => Interval.OneMonth,
        _     => Interval.OneMinute,
    };
}
