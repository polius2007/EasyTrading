using System.Globalization;
using System.Text.Json;
using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.Aster.Infrastructure;

namespace EasyTrading.Aster.Modules;

/// <summary>Aster implementation of <see cref="IOrders"/>. Reads + writes go through V3 Futures REST.</summary>
internal sealed class AsterOrders(AsterRestClient rest, AsterMetaCache meta) : IOrders
{
    // ─── Read methods (signed) ───────────────────────────────────────────────

    public async Task<IReadOnlyList<Order>> GetOpenAsync(string? symbol = null, CancellationToken ct = default)
    {
        var p = new Dictionary<string, string>();
        if (symbol is not null) p["symbol"] = symbol;
        var raw = await rest.SendSignedAsync<List<OrderResponseRaw>>(HttpMethod.Get, "/fapi/v3/openOrders", p, ct).ConfigureAwait(false);
        return raw.Select(AsterMapper.MapOrder).ToList();
    }

    public async Task<Order?> GetAsync(long orderId, CancellationToken ct = default)
    {
        // Aster's GET /fapi/v3/order requires `symbol` — when the caller doesn't have it, we
        // probe via openOrders and historical. The cleanest path is to ask the caller for symbol;
        // since the IOrders contract doesn't carry that, fall back to scanning all open + history.
        return await FindOrderAcrossSymbolsAsync(o => o.OrderId == orderId, ct).ConfigureAwait(false);
    }

    public async Task<Order?> GetByClientIdAsync(string clientOrderId, CancellationToken ct = default)
    {
        return await FindOrderAcrossSymbolsAsync(
            o => string.Equals(o.ClientOrderId, clientOrderId, StringComparison.Ordinal),
            ct).ConfigureAwait(false);
    }

    private async Task<Order?> FindOrderAcrossSymbolsAsync(Func<Order, bool> predicate, CancellationToken ct)
    {
        // Open first (cheapest).
        var open = await GetOpenAsync(ct: ct).ConfigureAwait(false);
        var hit = open.FirstOrDefault(predicate);
        if (hit is not null) return hit;

        // Then walk recent history.
        var hist = await GetHistoryAsync(ct: ct).ConfigureAwait(false);
        return hist.FirstOrDefault(predicate);
    }

    public async Task<IReadOnlyList<Order>> GetHistoryAsync(
        string? symbol = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var p = new Dictionary<string, string> { ["limit"] = "500" };
        if (symbol is not null) p["symbol"] = symbol;
        else throw new InvalidOrderException(
            "Aster's /fapi/v3/allOrders requires a symbol parameter. Pass `symbol` to GetHistoryAsync.");
        if (from is not null) p["startTime"] = from.Value.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        if (to   is not null) p["endTime"]   = to.Value.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

        var raw = await rest.SendSignedAsync<List<OrderResponseRaw>>(HttpMethod.Get, "/fapi/v3/allOrders", p, ct).ConfigureAwait(false);
        return raw.Select(AsterMapper.MapOrder).ToList();
    }

    public Task<IReadOnlyList<TwapSliceFill>> GetTwapFillsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TwapSliceFill>>(Array.Empty<TwapSliceFill>());

    // ─── Write methods (signed) ──────────────────────────────────────────────

    public async Task<PlaceOrderResult> PlaceAsync(OrderRequest request, CancellationToken ct = default)
    {
        var info = await meta.GetAsync(request.Symbol, ct).ConfigureAwait(false);
        AsterOrderValidator.Validate(info, request.Price ?? 0m, request.Size, request.ReduceOnly);

        var p = BuildOrderParams(request);
        var resp = await rest.SendSignedAsync<OrderResponseRaw>(HttpMethod.Post, "/fapi/v3/order", p, ct).ConfigureAwait(false);
        return MapOrderResult(resp);
    }

    private static PlaceOrderResult MapOrderResult(OrderResponseRaw resp) => new(
        OrderId:          resp.OrderId,
        ClientOrderId:    resp.ClientOrderId,
        Status:           AsterMapper.ParseStatus(resp.Status),
        FilledSize:       resp.ExecutedQty,
        AverageFillPrice: resp.AvgPrice > 0 ? resp.AvgPrice : null,
        ErrorMessage:     resp.Status == "REJECTED" ? "Aster rejected the order." : null);

    public Task<PlaceOrderResult> PlaceLimitAsync(
        string symbol, OrderSide side, decimal price, decimal size,
        TimeInForce tif = TimeInForce.Gtc, bool reduceOnly = false,
        string? clientOrderId = null, CancellationToken ct = default)
        => PlaceAsync(new OrderRequest(
            Symbol: symbol, Side: side, OrderType: OrderType.Limit,
            Size: size, Price: price, TimeInForce: tif,
            ReduceOnly: reduceOnly, ClientOrderId: clientOrderId), ct);

    public Task<PlaceOrderResult> PlaceMarketAsync(
        string symbol, OrderSide side, decimal size,
        bool reduceOnly = false, string? clientOrderId = null, CancellationToken ct = default)
        => PlaceAsync(new OrderRequest(
            Symbol: symbol, Side: side, OrderType: OrderType.Market,
            Size: size, Price: null, TimeInForce: TimeInForce.Ioc,
            ReduceOnly: reduceOnly, ClientOrderId: clientOrderId), ct);

    public Task<PlaceOrderResult> PlaceStopAsync(
        string symbol, OrderSide side, decimal triggerPrice, decimal size,
        bool isMarket = true, bool reduceOnly = true, CancellationToken ct = default)
        => PlaceAsync(new OrderRequest(
            Symbol: symbol, Side: side,
            OrderType: isMarket ? OrderType.StopMarket : OrderType.StopLimit,
            Size: size, Price: triggerPrice, TriggerPrice: triggerPrice,
            ReduceOnly: reduceOnly), ct);

    public async Task<BatchOrderResult> PlaceBatchAsync(IReadOnlyList<OrderRequest> requests, CancellationToken ct = default)
    {
        if (requests.Count == 0) return new BatchOrderResult(Array.Empty<PlaceOrderResult>());

        // Validate each + build the batch JSON array Aster expects in the `batchOrders` form field.
        var batch = new List<Dictionary<string, string>>(requests.Count);
        foreach (var r in requests)
        {
            var info = await meta.GetAsync(r.Symbol, ct).ConfigureAwait(false);
            AsterOrderValidator.Validate(info, r.Price ?? 0m, r.Size, r.ReduceOnly);
            batch.Add(BuildOrderParams(r));
        }

        var json = JsonSerializer.Serialize(batch, AsterJsonOptions.Default);
        var p = new Dictionary<string, string> { ["batchOrders"] = json };

        var arr = await rest.SendSignedRawAsync(HttpMethod.Post, "/fapi/v3/batchOrders", p, ct).ConfigureAwait(false);
        var results = new List<PlaceOrderResult>(requests.Count);
        if (arr.ValueKind == JsonValueKind.Array)
        {
            for (var i = 0; i < arr.GetArrayLength(); i++)
            {
                var el = arr[i];
                if (el.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() < 0)
                {
                    var msg = el.TryGetProperty("msg", out var m) ? m.GetString() : "rejected";
                    results.Add(new PlaceOrderResult(
                        OrderId:          0,
                        ClientOrderId:    requests[i].ClientOrderId,
                        Status:           OrderStatus.Rejected,
                        FilledSize:       0m,
                        AverageFillPrice: null,
                        ErrorMessage:     msg));
                }
                else
                {
                    var raw = el.Deserialize<OrderResponseRaw>(AsterJsonOptions.Default);
                    results.Add(raw is null
                        ? new PlaceOrderResult(0, null, OrderStatus.Rejected, 0m, null, "null response")
                        : MapOrderResult(raw));
                }
            }
        }
        return new BatchOrderResult(results);
    }

    public async Task<ModifyResult> ModifyAsync(ModifyRequest request, CancellationToken ct = default)
    {
        if (request.OrderId is null && request.ClientOrderId is null)
            throw new InvalidOrderException("ModifyRequest needs OrderId or ClientOrderId.");

        if (request.NewPrice is null || request.NewSize is null)
            throw new InvalidOrderException("Aster's PUT /fapi/v3/order requires BOTH NewPrice and NewSize.");

        var info = await meta.GetAsync(request.Symbol, ct).ConfigureAwait(false);
        AsterOrderValidator.Validate(info, request.NewPrice.Value, request.NewSize.Value, reduceOnly: false);

        var p = new Dictionary<string, string>
        {
            ["symbol"]   = request.Symbol,
            ["price"]    = Fmt(request.NewPrice.Value),
            ["quantity"] = Fmt(request.NewSize.Value),
        };
        if (request.OrderId is not null) p["orderId"] = request.OrderId.Value.ToString(CultureInfo.InvariantCulture);
        else                              p["origClientOrderId"] = request.ClientOrderId!;

        try
        {
            var resp = await rest.SendSignedAsync<OrderResponseRaw>(HttpMethod.Put, "/fapi/v3/order", p, ct).ConfigureAwait(false);
            return new ModifyResult(resp.OrderId, true, null);
        }
        catch (InvalidOrderException ex)
        {
            return new ModifyResult(request.OrderId ?? 0L, false, ex.Message);
        }
    }

    public async Task<BatchModifyResult> ModifyBatchAsync(IReadOnlyList<ModifyRequest> requests, CancellationToken ct = default)
    {
        // Aster doesn't expose a batch-modify endpoint in V3; sequential calls keep the contract.
        var results = new List<ModifyResult>(requests.Count);
        foreach (var r in requests)
            results.Add(await ModifyAsync(r, ct).ConfigureAwait(false));
        return new BatchModifyResult(results);
    }

    public async Task<CancelResult> CancelAsync(string symbol, long orderId, CancellationToken ct = default)
    {
        var p = new Dictionary<string, string>
        {
            ["symbol"]  = symbol,
            ["orderId"] = orderId.ToString(CultureInfo.InvariantCulture),
        };
        try
        {
            var resp = await rest.SendSignedAsync<OrderResponseRaw>(HttpMethod.Delete, "/fapi/v3/order", p, ct).ConfigureAwait(false);
            return new CancelResult(resp.OrderId, true, null);
        }
        catch (ExchangeApiException ex)
        {
            return new CancelResult(orderId, false, ex.Message);
        }
    }

    public async Task<CancelResult> CancelByClientIdAsync(string symbol, string clientOrderId, CancellationToken ct = default)
    {
        var p = new Dictionary<string, string>
        {
            ["symbol"]            = symbol,
            ["origClientOrderId"] = clientOrderId,
        };
        try
        {
            var resp = await rest.SendSignedAsync<OrderResponseRaw>(HttpMethod.Delete, "/fapi/v3/order", p, ct).ConfigureAwait(false);
            return new CancelResult(resp.OrderId, true, null);
        }
        catch (ExchangeApiException ex)
        {
            return new CancelResult(0L, false, ex.Message);
        }
    }

    public async Task<BatchCancelResult> CancelBatchAsync(IReadOnlyList<CancelRequest> requests, CancellationToken ct = default)
    {
        if (requests.Count == 0) return new BatchCancelResult(Array.Empty<CancelResult>());

        // Group by symbol — Aster's DELETE /fapi/v3/batchOrders accepts arrays of orderId / origClientOrderId per symbol.
        var grouped = requests.GroupBy(r => r.Symbol);
        var results = new List<CancelResult>();
        foreach (var g in grouped)
        {
            var withOid = g.Where(r => r.OrderId is not null).Select(r => r.OrderId!.Value).ToList();
            var withCid = g.Where(r => r.OrderId is null && r.ClientOrderId is not null).Select(r => r.ClientOrderId!).ToList();

            var p = new Dictionary<string, string> { ["symbol"] = g.Key };
            if (withOid.Count > 0)
                p["orderIdList"] = "[" + string.Join(",", withOid.Select(x => x.ToString(CultureInfo.InvariantCulture))) + "]";
            if (withCid.Count > 0)
                p["origClientOrderIdList"] = "[" + string.Join(",", withCid.Select(c => "\"" + c + "\"")) + "]";

            var arr = await rest.SendSignedRawAsync(HttpMethod.Delete, "/fapi/v3/batchOrders", p, ct).ConfigureAwait(false);
            if (arr.ValueKind == JsonValueKind.Array)
            {
                for (var i = 0; i < arr.GetArrayLength(); i++)
                {
                    var el = arr[i];
                    if (el.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() < 0)
                    {
                        var msg = el.TryGetProperty("msg", out var m) ? m.GetString() : "rejected";
                        results.Add(new CancelResult(0L, false, msg));
                    }
                    else
                    {
                        var oid = el.TryGetProperty("orderId", out var oidEl) ? oidEl.GetInt64() : 0L;
                        results.Add(new CancelResult(oid, true, null));
                    }
                }
            }
        }
        return new BatchCancelResult(results);
    }

    public async Task<int> CancelAllAsync(string? symbol = null, CancellationToken ct = default)
    {
        if (symbol is not null)
        {
            var p = new Dictionary<string, string> { ["symbol"] = symbol };
            await rest.SendSignedRawAsync(HttpMethod.Delete, "/fapi/v3/allOpenOrders", p, ct).ConfigureAwait(false);
            return 1; // Aster's endpoint doesn't tell us how many were cancelled. Return a non-zero positive.
        }

        // Without a symbol: fetch open orders, group by symbol, cancel each group.
        var open = await GetOpenAsync(ct: ct).ConfigureAwait(false);
        var symbols = open.Select(o => o.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var s in symbols)
        {
            var p = new Dictionary<string, string> { ["symbol"] = s };
            await rest.SendSignedRawAsync(HttpMethod.Delete, "/fapi/v3/allOpenOrders", p, ct).ConfigureAwait(false);
        }
        return open.Count;
    }

    public async Task ScheduleCancelAsync(DateTimeOffset? at, CancellationToken ct = default)
    {
        // Aster's countdownCancelAll is a per-symbol auto-cancel; without a symbol the contract
        // doesn't translate cleanly. We treat `at` as a per-account "cancel after this delay"
        // and apply it to every open symbol.
        var countdown = at.HasValue
            ? Math.Max(0L, (long)(at.Value - DateTimeOffset.UtcNow).TotalMilliseconds)
            : 0L;

        var open = await GetOpenAsync(ct: ct).ConfigureAwait(false);
        var symbols = open.Select(o => o.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (symbols.Count == 0) return;

        foreach (var s in symbols)
        {
            var p = new Dictionary<string, string>
            {
                ["symbol"]        = s,
                ["countdownTime"] = countdown.ToString(CultureInfo.InvariantCulture),
            };
            await rest.SendSignedRawAsync(HttpMethod.Post, "/fapi/v3/countdownCancelAll", p, ct).ConfigureAwait(false);
        }
    }

    // ─── TWAP — not in Aster's V3 surface ────────────────────────────────────

    public Task<TwapResult> PlaceTwapAsync(TwapRequest request, CancellationToken ct = default)
        => throw new NotSupportedException("Aster's V3 API does not expose a TWAP order type.");

    public Task<CancelResult> CancelTwapAsync(string symbol, long twapId, CancellationToken ct = default)
        => throw new NotSupportedException("Aster's V3 API does not expose a TWAP order type.");

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static Dictionary<string, string> BuildOrderParams(OrderRequest r)
    {
        var p = new Dictionary<string, string>
        {
            ["symbol"] = r.Symbol,
            ["side"]   = r.Side == OrderSide.Buy ? "BUY" : "SELL",
            ["type"]   = TypeToWire(r.OrderType),
        };
        if (r.Size > 0) p["quantity"] = Fmt(r.Size);
        if (r.Price.HasValue && r.Price.Value > 0)
        {
            if (r.OrderType is OrderType.Limit or OrderType.StopLimit or OrderType.TakeProfit)
                p["price"] = Fmt(r.Price.Value);
        }
        if (r.TriggerPrice.HasValue && r.TriggerPrice.Value > 0)
            p["stopPrice"] = Fmt(r.TriggerPrice.Value);

        // Aster forbids reduceOnly in Hedge mode; default mode is One-way so it's safe.
        if (r.ReduceOnly) p["reduceOnly"] = "true";

        // Limit + stop/take-profit orders need TIF. Market doesn't take a TIF (Aster will ignore).
        if (r.OrderType is OrderType.Limit or OrderType.StopLimit or OrderType.TakeProfit)
            p["timeInForce"] = TifToWire(r.TimeInForce);

        if (!string.IsNullOrEmpty(r.ClientOrderId))
            p["newClientOrderId"] = r.ClientOrderId!;

        return p;
    }

    private static string TypeToWire(OrderType t) => t switch
    {
        OrderType.Limit       => "LIMIT",
        OrderType.Market      => "MARKET",
        OrderType.StopLimit   => "STOP",
        OrderType.StopMarket  => "STOP_MARKET",
        OrderType.TakeProfit  => "TAKE_PROFIT_MARKET",
        _ => throw new InvalidOrderException($"Order type '{t}' is not supported by Aster."),
    };

    private static string TifToWire(TimeInForce tif) => tif switch
    {
        TimeInForce.Gtc => "GTC",
        TimeInForce.Ioc => "IOC",
        TimeInForce.Fok => "FOK",
        TimeInForce.Alo => "GTX", // Good-Till-Crossing = post-only
        _               => "GTC",
    };

    private static string Fmt(decimal d) => d.ToString("0.##########", CultureInfo.InvariantCulture);
}
