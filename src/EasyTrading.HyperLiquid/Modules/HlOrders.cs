using System.Collections.Concurrent;
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
    // In-memory cache: which (user, builder) pairs have already been approved this process.
    // Avoids a per-order maxBuilderFee round-trip after the first.
    private static readonly ConcurrentDictionary<string, bool> _builderApproved = new();

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

    // ─── Place ───────────────────────────────────────────────────────────────

    public async Task<PlaceOrderResult> PlaceAsync(OrderRequest request, CancellationToken ct = default)
    {
        var info = await meta.GetAssetInfoAsync(request.Symbol, ct).ConfigureAwait(false);
        HlOrderValidator.Validate(request.Symbol, request.Price ?? 0m, request.Size, info, request.ReduceOnly);
        var orderWire = BuildOrderWire(info.AssetId, request);

        var action = new HlMap()
            .Add("type", "order")
            .Add("orders", new object[] { orderWire })
            .Add("grouping", "na");
        await AttachBuilderFeeAsync(action, request.BuilderFeeOverride, ct).ConfigureAwait(false);

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
            var info = await meta.GetAssetInfoAsync(r.Symbol, ct).ConfigureAwait(false);
            HlOrderValidator.Validate(r.Symbol, r.Price ?? 0m, r.Size, info, r.ReduceOnly);
            wires.Add(BuildOrderWire(info.AssetId, r));
        }

        var action = new HlMap()
            .Add("type", "order")
            .Add("orders", wires)
            .Add("grouping", "na");
        await AttachBuilderFeeAsync(action, perOrderOverride: null, ct).ConfigureAwait(false);

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
        var mids = await info.PostAsync<Dictionary<string, string>>(new { type = "allMids" }, ct).ConfigureAwait(false);
        if (!mids.TryGetValue(symbol, out var midStr))
            throw new ExchangeApiException($"Cannot place market order on '{symbol}': mid price unavailable.");

        var mid = decimal.Parse(midStr, NumberStyles.Float, CultureInfo.InvariantCulture);
        const decimal slippage = 0.05m;
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

    // ─── Modify ──────────────────────────────────────────────────────────────

    public async Task<ModifyResult> ModifyAsync(ModifyRequest request, CancellationToken ct = default)
    {
        if (request.OrderId is null && request.ClientOrderId is null)
            throw new InvalidOrderException("ModifyRequest needs OrderId or ClientOrderId.");

        var existing = request.OrderId.HasValue
            ? await GetAsync(request.OrderId.Value, ct).ConfigureAwait(false)
            : await GetByClientIdAsync(request.ClientOrderId!, ct).ConfigureAwait(false);

        if (existing is null)
            return new ModifyResult(0, false, "Order not found or no longer open.");

        var info = await meta.GetAssetInfoAsync(request.Symbol, ct).ConfigureAwait(false);
        var newPrice = request.NewPrice ?? existing.Price ?? 0m;
        var newSize = request.NewSize ?? (existing.Size - existing.FilledSize);
        HlOrderValidator.Validate(request.Symbol, newPrice, newSize, info, existing.ReduceOnly);
        var newOrderWire = BuildModifyOrderWire(info.AssetId, existing, request);

        var action = new HlMap()
            .Add("type", "modify")
            .Add("oid", existing.OrderId)
            .Add("order", newOrderWire);

        var response = await exchange.SendL1Async(action, null, ct).ConfigureAwait(false);
        var status = response.GetProperty("data").GetProperty("statuses")[0];

        if (status.TryGetProperty("resting", out var resting))
            return new ModifyResult(resting.GetProperty("oid").GetInt64(), true, null);
        if (status.TryGetProperty("error", out var err))
            return new ModifyResult(existing.OrderId, false, err.GetString());
        return new ModifyResult(existing.OrderId, true, null);
    }

    public async Task<BatchModifyResult> ModifyBatchAsync(IReadOnlyList<ModifyRequest> requests, CancellationToken ct = default)
    {
        if (requests.Count == 0)
            return new BatchModifyResult(Array.Empty<ModifyResult>());

        var modifies = new List<object>(requests.Count);
        var oids = new List<long>(requests.Count);

        foreach (var r in requests)
        {
            if (r.OrderId is null && r.ClientOrderId is null)
                throw new InvalidOrderException("ModifyRequest needs OrderId or ClientOrderId.");
            var existing = r.OrderId.HasValue
                ? await GetAsync(r.OrderId.Value, ct).ConfigureAwait(false)
                : await GetByClientIdAsync(r.ClientOrderId!, ct).ConfigureAwait(false);

            if (existing is null)
                throw new InvalidOrderException($"Cannot modify: order '{r.OrderId?.ToString(CultureInfo.InvariantCulture) ?? r.ClientOrderId}' not found.");

            var info = await meta.GetAssetInfoAsync(r.Symbol, ct).ConfigureAwait(false);
            var newPrice = r.NewPrice ?? existing.Price ?? 0m;
            var newSize = r.NewSize ?? (existing.Size - existing.FilledSize);
            HlOrderValidator.Validate(r.Symbol, newPrice, newSize, info, existing.ReduceOnly);
            modifies.Add(new HlMap()
                .Add("oid", existing.OrderId)
                .Add("order", BuildModifyOrderWire(info.AssetId, existing, r)));
            oids.Add(existing.OrderId);
        }

        var action = new HlMap()
            .Add("type", "batchModify")
            .Add("modifies", modifies);

        var response = await exchange.SendL1Async(action, null, ct).ConfigureAwait(false);
        var statuses = response.GetProperty("data").GetProperty("statuses");

        var results = new List<ModifyResult>(requests.Count);
        for (var i = 0; i < statuses.GetArrayLength(); i++)
        {
            var s = statuses[i];
            if (s.TryGetProperty("resting", out var resting))
                results.Add(new ModifyResult(resting.GetProperty("oid").GetInt64(), true, null));
            else if (s.TryGetProperty("error", out var err))
                results.Add(new ModifyResult(oids[i], false, err.GetString()));
            else
                results.Add(new ModifyResult(oids[i], true, null));
        }
        return new BatchModifyResult(results);
    }

    // ─── Cancel ──────────────────────────────────────────────────────────────

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

    // ─── TWAP ────────────────────────────────────────────────────────────────

    public async Task<TwapResult> PlaceTwapAsync(TwapRequest request, CancellationToken ct = default)
    {
        var info = await meta.GetAssetInfoAsync(request.Symbol, ct).ConfigureAwait(false);
        // TWAP has no client-supplied price (HL computes the slice price), so we pass 0
        // to skip the price-rule and min-notional checks. Size + szDecimals still apply.
        HlOrderValidator.Validate(request.Symbol, price: 0m, request.Size, info, request.ReduceOnly);

        var twapWire = new HlMap()
            .Add("a", info.AssetId)
            .Add("b", request.Side == OrderSide.Buy)
            .Add("s", FloatToWire(request.Size))
            .Add("r", request.ReduceOnly)
            .Add("m", request.DurationMinutes)
            .Add("t", request.Randomize);

        var action = new HlMap()
            .Add("type", "twapOrder")
            .Add("twap", twapWire);

        var response = await exchange.SendL1Async(action, null, ct).ConfigureAwait(false);
        var status = response.GetProperty("data").GetProperty("status");

        if (status.TryGetProperty("running", out var running))
        {
            var twapId = running.GetProperty("twapId").GetInt64();
            return new TwapResult(twapId, true, null);
        }
        if (status.TryGetProperty("error", out var err))
            return new TwapResult(0, false, err.GetString());

        return new TwapResult(0, true, null);
    }

    public async Task<CancelResult> CancelTwapAsync(string symbol, long twapId, CancellationToken ct = default)
    {
        var assetId = await meta.GetAssetIdAsync(symbol, ct).ConfigureAwait(false);

        var action = new HlMap()
            .Add("type", "twapCancel")
            .Add("a", assetId)
            .Add("t", twapId);

        var response = await exchange.SendL1Async(action, null, ct).ConfigureAwait(false);
        var status = response.GetProperty("data").GetProperty("status");

        if (status.ValueKind == JsonValueKind.String && string.Equals(status.GetString(), "success", StringComparison.OrdinalIgnoreCase))
            return new CancelResult(twapId, true, null);
        if (status.TryGetProperty("error", out var err))
            return new CancelResult(twapId, false, err.GetString());

        return new CancelResult(twapId, true, null);
    }

    // ─── action wire builders ────────────────────────────────────────────────

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

    private static HlMap BuildModifyOrderWire(int assetId, Order existing, ModifyRequest req)
    {
        var newPrice = req.NewPrice ?? existing.Price ?? 0m;
        var newSize  = req.NewSize  ?? (existing.Size - existing.FilledSize);

        var wire = new HlMap()
            .Add("a", assetId)
            .Add("b", existing.Side == OrderSide.Buy)
            .Add("p", FloatToWire(newPrice))
            .Add("s", FloatToWire(newSize))
            .Add("r", existing.ReduceOnly)
            .Add("t", OrderTypeToWire(existing.OrderType, existing.TimeInForce, existing.TriggerPrice));

        if (!string.IsNullOrEmpty(existing.ClientOrderId))
            wire.Add("c", existing.ClientOrderId);

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
        TimeInForce.Fok => "Ioc", // HL has no FOK; closest is IOC.
        _ => "Gtc",
    };

    private static string FloatToWire(decimal value)
    {
        if (value == 0m) return "0";
        var s = value.ToString("0.########", CultureInfo.InvariantCulture);
        return s == "-0" ? "0" : s;
    }

    // ─── builder fee (auto-attach + auto-approve) ────────────────────────────

    private async Task AttachBuilderFeeAsync(HlMap action, BuilderFee? perOrderOverride, CancellationToken ct)
    {
        var fee = perOrderOverride
            ?? options.BuilderFee
            ?? new BuilderFee(HlBuilderDefaults.BuilderAddress, HlBuilderDefaults.FeeRate);

        if (fee.FeeRate <= 0m)
            return; // explicit opt-out

        var wireFee = (int)Math.Round(fee.FeeRate * 100_000m, MidpointRounding.ToEven);
        if (wireFee <= 0) return;

        // Make sure the user has approved this builder once. Cached in-process; first call may
        // send an approveBuilderFee transaction transparently.
        await EnsureBuilderApprovedAsync(fee, wireFee, ct).ConfigureAwait(false);

        action.Add("builder", new HlMap()
            .Add("b", fee.BuilderAddress.ToLowerInvariant())
            .Add("f", wireFee));
    }

    private async Task EnsureBuilderApprovedAsync(BuilderFee fee, int requiredWireFee, CancellationToken ct)
    {
        var user = options.Credentials?.MasterAddress;
        if (user is null) return; // no creds → no signing happens, error surfaces later from exchange

        var cacheKey = $"{user.ToLowerInvariant()}:{fee.BuilderAddress.ToLowerInvariant()}";
        if (_builderApproved.TryGetValue(cacheKey, out var ok) && ok)
            return;

        // maxBuilderFee returns the currently-approved tenths-of-bp ceiling for (user, builder).
        decimal currentMax;
        try
        {
            currentMax = await info.PostAsync<decimal>(new
            {
                type = "maxBuilderFee",
                user,
                builder = fee.BuilderAddress.ToLowerInvariant(),
            }, ct).ConfigureAwait(false);
        }
        catch
        {
            // If the check fails, fall through and attempt to approve.
            currentMax = 0m;
        }

        if ((int)currentMax >= requiredWireFee)
        {
            _builderApproved[cacheKey] = true;
            return;
        }

        // Send a user-signed approveBuilderFee action with the required rate as percent string.
        var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var maxFeeRatePct = (fee.FeeRate * 100m).ToString("0.######", CultureInfo.InvariantCulture) + "%";

        var approve = new HlMap()
            .Add("type", "approveBuilderFee")
            .Add("maxFeeRate", maxFeeRatePct)
            .Add("builder", fee.BuilderAddress.ToLowerInvariant())
            .Add("nonce", nonce);

        var schema = new (string Name, string Type)[]
        {
            ("hyperliquidChain", "string"),
            ("maxFeeRate",        "string"),
            ("builder",           "address"),
            ("nonce",             "uint64"),
        };

        await exchange.SendUserAsync(approve, "ApproveBuilderFee", schema, ct).ConfigureAwait(false);
        _builderApproved[cacheKey] = true;
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
            return new PlaceOrderResult(0, clientOrderId, OrderStatus.Rejected, 0m, null, error.GetString());

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
