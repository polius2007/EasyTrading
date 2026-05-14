using System.Text.Json;
using EasyTrading.Abstractions;
using EasyTrading.HyperLiquid.Infrastructure;

namespace EasyTrading.HyperLiquid.UnitTests;

/// <summary>
/// Mapper unit tests use JSON fixtures patterned after the live HyperLiquid Info endpoint payloads.
/// They exercise the raw DTO → Abstractions model boundary without hitting the network.
/// </summary>
public sealed class HlMapperTests
{
    private static readonly JsonSerializerOptions Json = HlJsonOptions.Default;

    [Theory]
    [InlineData("B", OrderSide.Buy)]
    [InlineData("A", OrderSide.Sell)]
    public void ParseSide_maps_HL_letters(string raw, OrderSide expected)
    {
        Assert.Equal(expected, HlMapper.ParseSide(raw));
    }

    [Theory]
    [InlineData("1m", Interval.OneMinute)]
    [InlineData("5m", Interval.FiveMinutes)]
    [InlineData("15m", Interval.FifteenMinutes)]
    [InlineData("1h", Interval.OneHour)]
    [InlineData("4h", Interval.FourHours)]
    [InlineData("1d", Interval.OneDay)]
    [InlineData("1w", Interval.OneWeek)]
    [InlineData("1M", Interval.OneMonth)]
    public void ParseInterval_round_trips(string raw, Interval expected)
    {
        Assert.Equal(expected, HlMapper.ParseInterval(raw));
        Assert.Equal(raw, HlMapper.SerializeInterval(expected));
    }

    [Theory]
    [InlineData("open", OrderStatus.Open)]
    [InlineData("filled", OrderStatus.Filled)]
    [InlineData("canceled", OrderStatus.Cancelled)]
    [InlineData("marginCanceled", OrderStatus.Cancelled)]
    [InlineData("rejected", OrderStatus.Rejected)]
    [InlineData("triggered", OrderStatus.Triggered)]
    public void ParseOrderStatus_covers_the_common_values(string raw, OrderStatus expected)
    {
        Assert.Equal(expected, HlMapper.ParseOrderStatus(raw));
    }

    [Fact]
    public void Map_l2Book_parses_bids_and_asks()
    {
        const string json = """
        {
          "coin": "BTC",
          "time": 1754450974231,
          "levels": [
            [
              { "px": "113377.0", "sz": "7.6699", "n": 17 },
              { "px": "113376.0", "sz": "1.0",    "n": 1  }
            ],
            [
              { "px": "113397.0", "sz": "0.11543", "n": 3 }
            ]
          ]
        }
        """;

        var raw = JsonSerializer.Deserialize<L2BookRaw>(json, Json)!;
        var book = HlMapper.Map(raw);

        Assert.Equal("BTC", book.Symbol);
        Assert.Equal(2, book.Bids.Count);
        Assert.Equal(113377.0m, book.Bids[0].Price);
        Assert.Equal(7.6699m, book.Bids[0].Size);
        Assert.Equal(17, book.Bids[0].OrderCount);
        Assert.Single(book.Asks);
        Assert.Equal(113397.0m, book.Asks[0].Price);
    }

    [Fact]
    public void Map_candle_parses_OHLCV_and_interval()
    {
        const string json = """
        {
          "T": 1681924499999,
          "c": "29258.0",
          "h": "29309.0",
          "i": "15m",
          "l": "29250.0",
          "n": 189,
          "o": "29295.0",
          "s": "BTC",
          "t": 1681923600000,
          "v": "0.98639"
        }
        """;

        var raw = JsonSerializer.Deserialize<CandleRaw>(json, Json)!;
        var candle = HlMapper.Map(raw);

        Assert.Equal("BTC", candle.Symbol);
        Assert.Equal(Interval.FifteenMinutes, candle.Interval);
        Assert.Equal(29295.0m, candle.Open);
        Assert.Equal(29309.0m, candle.High);
        Assert.Equal(29250.0m, candle.Low);
        Assert.Equal(29258.0m, candle.Close);
        Assert.Equal(0.98639m, candle.Volume);
        Assert.Equal(189, candle.TradeCount);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1681923600000), candle.OpenTime);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1681924499999), candle.CloseTime);
    }

    [Fact]
    public void Map_position_parses_signed_size_and_isolated_leverage()
    {
        const string json = """
        {
          "coin": "BTC",
          "szi": "-0.5",
          "entryPx": "60000",
          "positionValue": "30000",
          "unrealizedPnl": "-100",
          "returnOnEquity": "-0.05",
          "liquidationPx": "62000",
          "marginUsed": "1500",
          "maxLeverage": 50,
          "leverage": { "type": "isolated", "value": 20, "rawUsd": "1500" },
          "cumFunding": { "allTime": "0", "sinceChange": "0", "sinceOpen": "0" }
        }
        """;

        var raw = JsonSerializer.Deserialize<PositionRaw>(json, Json)!;
        var pos = HlMapper.Map(raw);

        Assert.Equal("BTC", pos.Symbol);
        Assert.Equal(-0.5m, pos.Size);
        Assert.Equal(60000m, pos.EntryPrice);
        Assert.Equal(-100m, pos.UnrealizedPnl);
        Assert.Equal(20, pos.Leverage);
        Assert.Equal(MarginMode.Isolated, pos.MarginMode);
        Assert.Equal(62000m, pos.LiquidationPrice);
    }

    [Fact]
    public void Map_userFill_maps_side_and_maker_flag_correctly()
    {
        const string json = """
        {
          "coin": "ETH",
          "px": "2400.5",
          "sz": "0.1",
          "side": "B",
          "time": 1700000000000,
          "startPosition": "0",
          "dir": "Open Long",
          "closedPnl": "0",
          "hash": "0xabc",
          "oid": 99,
          "crossed": false,
          "fee": "0.05",
          "tid": 42,
          "feeToken": "USDC"
        }
        """;

        var raw = JsonSerializer.Deserialize<UserFillRaw>(json, Json)!;
        var fill = HlMapper.Map(raw);

        Assert.Equal(42L, fill.TradeId);
        Assert.Equal(99L, fill.OrderId);
        Assert.Equal("ETH", fill.Symbol);
        Assert.Equal(OrderSide.Buy, fill.Side);
        Assert.Equal(2400.5m, fill.Price);
        Assert.Equal(0.1m, fill.Size);
        Assert.Equal(0.05m, fill.Fee);
        Assert.Equal("USDC", fill.FeeAsset);
        Assert.True(fill.IsMaker);   // crossed=false => maker
    }

    [Fact]
    public void Map_account_state_combines_perp_withdrawable_and_spot_balances()
    {
        const string perpJson = """
        {
          "assetPositions": [],
          "crossMaintenanceMarginUsed": "0",
          "crossMarginSummary": { "accountValue": "1000", "totalMarginUsed": "0", "totalNtlPos": "0", "totalRawUsd": "1000" },
          "marginSummary":      { "accountValue": "1000", "totalMarginUsed": "0", "totalNtlPos": "0", "totalRawUsd": "1000" },
          "time": 1700000000000,
          "withdrawable": "950.5"
        }
        """;

        const string spotJson = """
        {
          "balances": [
            { "coin": "USDC", "token": 0, "total": "200.0", "hold": "0", "entryNtl": "0" },
            { "coin": "PURR", "token": 1, "total": "50.0",  "hold": "5", "entryNtl": "100" }
          ]
        }
        """;

        var perp = JsonSerializer.Deserialize<ClearinghouseStateRaw>(perpJson, Json)!;
        var spot = JsonSerializer.Deserialize<SpotClearinghouseStateRaw>(spotJson, Json)!;

        var state = HlMapper.MapAccountState(perp, spot.Balances);

        Assert.Equal(1000m, state.AccountValue);
        Assert.Equal(950.5m, state.FreeCollateral);
        Assert.Empty(state.Positions);
        Assert.Equal(200.0m, state.Balances["USDC"]);
        Assert.Equal(50.0m, state.Balances["PURR"]);
    }

    [Fact]
    public void Map_openOrder_with_full_frontend_payload()
    {
        const string json = """
        {
          "coin": "BTC",
          "isPositionTpsl": false,
          "isTrigger": false,
          "limitPx": "29792.0",
          "oid": 91490942,
          "orderType": "Limit",
          "origSz": "5.0",
          "reduceOnly": false,
          "side": "A",
          "sz": "5.0",
          "timestamp": 1681247412573,
          "triggerCondition": "N/A",
          "triggerPx": "0.0",
          "tif": "Gtc"
        }
        """;

        var raw = JsonSerializer.Deserialize<OpenOrderRaw>(json, Json)!;
        var order = HlMapper.Map(raw);

        Assert.Equal(91490942L, order.OrderId);
        Assert.Equal("BTC", order.Symbol);
        Assert.Equal(OrderSide.Sell, order.Side);
        Assert.Equal(OrderType.Limit, order.OrderType);
        Assert.Equal(29792.0m, order.Price);
        Assert.Equal(5.0m, order.Size);
        Assert.Equal(0m, order.FilledSize);
        Assert.Equal(TimeInForce.Gtc, order.TimeInForce);
        Assert.False(order.ReduceOnly);
    }

    [Fact]
    public void Map_portfolio_extracts_allTime_period()
    {
        const string json = """
        [
          ["day",      { "accountValueHistory": [], "pnlHistory": [], "vlm": "0.0" }],
          ["week",     { "accountValueHistory": [], "pnlHistory": [], "vlm": "0.0" }],
          ["allTime",  { "accountValueHistory": [[1700000000000, "100.0"], [1700001000000, "120.0"]], "pnlHistory": [[1700000000000, "0.0"], [1700001000000, "20.0"]], "vlm": "500" }]
        ]
        """;

        using var doc = JsonDocument.Parse(json);
        var portfolio = HlMapper.MapPortfolio(doc.RootElement);

        Assert.Equal(2, portfolio.AccountValueHistory.Count);
        Assert.Equal(120.0m, portfolio.AccountValueHistory[1].Value);
        Assert.Equal(2, portfolio.PnlHistory.Count);
        Assert.Equal(20.0m, portfolio.PnlHistory[1].Value);
    }

    [Fact]
    public void Map_rateLimit_passes_used_and_cap_through()
    {
        const string json = """
        {
          "cumVlm": "2854574.593578",
          "nRequestsUsed": 2890,
          "nRequestsCap": 2864574,
          "nRequestsSurplus": 0
        }
        """;

        var raw = JsonSerializer.Deserialize<UserRateLimitRaw>(json, Json)!;
        var rl = HlMapper.Map(raw);

        Assert.Equal(2890, rl.Used);
        Assert.Equal(2864574, rl.Limit);
    }
}
