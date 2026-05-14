# AGENTS.md — Guidance for AI coding assistants

This file tells AI tools (Claude Code, Cursor, GitHub Copilot, Aider, Continue, etc.) how to be helpful when working with the **EasyTrading** library. If you're an AI assistant reading this, follow these conventions when generating code that uses EasyTrading or contributes to its repository.

## What EasyTrading is

EasyTrading is a multi-DEX trading client for .NET. The same `IExchangeClient` interface drives every supported DEX (HyperLiquid first; Aster and dYdX v4 planned), so strategies can switch venues by changing the DI registration only.

- **Home**: [easytrading.pw](https://easytrading.pw)
- **Source**: [github.com/polius2007/EasyTrading](https://github.com/polius2007/EasyTrading)
- **NuGet**: `EasyTrading.Abstractions`, `EasyTrading.Core`, `EasyTrading.HyperLiquid`, `EasyTrading.Broker` (more planned)
- **License**: MIT

## Core conventions (apply always)

1. **Group by entity, not by intent.** Place orders via `client.Orders.PlaceAsync`; get positions via `client.Positions.GetAllAsync`. Everything about orders lives in `Orders`, everything about positions in `Positions`, and so on.
2. **`decimal` for money.** Never use `double` or `float` for prices, sizes, fees, balances, or PnL.
3. **`CancellationToken ct = default` last.** Every async method takes a cancellation token as its last parameter, with a default value.
4. **`IAsyncEnumerable<T>` for streams.** WebSocket subscriptions are async iterators — use `await foreach`.
5. **Check `Capabilities` before optional features.** Probe `client.Capabilities.HasFlag(ExchangeCapabilities.X)` before calling TWAP, vaults, builder fees, scheduled cancel, etc.
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

- `IHyperLiquidExchange` — HL-specific surface (adds `Vaults`, `Staking`, `Builder`)
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
```

### Read account state
```csharp
var state = await ex.Account.GetStateAsync();
Console.WriteLine($"Equity: {state.AccountValue}  Free: {state.FreeCollateral}");
```

### Set leverage & close a position
```csharp
await ex.Positions.SetLeverageAsync("BTC", 10, MarginMode.Cross);
await ex.Positions.CloseAsync("BTC");
```

### Stream fills
```csharp
await foreach (var fill in ex.Streams.MyFillsAsync(ct))
    Console.WriteLine($"{fill.Symbol} {fill.Side} {fill.Size} @ {fill.Price}");
```

### Subscribe to order book
```csharp
await foreach (var book in ex.Streams.OrderBookAsync("BTC", depth: 20, ct: ct))
{
    var bestBid = book.Bids[0].Price;
    var bestAsk = book.Asks[0].Price;
}
```

## Anti-patterns — DON'T do this

- ❌ `new HyperLiquidClient(...)` directly when DI is available. Use `AddHyperLiquid()`.
- ❌ `double` / `float` for any money value.
- ❌ Forgetting `CancellationToken` in long-running calls or `await foreach` loops.
- ❌ Catching raw `Exception`. Catch typed: `RateLimitException`, `InsufficientFundsException`, …
- ❌ Holding the **master** account's private key in production. Approve an agent wallet via `IAccount.ApproveAgentAsync` and use that key instead — agents can be revoked without rotating the master key.
- ❌ Calling HL-only features (`Vaults`, `Staking`, `Builder`) on `IExchangeClient` — cast to `IHyperLiquidExchange` or inject that directly.
- ❌ Assuming `Capabilities` are universal — check with `HasFlag` first.

## Builder fees / rebates

The library routes orders through a configurable builder address for rebate revenue (via `EasyTrading.Broker`). To opt out, omit `EasyTrading.Broker` from your installation, or override `OrderRequest.BuilderFeeOverride` explicitly per order.

## Key types

- `IExchangeClient` — top-level cross-DEX contract; exposes 7 sub-clients (Markets, Orders, Positions, Trades, Account, Transfers, Streams).
- `IHyperLiquidExchange : IExchangeClient` — adds HL-only sub-clients (Vaults, Staking, Builder).
- `Symbol` — market metadata (name, kind, tick, step, min size, max leverage).
- `OrderRequest`, `Order`, `Fill`, `Position`, `OrderBook`, `Candle`, `AccountState` — DTOs as records.
- `OrderSide`, `OrderType`, `TimeInForce`, `MarginMode`, `MarketKind`, `Interval`, `OrderStatus` — enums.
- `ExchangeCapabilities` — flags enum.

## When contributing to the repo (vs. just using the library)

- Conventions: file-scoped namespaces, implicit usings, nullable enabled, records for DTOs.
- `decimal` everywhere for money. `Async` suffix on every async. `from`/`to` for time ranges. XML doc on every public member.
- Central package management — `PackageReference` entries must omit `Version` (versions live in `Directory.Packages.props`).
- Commands: `dotnet build EasyTrading.slnx`, `dotnet test EasyTrading.slnx`, `dotnet run --project samples/EasyTrading.Samples.Console`.
- Phase awareness: see [CHANGELOG.md](CHANGELOG.md). Phase 1 ships scaffolding only; real exchange calls land in Phase 2+.

## Where to find more

- [API reference](https://easytrading.pw) — auto-generated from XML docs
- [README](README.md) — project overview and quick start
- [CHANGELOG](CHANGELOG.md) — phase history and roadmap
- [HyperLiquid API docs](https://hyperliquid.gitbook.io/hyperliquid-docs) — upstream reference for HL-specific behavior
