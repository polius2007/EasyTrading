# AGENTS.md — Guidance for AI coding assistants

This file tells AI tools (Claude Code, Cursor, GitHub Copilot, Aider, Continue, etc.) how to be helpful when working with the **EasyTrading** library. If you're an AI assistant reading this, follow these conventions when generating code that uses EasyTrading or contributes to its repository.

## What EasyTrading is

EasyTrading is a multi-DEX trading client for .NET. The same `IExchangeClient` interface drives every supported DEX (HyperLiquid is at `1.0.0`; Aster and dYdX v4 planned), so strategies can switch venues by changing the DI registration only.

- **Home**: [easytrading.pw](https://easytrading.pw)
- **Source**: [github.com/polius2007/EasyTrading](https://github.com/polius2007/EasyTrading)
- **NuGet**: `EasyTrading.Abstractions`, `EasyTrading.Core`, `EasyTrading.HyperLiquid` (more planned)
- **License**: MIT

## Current status (HyperLiquid)

- Read / write / stream all functional against live HyperLiquid mainnet (verified by integration tests).
- Signing: EIP-712 L1 (phantom-agent) + user-signed flavours, both implemented.
- WebSocket: 9 channels with reconnect + per-subscriber back-pressure.
- **Pre-flight order validation** rejects orders that would fail HL's tick / lot / min-notional rules before they go on the wire. Suggest typed `Orders.Place*` calls; don't pre-validate manually in user code.
- **REST resilience**: network errors, timeouts, 5xx, and 429 (with `Retry-After`) are retried with exponential backoff + jitter. Configure via `HyperLiquidClientOptions.RetryPolicy` (default: 3 attempts).
- **WS gap recovery**: user-scoped streams (`MyFills`, `MyOrders`, `MyFundings`) fetch REST catch-up on every reconnect and dedupe against the live feed. Consumers see one unified `IAsyncEnumerable<T>` and don't need to handle reconnects in user code.
- Builder-fee routing: automatic. Library transparently calls `approveBuilderFee` on first order per trader; subsequent orders carry the `builder` field directly. No setup required for the consumer.

## Core conventions (apply always)

1. **Group by entity, not by intent.** Place orders via `client.Orders.PlaceAsync`; get positions via `client.Positions.GetAllAsync`. Everything about orders lives in `Orders`, everything about positions in `Positions`, and so on.
2. **`decimal` for money.** Never use `double` or `float` for prices, sizes, fees, balances, or PnL.
3. **`CancellationToken ct = default` last.** Every async method takes a cancellation token as its last parameter, with a default value.
4. **`IAsyncEnumerable<T>` for streams.** WebSocket subscriptions are async iterators — use `await foreach`.
5. **Check `Capabilities` before optional features.** Probe `client.Capabilities.HasFlag(ExchangeCapabilities.X)` before calling TWAP, vaults, scheduled cancel, etc.
6. **Typed exceptions.** Catch one of: `RateLimitException`, `InsufficientFundsException`, `InvalidOrderException`, `AuthenticationException`, `SigningException`, or the base `ExchangeApiException`. Don't catch raw `Exception`.

## Recommended registration (DI)

```csharp
using EasyTrading.Abstractions;
using EasyTrading.HyperLiquid;
using Microsoft.Extensions.DependencyInjection;

services.AddEasyTrading()
        .AddHyperLiquid(opts =>
        {
            opts.Network     = HyperLiquidNetwork.Mainnet;
            opts.Credentials = new HyperLiquidCredentials(
                masterAddress: "0xYourMasterAddress",
                privateKey:    Environment.GetEnvironmentVariable("HL_PRIVATE_KEY")!,
                agentName:     "my-bot");
        });
```

Then inject:

- `IHyperLiquidExchange` — HL-specific surface (adds `Vaults`, `Staking`)
- `IExchangeClient` — cross-DEX surface (works for HL today, Aster/dYdX later)

## Common patterns

### Place a limit order (post-only)
```csharp
var result = await ex.Orders.PlaceLimitAsync(
    symbol: "BTC", side: OrderSide.Buy,
    price:  60_000m, size: 0.01m,
    tif:    TimeInForce.Alo);
```

### Place a market order
```csharp
// The library internally posts an IOC limit with 5% slippage from the live mid.
var result = await ex.Orders.PlaceMarketAsync("BTC", OrderSide.Sell, size: 0.01m);
```

### Place a stop-loss
```csharp
var result = await ex.Orders.PlaceStopAsync(
    symbol: "BTC", side: OrderSide.Sell,
    triggerPrice: 58_000m, size: 0.01m,
    isMarket: true, reduceOnly: true);
```

### Cancel by exchange or client id
```csharp
await ex.Orders.CancelAsync("BTC", orderId: 123456789);
await ex.Orders.CancelByClientIdAsync("BTC", clientOrderId: "my-cloid-001");
await ex.Orders.CancelAllAsync();                // every open order, every market
await ex.Orders.CancelAllAsync(symbol: "BTC");   // only BTC
```

### Read account state
```csharp
var state = await ex.Account.GetStateAsync();
Console.WriteLine($"Equity: {state.AccountValue}  Free: {state.FreeCollateral}");
foreach (var p in state.Positions)
    Console.WriteLine($"{p.Symbol} size={p.Size} pnl={p.UnrealizedPnl}");
```

### Set leverage & close a position
```csharp
await ex.Positions.SetLeverageAsync("BTC", 10, MarginMode.Cross);
await ex.Positions.CloseAsync("BTC"); // reduce-only IOC market with slippage
```

### Transfers, agent / vault / staking
```csharp
await ex.Transfers.SpotToPerpAsync(100m);                  // user-signed
await ex.Transfers.WithdrawAsync("0xExternalAddress", 50m); // user-signed bridge withdraw
await ex.Account.ApproveAgentAsync("0xAgentAddr", "easy-bot");
await ex.Vaults.DepositAsync("0xVaultAddr", 500m);
await ex.Staking.DelegateAsync("0xValidator", 100m);
```

### WebSocket streams

```csharp
// Public — no creds required
await foreach (var trade in ex.Streams.TradesAsync("BTC", ct))
    Console.WriteLine($"trade {trade.Trade.Price} sz={trade.Trade.Size}");

await foreach (var update in ex.Streams.OrderBookAsync("BTC", depth: 20, ct: ct))
    Console.WriteLine($"bid {update.Bids[0].Price} / ask {update.Asks[0].Price}");

await foreach (var mid in ex.Streams.AllMidsAsync(ct))
    Console.WriteLine($"{mid.Symbol} = {mid.Mid}");

// User-scoped — creds required (uses options.Credentials.MasterAddress)
await foreach (var order in ex.Streams.MyOrdersAsync(ct))
    Console.WriteLine($"order {order.Order.OrderId} → {order.Order.Status}");

await foreach (var fill in ex.Streams.MyFillsAsync(ct))
    Console.WriteLine($"fill {fill.Fill.Symbol} {fill.Fill.Size}@{fill.Fill.Price}");
```

The shared WebSocket lazy-connects on first subscription. Multiple subscribers for the same channel-symbol share one HL subscription and each get every message — no duplication on the wire. The connection reconnects with exponential-ish backoff (cap 30 s) and silently re-subscribes to every active key.

## Anti-patterns — DON'T do this

- ❌ `new HyperLiquidClient(...)` directly when DI is available. Use `AddHyperLiquid()`.
- ❌ `double` / `float` for any money value.
- ❌ Forgetting `CancellationToken` in long-running calls or `await foreach` loops.
- ❌ Catching raw `Exception`. Catch typed: `RateLimitException`, `InsufficientFundsException`, …
- ❌ Holding the **master** account's private key in production. Approve an agent wallet via `IAccount.ApproveAgentAsync` and use that key instead — agents can be revoked without rotating the master key.
- ❌ Calling HL-only features (`Vaults`, `Staking`) on `IExchangeClient` — cast to `IHyperLiquidExchange` or inject that directly.
- ❌ Assuming `Capabilities` are universal — check with `HasFlag` first.
- ❌ Looping over `await foreach` without a `CancellationToken` — the WebSocket reader will not stop on its own.

## Key types

- `IExchangeClient` — top-level cross-DEX contract; exposes 7 sub-clients (Markets, Orders, Positions, Trades, Account, Transfers, Streams).
- `IHyperLiquidExchange : IExchangeClient` — adds HL-only sub-clients (Vaults, Staking).
- `Symbol` — market metadata (name, kind, tick, step, min size, max leverage).
- `OrderRequest`, `Order`, `Fill`, `Position`, `OrderBook`, `Candle`, `AccountState` — DTOs as records.
- `TradeUpdate`, `OrderBookUpdate`, `CandleUpdate`, `MidUpdate`, `BboUpdate`, `OrderUpdate`, `FillUpdate`, `FundingUpdate`, `NotificationUpdate` — stream payload records.
- `OrderSide`, `OrderType`, `TimeInForce`, `MarginMode`, `MarketKind`, `Interval`, `OrderStatus` — enums.
- `ExchangeCapabilities` — flags enum.

## When contributing to the repo (vs. just using the library)

- Conventions: file-scoped namespaces, implicit usings, nullable enabled, records for DTOs.
- `decimal` everywhere for money. `Async` suffix on every async. `from`/`to` for time ranges. XML doc on every public member.
- Central package management — `PackageReference` entries must omit `Version` (versions live in `Directory.Packages.props`).
- Commands: `dotnet build EasyTrading.slnx`, `dotnet test EasyTrading.slnx`, `dotnet run --project samples/EasyTrading.Samples.Console`.
- Integration tests against live mainnet: set `EASYTRADING_INTEGRATION=1` then `dotnet test --filter "Category=Integration"`.

## Where to find more

- [Getting started](docs/getting-started.md) — step-by-step first trade
- [Recipes](docs/recipes.md) — common patterns (limit, market, stop, batch, streams, retries, builder fee, hosted service)
- [API reference](https://easytrading.pw) — auto-generated from XML docs
- [README](README.md) — project overview and quick start
- [CHANGELOG](CHANGELOG.md) — phase history and roadmap
- [CONTRIBUTING](CONTRIBUTING.md) — how to add a DEX or contribute changes
- [HyperLiquid API docs](https://hyperliquid.gitbook.io/hyperliquid-docs) — upstream reference for HL-specific behavior
