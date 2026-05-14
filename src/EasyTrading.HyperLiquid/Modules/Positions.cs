using System.Globalization;
using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.HyperLiquid.Infrastructure;

namespace EasyTrading.HyperLiquid.Modules;

/// <summary>HyperLiquid implementation of <see cref="IPositions"/>. Reads use clearinghouseState; writes sign and POST to the Exchange endpoint.</summary>
internal sealed class Positions(
    InfoClient info,
    ExchangeClient exchange,
    MetaCache meta,
    HyperLiquidClientOptions options) : IPositions
{
    public async Task<IReadOnlyList<Position>> GetAllAsync(CancellationToken ct = default)
    {
        var user = RequireUser();
        var raw = await info.PostAsync<ClearinghouseStateRaw>(new { type = "clearinghouseState", user }, ct).ConfigureAwait(false);
        return raw.AssetPositions.Select(ap => Mapper.Map(ap.Position)).ToList();
    }

    public async Task<Position?> GetAsync(string symbol, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct).ConfigureAwait(false);
        return all.FirstOrDefault(p => p.Symbol == symbol);
    }

    public async Task SetLeverageAsync(string symbol, int leverage, MarginMode mode, CancellationToken ct = default)
    {
        var assetId = await meta.GetAssetIdAsync(symbol, ct).ConfigureAwait(false);
        var action = new HlMap()
            .Add("type", "updateLeverage")
            .Add("asset", assetId)
            .Add("isCross", mode == MarginMode.Cross)
            .Add("leverage", leverage);

        await exchange.SendL1Async(action, expiresAfter: null, ct).ConfigureAwait(false);
    }

    public Task AddMarginAsync(string symbol, decimal amount, CancellationToken ct = default)
        => UpdateIsolatedMarginAsync(symbol, amount, ct);

    public Task ReduceMarginAsync(string symbol, decimal amount, CancellationToken ct = default)
        => UpdateIsolatedMarginAsync(symbol, -amount, ct);

    private async Task UpdateIsolatedMarginAsync(string symbol, decimal deltaUsdc, CancellationToken ct)
    {
        var user = RequireUser();
        // We need the position's direction to fill the `isBuy` field.
        var state = await info.PostAsync<ClearinghouseStateRaw>(new { type = "clearinghouseState", user }, ct).ConfigureAwait(false);
        var position = state.AssetPositions
            .Select(ap => ap.Position)
            .FirstOrDefault(p => string.Equals(p.Coin, symbol, StringComparison.OrdinalIgnoreCase));

        if (position is null || position.Szi == 0m)
            throw new InvalidOrderException($"Cannot adjust margin on '{symbol}': no open position.");

        var assetId = await meta.GetAssetIdAsync(symbol, ct).ConfigureAwait(false);
        // ntli is delta in 6-decimal USDC (positive = add margin, negative = remove).
        var ntli = (long)decimal.Round(deltaUsdc * 1_000_000m, MidpointRounding.ToEven);

        var action = new HlMap()
            .Add("type", "updateIsolatedMargin")
            .Add("asset", assetId)
            .Add("isBuy", position.Szi > 0m)
            .Add("ntli", ntli);

        await exchange.SendL1Async(action, null, ct).ConfigureAwait(false);
    }

    public async Task<PlaceOrderResult> CloseAsync(string symbol, CancellationToken ct = default)
    {
        var user = RequireUser();
        var state = await info.PostAsync<ClearinghouseStateRaw>(new { type = "clearinghouseState", user }, ct).ConfigureAwait(false);

        var position = state.AssetPositions
            .Select(ap => ap.Position)
            .FirstOrDefault(p => string.Equals(p.Coin, symbol, StringComparison.OrdinalIgnoreCase));

        if (position is null || position.Szi == 0m)
        {
            return new PlaceOrderResult(
                OrderId: 0, ClientOrderId: null, Status: OrderStatus.Filled,
                FilledSize: 0m, AverageFillPrice: null,
                ErrorMessage: $"No open position on '{symbol}' to close.");
        }

        var size = Math.Abs(position.Szi);
        var side = position.Szi > 0 ? OrderSide.Sell : OrderSide.Buy;

        var mids = await info.PostAsync<Dictionary<string, string>>(new { type = "allMids" }, ct).ConfigureAwait(false);
        if (!mids.TryGetValue(symbol, out var midStr))
            return new PlaceOrderResult(0, null, OrderStatus.Rejected, 0m, null, $"Mid price unavailable for {symbol}.");

        var mid = decimal.Parse(midStr, NumberStyles.Float, CultureInfo.InvariantCulture);
        const decimal slippage = 0.05m;
        var px = side == OrderSide.Buy ? mid * (1m + slippage) : mid * (1m - slippage);

        var assetId = await meta.GetAssetIdAsync(symbol, ct).ConfigureAwait(false);
        var orderWire = new HlMap()
            .Add("a", assetId)
            .Add("b", side == OrderSide.Buy)
            .Add("p", FloatToWire(px))
            .Add("s", FloatToWire(size))
            .Add("r", true)
            .Add("t", new HlMap().Add("limit", new HlMap().Add("tif", "Ioc")));

        var action = new HlMap()
            .Add("type", "order")
            .Add("orders", new object[] { orderWire })
            .Add("grouping", "na");
        AttachBuilderFeeStatic(action, options);

        var response = await exchange.SendL1Async(action, null, ct).ConfigureAwait(false);
        var status = response.GetProperty("data").GetProperty("statuses")[0];

        if (status.TryGetProperty("filled", out var filled))
        {
            var oid = filled.GetProperty("oid").GetInt64();
            var totalSz = decimal.Parse(filled.GetProperty("totalSz").GetString()!, NumberStyles.Float, CultureInfo.InvariantCulture);
            var avgPx = decimal.Parse(filled.GetProperty("avgPx").GetString()!, NumberStyles.Float, CultureInfo.InvariantCulture);
            return new PlaceOrderResult(oid, null, OrderStatus.Filled, totalSz, avgPx, null);
        }

        if (status.TryGetProperty("resting", out var resting))
            return new PlaceOrderResult(resting.GetProperty("oid").GetInt64(), null, OrderStatus.Open, 0m, null, null);

        if (status.TryGetProperty("error", out var err))
            return new PlaceOrderResult(0, null, OrderStatus.Rejected, 0m, null, err.GetString());

        return new PlaceOrderResult(0, null, OrderStatus.Pending, 0m, null, status.GetRawText());
    }

    private static void AttachBuilderFeeStatic(HlMap action, HyperLiquidClientOptions options)
    {
        var fee = options.BuilderFee
            ?? new BuilderFee(BuilderDefaults.BuilderAddress, BuilderDefaults.FeeRate);
        if (fee.FeeRate <= 0m) return;
        var wireFee = (int)Math.Round(fee.FeeRate * 100_000m, MidpointRounding.ToEven);
        if (wireFee <= 0) return;
        action.Add("builder", new HlMap()
            .Add("b", fee.BuilderAddress.ToLowerInvariant())
            .Add("f", wireFee));
    }

    private static string FloatToWire(decimal value)
    {
        if (value == 0m) return "0";
        var s = value.ToString("0.########", CultureInfo.InvariantCulture);
        return s == "-0" ? "0" : s;
    }

    private string RequireUser() => options.Credentials?.MasterAddress
        ?? throw new AuthenticationException(
            "HyperLiquidClientOptions.Credentials.MasterAddress is required for position queries.");
}
