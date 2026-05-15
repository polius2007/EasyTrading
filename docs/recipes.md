# Recipes

Common patterns when working with EasyTrading. For a guided first-trade walk-through see [getting-started.md](getting-started.md); for API details see the [DocFX API reference](https://polius2007.github.io/EasyTrading/).

## Table of contents

- [Place a limit order (post-only)](#place-a-limit-order-post-only)
- [Place a market order](#place-a-market-order)
- [Stop-loss + take-profit](#stop-loss--take-profit)
- [Batch place / cancel](#batch-place--cancel)
- [Modify an open order](#modify-an-open-order)
- [Cancel everything on shutdown](#cancel-everything-on-shutdown)
- [Read account state](#read-account-state)
- [Set leverage and close a position](#set-leverage-and-close-a-position)
- [Transfers and approvals](#transfers-and-approvals)
- [Subscribe to public streams](#subscribe-to-public-streams)
- [Subscribe to user streams with auto gap-recovery](#subscribe-to-user-streams-with-auto-gap-recovery)
- [Custom retry policy](#custom-retry-policy)
- [Route or disable the builder fee](#route-or-disable-the-builder-fee)
- [Use one strategy on multiple DEXes](#use-one-strategy-on-multiple-dexes)
- [Run in a hosted service](#run-in-a-hosted-service)

---

## Place a limit order (post-only)

`TimeInForce.Alo` ("Add Liquidity Only") rejects the order if it would cross the spread — useful for makers.

```csharp
var r = await ex.Orders.PlaceLimitAsync(
    symbol: "BTC", side: OrderSide.Buy,
    price:  60_000m, size: 0.01m,
    tif:    TimeInForce.Alo);

if (!r.Success)
    Console.WriteLine($"rejected: {r.ErrorMessage}");
```

## Place a market order

```csharp
// The library posts an IOC limit with 5% slippage from the live mid.
var r = await ex.Orders.PlaceMarketAsync("BTC", OrderSide.Sell, size: 0.01m);
```

## Stop-loss + take-profit

```csharp
// Stop-loss as market (default reduce-only): triggers when price hits 58k.
await ex.Orders.PlaceStopAsync(
    symbol: "BTC", side: OrderSide.Sell,
    triggerPrice: 58_000m, size: 0.01m,
    isMarket: true, reduceOnly: true);

// Take-profit (also reduce-only) by using a limit trigger order:
await ex.Orders.PlaceAsync(new OrderRequest(
    Symbol: "BTC", Side: OrderSide.Sell, OrderType: OrderType.TakeProfit,
    Size:  0.01m, Price: 65_000m, TriggerPrice: 64_500m,
    ReduceOnly: true));
```

## Batch place / cancel

```csharp
var batch = new[]
{
    new OrderRequest("BTC", OrderSide.Buy,  OrderType.Limit, Size: 0.01m, Price: 59_000m, TimeInForce: TimeInForce.Alo),
    new OrderRequest("BTC", OrderSide.Buy,  OrderType.Limit, Size: 0.01m, Price: 58_500m, TimeInForce: TimeInForce.Alo),
    new OrderRequest("ETH", OrderSide.Sell, OrderType.Limit, Size: 0.1m,  Price: 3_500m,  TimeInForce: TimeInForce.Alo),
};
var placed = await ex.Orders.PlaceBatchAsync(batch);

// Cancel a subset in one call.
var cancels = placed.Results
    .Where(r => r.Success)
    .Select(r => new CancelRequest("BTC", OrderId: r.OrderId))
    .ToList();
await ex.Orders.CancelBatchAsync(cancels);
```

## Modify an open order

You only have to supply the fields you want to change; the library reads the existing order and fills the rest.

```csharp
await ex.Orders.ModifyAsync(new ModifyRequest(
    Symbol:    "BTC",
    OrderId:   placedOrderId,
    NewPrice:  59_500m));
```

## Cancel everything on shutdown

```csharp
AppDomain.CurrentDomain.ProcessExit += async (_, _) =>
{
    try { await ex.Orders.CancelAllAsync(); }
    catch { /* best-effort during process exit */ }
};
```

## Read account state

```csharp
var state = await ex.Account.GetStateAsync();
Console.WriteLine($"Equity: {state.AccountValue}  Free collateral: {state.FreeCollateral}");
foreach (var p in state.Positions)
    Console.WriteLine($"  {p.Symbol} size={p.Size} pnl={p.UnrealizedPnl}");
```

## Set leverage and close a position

```csharp
await ex.Positions.SetLeverageAsync("BTC", leverage: 10, MarginMode.Cross);
await ex.Positions.CloseAsync("BTC");   // reduce-only IOC market with slippage
```

## Transfers and approvals

```csharp
// Cross-USDC transfers between spot and perp.
await ex.Transfers.SpotToPerpAsync(100m);
await ex.Transfers.PerpToSpotAsync(50m);

// External withdraw (L1 bridge).
await ex.Transfers.WithdrawAsync("0xExternalAddress", amount: 100m);

// One-time agent approval (run with master key once; then trade with agent key).
await ex.Account.ApproveAgentAsync("0xAgentAddress", agentName: "my-bot");
```

## Subscribe to public streams

```csharp
using var cts = new CancellationTokenSource();

await foreach (var t in ex.Streams.TradesAsync("BTC", cts.Token))
    Console.WriteLine($"trade {t.Trade.Price} {t.Trade.Size}");

// Multiple subscribers to the same channel share one HL subscription on the wire.
```

## Subscribe to user streams with auto gap-recovery

```csharp
// After every reconnect, the library queries REST for fills since the last seen
// timestamp and emits anything the live feed missed — deduped against the live
// stream. You don't need to handle reconnects in user code.
await foreach (var f in ex.Streams.MyFillsAsync(cts.Token))
    await db.SaveFillAsync(f.Fill);
```

`MyOrdersAsync` and `MyFundingsAsync` get the same treatment.

## Custom retry policy

```csharp
services.AddHyperLiquid(o =>
{
    o.Network = HyperLiquidNetwork.Mainnet;
    o.RetryPolicy = new HyperLiquidRetryOptions
    {
        MaxAttempts        = 5,
        InitialDelay       = TimeSpan.FromMilliseconds(150),
        MaxDelay           = TimeSpan.FromSeconds(10),
        BackoffMultiplier  = 2.0,
        RetryOnServerError = true,
        RetryOnRateLimit   = true,
    };
});
```

To disable retries entirely set `MaxAttempts = 1`.

## Route or disable the builder fee

The default is `0.005%` routed to `EasyTrading.pw`. Override per-client:

```csharp
services.AddHyperLiquid(o =>
{
    o.BuilderFee = new BuilderFee(
        BuilderAddress: "0xYourBuilderAddress",
        FeeRate:        0.0001m);    // 1 bp
});

// Or opt out completely:
o.BuilderFee = new BuilderFee("0x0", FeeRate: 0m);
```

## Use one strategy on multiple DEXes

Inject `IExchangeClient` (the cross-DEX interface) instead of `IHyperLiquidExchange`. Your code never names the venue:

```csharp
public sealed class Strategy(IExchangeClient ex)
{
    public async Task PlaceAsync(string symbol, decimal price)
    {
        await ex.Orders.PlaceLimitAsync(symbol, OrderSide.Buy,
            price: price, size: 0.01m, tif: TimeInForce.Alo);
    }
}
```

Switching venues is one line of DI registration. Aster and dYdX v4 ship the same
`IExchangeClient` contract today.

## Run in a hosted service

```csharp
public sealed class TradingService(IHyperLiquidExchange ex, ILogger<TradingService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var fill in ex.Streams.MyFillsAsync(ct))
        {
            log.LogInformation("filled {sym} {sz}@{px}",
                fill.Fill.Symbol, fill.Fill.Size, fill.Fill.Price);
        }
    }
}

// Program.cs
builder.Services
    .AddEasyTrading()
    .AddHyperLiquid(o => { /* ... */ });
builder.Services.AddHostedService<TradingService>();
```

The hosted service stops when the host shuts down — passing the framework-supplied `ct` into `MyFillsAsync` cleanly tears down the stream.
