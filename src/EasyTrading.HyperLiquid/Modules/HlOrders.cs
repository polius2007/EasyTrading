using System.Globalization;
using System.Text.Json;
using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.HyperLiquid.Infrastructure;

namespace EasyTrading.HyperLiquid.Modules;

/// <summary>HyperLiquid implementation of <see cref="IOrders"/>. Read methods use the Info endpoint; write methods sign and POST to the Exchange endpoint.</summary>
internal sealed class HlOrders(
    HlInfoClient info,
    HlExchangeClient exchange,
    HlMetaCache meta,
    HyperLiquidClientOptions options) : IOrders
{
    // ─── Read methods (Phase 2) ──────────────────────────────────────────────

    public async Task<IReadOnlyList<Order>> GetOpenAsync(string? symbol = null, CancellationToken ct = default)
    {
        var user = RequireUser();
        var raw = await info.PostAsync<List<OpenOrderRaw>>(new { type = "frontendOpenOrders", user }, ct).ConfigureAwait(false);
        IEnumerable<Order> orders = raw.Select(o => HlMapper.Map(o));
        if (symbol is not null) orders = orders.Where(o => o.Symbol == symbol);
        return orders.ToList();
    }

    public async Task<Order?> GetAsync(long orderId, CancellationToken ct = default)
    {
        var user = RequireUser();
        var raw = await info.PostAsync<OrderStatusResponseRaw>(new { type = "orderStatus", user, oid = orderId }, ct).ConfigureAwait(false);
        return MapStatus(raw);
    }

    public async Task<Order?> GetByClientIdAsync(string clientOrderId, CancellationToken ct = default)
    {
        var user = RequireUser();
        var raw = await info.PostAsync<OrderStatusResponseRaw>(new { type = "orderStatus", user, oid = clientOrderId }, ct).ConfigureAwait(false);
        return MapStatus(raw);
    }

    public async Task<IReadOnlyList<Order>> GetHistoryAsync(string? symbol = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var user = RequireUser();
        var raw = await info.PostAsync<List<HistoricalOrderRaw>>(new { type = "historicalOrders", user }, ct).ConfigureAwait(false);
        IEnumerable<Order> orders = raw.Select(HlMapper.Map);
        if (symbol is not null) orders = orders.Where(o => o.Symbol == symbol);
        if (from is not null) orders = orders.Where(o => o.CreatedAt >= from.Value);
        if (to is not null) orders = orders.Where(o => o.CreatedAt < to.Value);
        return orders.ToList();
    }

    public async Task<IReadOnlyList<TwapSliceFill>> GetTwapFillsAsync(CancellationToken ct = default)
    {
        var user = RequireUser();
        var raw = await info.PostAsync<List<TwapSliceFillRaw>>(new { type = "userTwapSliceFills", user }, ct).ConfigureAwait(false);
        return raw.Select(HlMapper.Map).ToList();
    }

    private static Order? MapStatus(OrderStatusResponseRaw raw)
    {
        if (raw.Status != "order" || raw.Order is null)
            return null;
        return HlMapper.Map(raw.Order.Order, HlMapper.ParseOrderStatus(raw.Order.Status), raw.Order.StatusTimestamp);
    }

    // ─── Write methods (Phase 3) ─────────────────────────────────────────────

    public async Task<PlaceOrderResult> PlaceAsync(OrderRequest request, CancellationToken ct = default)
    {
        var assetId = await meta.GetAssetIdAsync(request.Symbol, ct).ConfigureAwait(false);
        var orderWire = BuildOrderWire(assetId, request);

        var action = new HlMap()
            .Add("type", "order")
            .Add("orders", new object[] { orderWire })
            .Add("grouping", "na");
        AttachBuilderFee(action, request.BuilderFeeOverride);

        var response = await exchange.SendL1Async(action, expiresAfter: null, ct).ConfigureAwait(false);
        return ParseOrderStatus(response.GetProperty("data").GetProperty("statuses")[0], request.ClientOrderId);
    }

    public async Task<BatchOrderResult> PlaceBatchAsync(IReadOnlyList<OrderRequest> requests, CancellationToken ct = default)
    {
        if (requests.Count == 0)
            return new BatchOrderResult(Array.Empty<PlaceOrderResult>());

        var wires = new List<object>(requests.Count);
        foreach (var r in requests)
        {
            var assetId = await meta.GetAssetIdAsync(r.Symbol, ct).ConfigureAwait(false);
            wires.Add(BuildOrderWire(assetId, r));
        }

        var action = new HlMap()
            .Add("type", "order")
            .Add("orders", wires)
            .Add("grouping", "na");
        AttachBuilderFee(action, perOrderOverride: null);

        var response = await exchange.SendL1Async(action, expiresAfter: null, ct).ConfigureAwait(false);

        var statuses = response.GetProperty("data").GetProperty("statuses");
        var results = new List<PlaceOrderResult>(requests.Count);
        for (var i = 0; i < statuses.GetArrayLength(); i++)
            results.Add(ParseOrderStatus(statuses[i], requests[i].ClientOrderId));
        return new BatchOrderResult(results);
    }

    public Task<PlaceOrderResult> PlaceLimitAsync(
        string symbol, OrderSide side, decimal price, decimal size,
        TimeInForce tif = TimeInForce.Gtc, bool reduceOnly = false,
        string? clientOrderId = null, CancellationToken ct = default)
        => PlaceAsync(new OrderRequest(
            Symbol: symbol, Side: side, OrderType: OrderType.Limit,
            Size: size, Price: price, TimeInForce: tif,
            ReduceOnly: reduceOnly, ClientOrderId: clientOrderId), ct);

    public async Task<PlaceOrderResult> PlaceMarketAsync(
        string symbol, OrderSide side, decimal size,
        bool reduceOnly = false, string? clientOrderId = null,
        CancellationToken ct = default)
    {
        // HyperLiquid has no native market order type — we send an IOC limit with 5% slippage
        // from the current mid price, matching the Python reference SDK's PlaceMarketOrder helper.
        var mids = await info.PostAsync<Dictionary<string, string>>(new { type = "allMids" }, ct).ConfigureAwait(false);
        if (!mids.TryGetValue(symbol, out var midStr))
            throw new ExchangeApiException($"Cannot place market order on '{symbol}': mid price unavailable.");

        var mid = decimal.Parse(midStr, NumberStyles.Float, CultureInfo.InvariantCulture);
        const decimal slippage = 0.05m; // 5%
        var price = side == OrderSide.Buy ? mid * (1m + slippage) : mid * (1m - slippage);

        return await PlaceAsync(new OrderRequest(
            Symbol: symbol, Side: side, OrderType: OrderType.Limit,
            Size: size, Price: price, TimeInForce: TimeInForce.Ioc,
            ReduceOnly: reduceOnly, ClientOrderId: clientOrderId), ct).ConfigureAwait(false);
    }

    public Task<PlaceOrderResult> PlaceStopAsync(
        string symbol, OrderSide side, decimal triggerPrice, decimal size,
        bool isMarket = true, bool reduceOnly = true,
        CancellationToken ct = default)
        => PlaceAsync(new OrderRequest(
            Symbol: symbol, Side: side,
            OrderType: isMarket ? OrderType.StopMarket : OrderType.StopLimit,
            Size: size, Price: triggerPrice, TriggerPrice: triggerPrice,
            ReduceOnly: reduceOnly), ct);

    public async Task<CancelResult> CancelAsync(string symbol, long orderId, CancellationToken ct = default)
    {
        var assetId = await meta.GetAssetIdAsync(symbol, ct).ConfigureAwait(false);
        var action = new HlMap()
            .Add("type", "cancel")
            .Add("cancels", new object[]
            {
                new HlMap().Add("a", assetId).Add("o", orderId),
            });

        var response = await exchange.SendL1Async(action, null, ct).ConfigureAwait(false);
        return ParseCancelStatus(response.GetProperty("data").GetProperty("statuses")[0], orderId);
    }

    public async Task<CancelResult> CancelByClientIdAsync(string symbol, string clientOrderId, CancellationToken ct = default)
    {
        var assetId = await meta.GetAssetIdAsync(symbol, ct).ConfigureAwait(false);
        var action = new HlMap()
            .Add("type", "cancelByCloid")
            .Add("cancels", new object[]
            {
                new HlMap().Add("asset", assetId).Add("cloid", clientOrderId),
            });

        var response = await exchange.SendL1Async(action, null, ct).ConfigureAwait(false);
        return ParseCancelStatus(response.GetProperty("data").GetProperty("statuses")[0], 0L);
    }

    public async Task<BatchCancelResult> CancelBatchAsync(IReadOnlyList<CancelRequest> requests, CancellationToken ct = default)
    {
        if (requests.Count == 0)
            return new BatchCancelResult(Array.Empty<CancelResult>());

        var cancels = new List<object>(requests.Count);
        var oids = new List<long>(requests.Count);
        foreach (var r in requests)
        {
            if (r.OrderId is null)
                throw new InvalidOrderException("CancelBatchAsync requires OrderId; use CancelByClientIdAsync for per-cloid cancellation.");
            var assetId = await meta.GetAssetIdAsync(r.Symbol, ct).ConfigureAwait(false);
            cancels.Add(new HlMap().Add("a", assetId).Add("o", r.OrderId.Value));
            oids.Add(r.OrderId.Value);
        }

        var action = new HlMap().Add("type", "cancel").Add("cancels", cancels);
        var response = await exchange.SendL1Async(action, null, ct).ConfigureAwait(false);

        var statuses = response.GetProperty("data").GetProperty("statuses");
        var results = new List<CancelResult>(requests.Count);
        for (var i = 0; i < statuses.GetArrayLength(); i++)
            results.Add(ParseCancelStatus(statuses[i], oids[i]));
        return new BatchCancelResult(results);
    }

    public async Task<int> CancelAllAsync(string? symbol = null, CancellationToken ct = default)
    {
        var open = await GetOpenAsync(symbol, ct).ConfigureAwait(false);
        if (open.Count == 0) return 0;

        var cancels = new List<object>(open.Count);
        foreach (var o in open)
        {
            var assetId = await meta.GetAssetIdAsync(o.Symbol, ct).ConfigureAwait(false);
            cancels.Add(new HlMap().Add("a", assetId).Add("o", o.OrderId));
        }

        var action = new HlMap().Add("type", "cancel").Add("cancels", cancels);
        await exchange.SendL1Async(action, null, ct).ConfigureAwait(false);
        return open.Count;
    }

    public async Task ScheduleCancelAsync(DateTimeOffset? at, CancellationToken ct = default)
    {
        var action = new HlMap().Add("type", "scheduleCancel");
        if (at.HasValue)
            action.Add("time", at.Value.ToUnixTimeMilliseconds());

        await exchange.SendL1Async(action, null, ct).ConfigureAwait(false);
    }

    // ─── Phase 3.1 (TWAP, Modify) — coming next ─────────────────────────────

    /// <summary>Phase 3.1 — modify currently requires the full new order spec; use <see cref="CancelAsync"/> + <see cref="PlaceAsync"/> as a workaround.</summary>
    public Task<ModifyResult> ModifyAsync(ModifyRequest request, CancellationToken ct = default)
        => Task.FromException<ModifyResult>(new NotImplementedException(HyperLiquidClient.WriteOpPhase31Message));

    public Task<BatchModifyResult> ModifyBatchAsync(IReadOnlyList<ModifyRequest> requests, CancellationToken ct = default)
        => Task.FromException<BatchModifyResult>(new NotImplementedException(HyperLiquidClient.WriteOpPhase31Message));

    public Task<TwapResult> PlaceTwapAsync(TwapRequest request, CancellationToken ct = default)
        => Task.FromException<TwapResult>(new NotImplementedException(HyperLiquidClient.WriteOpPhase31Message));

    public Task<CancelResult> CancelTwapAsync(string symbol, long twapId, CancellationToken ct = default)
        => Task.FromException<CancelResult>(new NotImplementedException(HyperLiquidClient.WriteOpPhase31Message));

    // ─── action builders ─────────────────────────────────────────────────────

    private static HlMap BuildOrderWire(int assetId, OrderRequest r)
    {
        var wire = new HlMap()
            .Add("a", assetId)
            .Add("b", r.Side == OrderSide.Buy)
            .Add("p", FloatToWire(r.Price ?? 0m))
            .Add("s", FloatToWire(r.Size))
            .Add("r", r.ReduceOnly)
            .Add("t", OrderTypeToWire(r.OrderType, r.TimeInForce, r.TriggerPrice));

        if (!string.IsNullOrEmpty(r.ClientOrderId))
            wire.Add("c", r.ClientOrderId);

        return wire;
    }

    private static HlMap OrderTypeToWire(OrderType orderType, TimeInForce tif, decimal? triggerPrice)
    {
        return orderType switch
        {
            OrderType.Limit or OrderType.Market =>
                new HlMap().Add("limit", new HlMap().Add("tif", TifToWire(tif))),

            OrderType.StopMarket =>
                new HlMap().Add("trigger", new HlMap()
                    .Add("triggerPx", FloatToWire(triggerPrice ?? 0m))
                    .Add("isMarket", true)
                    .Add("tpsl", "sl")),

            OrderType.StopLimit =>
                new HlMap().Add("trigger", new HlMap()
                    .Add("triggerPx", FloatToWire(triggerPrice ?? 0m))
                    .Add("isMarket", false)
                    .Add("tpsl", "sl")),

            OrderType.TakeProfit =>
                new HlMap().Add("trigger", new HlMap()
                    .Add("triggerPx", FloatToWire(triggerPrice ?? 0m))
                    .Add("isMarket", true)
                    .Add("tpsl", "tp")),

            _ => throw new InvalidOrderException(
                $"Order type '{orderType}' is not supported by HyperLiquid via this method."),
        };
    }

    private static string TifToWire(TimeInForce tif) => tif switch
    {
        TimeInForce.Gtc => "Gtc",
        TimeInForce.Ioc => "Ioc",
        TimeInForce.Alo => "Alo",
        TimeInForce.Fok => "Ioc", // HyperLiquid has no FOK; closest is IOC.
        _ => "Gtc",
    };

    private static string FloatToWire(decimal value)
    {
        if (value == 0m) return "0";
        var s = value.ToString("0.########", CultureInfo.InvariantCulture);
        return s == "-0" ? "0" : s;
    }

    private void AttachBuilderFee(HlMap action, BuilderFee? perOrderOverride)
    {
        var fee = perOrderOverride
            ?? options.BuilderFee
            ?? new BuilderFee(HlBuilderDefaults.BuilderAddress, HlBuilderDefaults.FeeRate);

        if (fee.FeeRate <= 0m)
            return; // explicit opt-out

        // HyperLiquid's `f` field is in tenths of a basis point (1 tenth = 0.00001).
        var wireFee = (int)Math.Round(fee.FeeRate * 100_000m, MidpointRounding.ToEven);
        if (wireFee <= 0) return;

        action.Add("builder", new HlMap()
            .Add("b", fee.BuilderAddress.ToLowerInvariant())
            .Add("f", wireFee));
    }

    // ─── response parsing ────────────────────────────────────────────────────

    private static PlaceOrderResult ParseOrderStatus(JsonElement status, string? clientOrderId)
    {
        if (status.TryGetProperty("resting", out var resting))
        {
            var oid = resting.GetProperty("oid").GetInt64();
            return new PlaceOrderResult(oid, clientOrderId, OrderStatus.Open, 0m, null, null);
        }

        if (status.TryGetProperty("filled", out var filled))
        {
            var oid = filled.GetProperty("oid").GetInt64();
            var totalSz = decimal.Parse(filled.GetProperty("totalSz").GetString()!, NumberStyles.Float, CultureInfo.InvariantCulture);
            var avgPx = decimal.Parse(filled.GetProperty("avgPx").GetString()!, NumberStyles.Float, CultureInfo.InvariantCulture);
            return new PlaceOrderResult(oid, clientOrderId, OrderStatus.Filled, totalSz, avgPx, null);
        }

        if (status.TryGetProperty("error", out var error))
        {
            return new PlaceOrderResult(0, clientOrderId, OrderStatus.Rejected, 0m, null, error.GetString());
        }

        return new PlaceOrderResult(0, clientOrderId, OrderStatus.Pending, 0m, null, status.GetRawText());
    }

    private static CancelResult ParseCancelStatus(JsonElement status, long orderId)
    {
        if (status.ValueKind == JsonValueKind.String)
        {
            var s = status.GetString();
            return new CancelResult(orderId, string.Equals(s, "success", StringComparison.OrdinalIgnoreCase), null);
        }

        if (status.TryGetProperty("error", out var err))
            return new CancelResult(orderId, false, err.GetString());

        return new CancelResult(orderId, true, null);
    }

    private string RequireUser() => options.Credentials?.MasterAddress
        ?? throw new AuthenticationException(
            "HyperLiquidClientOptions.Credentials.MasterAddress is required for order queries.");
}
