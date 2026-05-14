using System.Globalization;
using Cosmos.Base.V1Beta1;
using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.Dydx.Infrastructure;
using ProtoOrder    = Dydxprotocol.Clob.Order;
using ProtoOrderId  = Dydxprotocol.Clob.OrderId;
using MsgPlaceOrder = Dydxprotocol.Clob.MsgPlaceOrder;
using MsgCancelOrder = Dydxprotocol.Clob.MsgCancelOrder;
using SubaccountId  = Dydxprotocol.Subaccounts.SubaccountId;

namespace EasyTrading.Dydx.Modules;

/// <summary>
/// dYdX implementation of <see cref="IOrders"/>. Reads come from the Indexer's <c>/orders</c>
/// endpoint; writes build + sign Cosmos transactions via <see cref="TransactionBuilder"/> and
/// broadcast through the validator REST gateway.
/// </summary>
internal sealed class Orders(
    RestClient rest,
    DydxClientOptions options,
    MarketsCache markets,
    TransactionBuilder? txBuilder,
    CosmosClient? cosmos) : IOrders
{
    // ─── Reads ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<Order>> GetOpenAsync(string? symbol = null, CancellationToken ct = default)
    {
        var creds = RequireCreds();
        var query = new Dictionary<string, string>
        {
            ["address"]          = creds.Address,
            ["subaccountNumber"] = creds.SubaccountNumber.ToString(CultureInfo.InvariantCulture),
            ["status"]           = "OPEN",
            ["limit"]            = "1000",
        };
        if (symbol is not null) query["ticker"] = symbol;

        var raw = await rest.GetAsync<List<OrderRaw>>("orders", query, ct).ConfigureAwait(false);
        return raw.Select(Mapper.MapOrder).ToList();
    }

    public async Task<Order?> GetAsync(long orderId, CancellationToken ct = default)
    {
        var open = await GetOpenAsync(ct: ct).ConfigureAwait(false);
        var hit = open.FirstOrDefault(o => o.OrderId == orderId);
        if (hit is not null) return hit;
        var hist = await GetHistoryAsync(ct: ct).ConfigureAwait(false);
        return hist.FirstOrDefault(o => o.OrderId == orderId);
    }

    public async Task<Order?> GetByClientIdAsync(string clientOrderId, CancellationToken ct = default)
    {
        var open = await GetOpenAsync(ct: ct).ConfigureAwait(false);
        var hit = open.FirstOrDefault(o => string.Equals(o.ClientOrderId, clientOrderId, StringComparison.Ordinal));
        if (hit is not null) return hit;
        var hist = await GetHistoryAsync(ct: ct).ConfigureAwait(false);
        return hist.FirstOrDefault(o => string.Equals(o.ClientOrderId, clientOrderId, StringComparison.Ordinal));
    }

    public async Task<IReadOnlyList<Order>> GetHistoryAsync(
        string? symbol = null, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var creds = RequireCreds();
        var query = new Dictionary<string, string>
        {
            ["address"]          = creds.Address,
            ["subaccountNumber"] = creds.SubaccountNumber.ToString(CultureInfo.InvariantCulture),
            ["limit"]            = "1000",
        };
        if (symbol is not null) query["ticker"] = symbol;

        var raw = await rest.GetAsync<List<OrderRaw>>("orders", query, ct).ConfigureAwait(false);
        IEnumerable<Order> mapped = raw.Select(Mapper.MapOrder);
        if (from is not null) mapped = mapped.Where(o => o.UpdatedAt >= from.Value);
        if (to   is not null) mapped = mapped.Where(o => o.UpdatedAt < to.Value);
        return mapped.ToList();
    }

    public Task<IReadOnlyList<TwapSliceFill>> GetTwapFillsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TwapSliceFill>>(Array.Empty<TwapSliceFill>());

    // ─── Writes — Cosmos SDK signed transactions ────────────────────────────

    public Task<PlaceOrderResult> PlaceAsync(OrderRequest request, CancellationToken ct = default)
        => request.OrderType switch
        {
            OrderType.Limit  => PlaceLimitAsync(request.Symbol, request.Side, request.Price ?? 0m, request.Size, request.TimeInForce, request.ReduceOnly, request.ClientOrderId, ct),
            OrderType.Market => PlaceMarketAsync(request.Symbol, request.Side, request.Size, request.ReduceOnly, request.ClientOrderId, ct),
            _ => throw new InvalidOrderException($"Order type '{request.OrderType}' is not currently supported on dYdX v4. Use Limit, Market, or call /fapi/v3/order via Phase 7.2.1 (planned)."),
        };

    public async Task<PlaceOrderResult> PlaceLimitAsync(
        string symbol, OrderSide side, decimal price, decimal size,
        TimeInForce tif = TimeInForce.Gtc, bool reduceOnly = false,
        string? clientOrderId = null, CancellationToken ct = default)
    {
        var (creds, builder, cosmosClient) = RequireWriteContext();
        var info = await markets.GetAsync(symbol, ct).ConfigureAwait(false);

        var msg = BuildPlaceOrderMsg(creds, info, side, price, size, tif, reduceOnly, clientOrderId);

        var (accNum, sequence) = await cosmosClient.GetAccountAsync(creds.Address, ct).ConfigureAwait(false);
        var txBytes = builder.BuildAndSign(
            messages: new[] { TransactionBuilder.PackAny(msg) },
            accountNumber: accNum,
            sequence: sequence,
            fee: Array.Empty<Coin>(),    // dYdX waives the Cosmos fee for trading messages
            gasLimit: 1_000_000UL);

        var result = await cosmosClient.BroadcastAsync(txBytes, "BROADCAST_MODE_SYNC", ct).ConfigureAwait(false);

        return new PlaceOrderResult(
            OrderId:          result.TxHash is null ? 0L : Mapper.StableLongFromString(result.TxHash),
            ClientOrderId:    clientOrderId ?? msg.Order.OrderId.ClientId.ToString(CultureInfo.InvariantCulture),
            Status:           result.Success ? OrderStatus.Pending : OrderStatus.Rejected,
            FilledSize:       0m,
            AverageFillPrice: null,
            ErrorMessage:     result.ErrorMessage);
    }

    public Task<PlaceOrderResult> PlaceMarketAsync(
        string symbol, OrderSide side, decimal size,
        bool reduceOnly = false, string? clientOrderId = null, CancellationToken ct = default)
        // dYdX v4 has no MARKET order type; we emulate via IOC + a very aggressive limit price.
        // The Indexer's /perpetualMarkets oracle price gives us a reasonable mid; we apply 5%
        // slippage in the order's favour. Same convention HL/Aster use.
        => Task.FromException<PlaceOrderResult>(new NotImplementedException(
            "PlaceMarketAsync pending Phase 7.2.1 — needs MarketsCache extension for live oracle price + slippage helper."));

    public Task<PlaceOrderResult> PlaceStopAsync(
        string symbol, OrderSide side, decimal triggerPrice, decimal size,
        bool isMarket = true, bool reduceOnly = true, CancellationToken ct = default)
        => Task.FromException<PlaceOrderResult>(new NotImplementedException(
            "Conditional orders pending Phase 7.2.1 — needs CONDITIONAL order_flags wiring."));

    public Task<BatchOrderResult> PlaceBatchAsync(IReadOnlyList<OrderRequest> requests, CancellationToken ct = default)
        => Task.FromException<BatchOrderResult>(new NotSupportedException(
            "dYdX v4 doesn't support multi-message order batches in a single transaction the way HL/Aster do — submit orders sequentially."));

    public Task<ModifyResult> ModifyAsync(ModifyRequest request, CancellationToken ct = default)
        => Task.FromException<ModifyResult>(new NotSupportedException(
            "dYdX v4 has no native modify — cancel and re-place. See Orders.CancelAsync + PlaceLimitAsync."));

    public async Task<BatchModifyResult> ModifyBatchAsync(IReadOnlyList<ModifyRequest> requests, CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        throw new NotSupportedException("dYdX v4 has no modify — cancel and re-place.");
    }

    public async Task<CancelResult> CancelAsync(string symbol, long orderId, CancellationToken ct = default)
    {
        // dYdX cancels are stateless: they reference the OrderId triplet (subaccount + clientId +
        // clobPairId), not the cross-DEX numeric orderId. We can't reverse the stable-long hash
        // — caller needs to track their own client_id, or use CancelByClientIdAsync.
        await Task.CompletedTask.ConfigureAwait(false);
        throw new NotSupportedException(
            "CancelAsync(long orderId) is not supported on dYdX v4 — its OrderId is a (subaccount, client_id, order_flags, clob_pair_id) tuple, not a single integer. "
            + "Use CancelByClientIdAsync with the client_id you supplied at submission.");
    }

    public async Task<CancelResult> CancelByClientIdAsync(string symbol, string clientOrderId, CancellationToken ct = default)
    {
        var (creds, builder, cosmosClient) = RequireWriteContext();
        var info = await markets.GetAsync(symbol, ct).ConfigureAwait(false);

        if (!uint.TryParse(clientOrderId, NumberStyles.None, CultureInfo.InvariantCulture, out var clientId))
            throw new InvalidOrderException(
                $"dYdX requires a numeric uint32 client_id for cancel; '{clientOrderId}' is not a valid uint.");

        var orderId = new ProtoOrderId
        {
            SubaccountId = new SubaccountId { Owner = creds.Address, Number = (uint)creds.SubaccountNumber },
            ClientId     = clientId,
            OrderFlags   = OrderFlagsLongTerm,
            ClobPairId   = info.ClobPairId,
        };

        var msg = new MsgCancelOrder
        {
            OrderId             = orderId,
            GoodTilBlockTime    = (uint)DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds(),
        };

        var (accNum, sequence) = await cosmosClient.GetAccountAsync(creds.Address, ct).ConfigureAwait(false);
        var txBytes = builder.BuildAndSign(
            messages: new[] { TransactionBuilder.PackAny(msg) },
            accountNumber: accNum,
            sequence: sequence,
            fee: Array.Empty<Coin>(),
            gasLimit: 1_000_000UL);

        var result = await cosmosClient.BroadcastAsync(txBytes, "BROADCAST_MODE_SYNC", ct).ConfigureAwait(false);
        return new CancelResult(
            OrderId:      0L,
            Success:      result.Success,
            ErrorMessage: result.ErrorMessage);
    }

    public Task<BatchCancelResult> CancelBatchAsync(IReadOnlyList<CancelRequest> requests, CancellationToken ct = default)
        => Task.FromException<BatchCancelResult>(new NotSupportedException(
            "dYdX v4 doesn't support batched cancels — submit one CancelByClientIdAsync per order."));

    public Task<int> CancelAllAsync(string? symbol = null, CancellationToken ct = default)
        => Task.FromException<int>(new NotImplementedException(
            "CancelAllAsync pending Phase 7.2.1 — needs to enumerate open orders + issue one cancel per."));

    public Task ScheduleCancelAsync(DateTimeOffset? at, CancellationToken ct = default)
        => Task.FromException(new NotSupportedException("dYdX v4 has no dead-man switch / scheduled cancel."));

    public Task<TwapResult> PlaceTwapAsync(TwapRequest request, CancellationToken ct = default)
        => Task.FromException<TwapResult>(new NotSupportedException("dYdX v4 does not offer a native TWAP order type."));

    public Task<CancelResult> CancelTwapAsync(string symbol, long twapId, CancellationToken ct = default)
        => Task.FromException<CancelResult>(new NotSupportedException("dYdX v4 does not offer a native TWAP order type."));

    // ─── helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// dYdX <c>order_flags</c> values. <c>0</c> is the SHORT_TERM placeholder; <c>64</c> = LONG_TERM
    /// (good_til_block_time-based). LONG_TERM is what we use for resting orders — it doesn't need
    /// the current block height the way SHORT_TERM does.
    /// </summary>
    private const uint OrderFlagsLongTerm = 64;

    /// <summary>Build a fully-populated <see cref="MsgPlaceOrder"/> for the LONG_TERM order flow.</summary>
    private static MsgPlaceOrder BuildPlaceOrderMsg(
        DydxCredentials creds, MarketInfo market, OrderSide side, decimal price, decimal size,
        TimeInForce tif, bool reduceOnly, string? clientOrderIdStr)
    {
        // dYdX client_id is uint32. Accept the caller's value if they supplied one, otherwise
        // synthesise from a random 32-bit seed seeded by time + size + price so it's stable
        // within a single call but unlikely to collide across concurrent orders.
        uint clientId;
        if (clientOrderIdStr is not null && uint.TryParse(clientOrderIdStr, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            clientId = parsed;
        else
            clientId = (uint)Random.Shared.NextInt64(0, uint.MaxValue);

        var orderId = new ProtoOrderId
        {
            SubaccountId = new SubaccountId { Owner = creds.Address, Number = (uint)creds.SubaccountNumber },
            ClientId     = clientId,
            OrderFlags   = OrderFlagsLongTerm,
            ClobPairId   = market.ClobPairId,
        };

        var order = new ProtoOrder
        {
            OrderId          = orderId,
            Side             = side == OrderSide.Buy ? ProtoOrder.Types.Side.Buy : ProtoOrder.Types.Side.Sell,
            Quantums         = market.ToQuantums(size),
            Subticks         = market.ToSubticks(price),
            GoodTilBlockTime = (uint)DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds(),
            TimeInForce      = MapTif(tif),
            ReduceOnly       = reduceOnly,
        };

        return new MsgPlaceOrder { Order = order };
    }

    private static ProtoOrder.Types.TimeInForce MapTif(TimeInForce tif) => tif switch
    {
        TimeInForce.Ioc => ProtoOrder.Types.TimeInForce.Ioc,
        // dYdX deprecated their dedicated FILL_OR_KILL enum value; IOC is the closest
        // remaining semantics (fill what you can, cancel the rest). Callers that need a
        // strict all-or-nothing fill should use SHORT_TERM with explicit fill_or_kill flags
        // via the Phase 7.2.1 extension once it lands.
        TimeInForce.Fok => ProtoOrder.Types.TimeInForce.Ioc,
        TimeInForce.Alo => ProtoOrder.Types.TimeInForce.PostOnly,
        _               => ProtoOrder.Types.TimeInForce.Unspecified,  // GTC == default for LONG_TERM
    };

    private DydxCredentials RequireCreds() => options.Credentials
        ?? throw new AuthenticationException(
            "DydxClientOptions.Credentials.Address is required for order queries.");

    private (DydxCredentials Creds, TransactionBuilder Builder, CosmosClient Cosmos) RequireWriteContext()
    {
        var creds = RequireCreds();
        if (txBuilder is null || cosmos is null)
            throw new AuthenticationException(
                "DydxClientOptions.Credentials.Mnemonic is required for signed Cosmos transactions.");
        return (creds, txBuilder, cosmos);
    }
}
