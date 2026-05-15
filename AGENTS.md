# AGENTS.md — Guidance for AI coding assistants

This file tells AI tools (Claude Code, Cursor, GitHub Copilot, Aider, Continue, etc.) how to be helpful when working with the **EasyTrading** library. If you're an AI assistant reading this, follow these conventions when generating code that uses EasyTrading or contributes to its repository.

## What EasyTrading is

EasyTrading is a multi-DEX trading client for .NET. The same `IExchangeClient` interface drives every supported DEX, so strategies can switch venues by changing the DI registration only.

- **API reference**: [polius2007.github.io/EasyTrading](https://polius2007.github.io/EasyTrading/) (auto-generated DocFX site)
- **Source**: [github.com/polius2007/EasyTrading](https://github.com/polius2007/EasyTrading)
- **NuGet**: `EasyTrading.Abstractions`, `EasyTrading.Core`, `EasyTrading.HyperLiquid`, `EasyTrading.Aster` (`EasyTrading.Dydx` in-tree, not yet published)
- **License**: MIT

## Current status per venue

**HyperLiquid `1.2.0`** — stable. Read / write / stream all functional against live mainnet.
EIP-712 L1 (phantom-agent) + user-signed flavours. WebSocket: 9 channels with reconnect +
per-subscriber back-pressure. Pre-flight order validation (tick / lot / min-notional).
REST retry policy with backoff + jitter and `Retry-After`. WebSocket gap recovery on user
streams (`MyFills` / `MyOrders` / `MyFundings`) — REST catch-up on each reconnect with
dedup. Builder-fee routing is automatic; the library calls `approveBuilderFee` once per
trader transparently.

**Aster Finance `1.2.0`** — stable. Same surface, EIP-712 signing under
`AsterSignTransaction` v1 / chainId 1666. Pre-flight validator wired to
`/fapi/v3/exchangeInfo` filters (PRICE_FILTER / LOT_SIZE / MIN_NOTIONAL). WebSocket:
Binance-style multiplex for market data + a separate listenKey-bound socket for user
data with 30-min keepalive.

**dYdX v4 `1.2.0`** — stable. Indexer REST + public WebSocket + signed Indexer reads +
full Cosmos SDK transaction signing (BIP-39 → BIP-32 → secp256k1 → bech32 → protobuf
`TxRaw` → REST broadcast). End-to-end verified on testnet: `Testnet_PlaceLimit_and_Cancel`
posts a far-from-market post-only BTC-USD buy from a freshly-faucet-funded wallet and
cancels by client id; the validator accepts both. Defaults to the polkachu community
endpoints (`dydx-dao-api.polkachu.com` for mainnet, `dydx-testnet-api.polkachu.com` for
testnet) with `dydx-mainnet-1` / `dydx-testnet-4` chain ids.

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
using EasyTrading.Aster;
using EasyTrading.Dydx;
using Microsoft.Extensions.DependencyInjection;

services.AddEasyTrading()
        // HyperLiquid — agent-wallet model (one master + one signer per bot).
        .AddHyperLiquid(o =>
        {
            o.Network     = HyperLiquidNetwork.Mainnet;
            o.Credentials = new HyperLiquidCredentials(
                masterAddress: "0xYourMasterAddress",
                privateKey:    Environment.GetEnvironmentVariable("HL_AGENT_KEY")!,
                agentName:     "my-bot");
        })
        // Aster — same master/signer split (separate API wallet registered via Aster's UI).
        .AddAster(o =>
        {
            o.Network     = AsterNetwork.Mainnet;
            o.Credentials = new AsterCredentials(
                MasterAddress: "0xYourMasterAddress",
                SignerAddress: "0xYourSignerAddress",
                PrivateKey:    Environment.GetEnvironmentVariable("ASTER_SIGNER_KEY")!);
        })
        // dYdX v4 — Cosmos chain; trade directly with a hot-wallet mnemonic. Reads-only today.
        .AddDydx(o =>
        {
            o.Network     = DydxNetwork.Mainnet;
            o.Credentials = new DydxCredentials(
                Address:  "dydx1…",
                Mnemonic: Environment.GetEnvironmentVariable("DYDX_MNEMONIC")!,
                SubaccountNumber: 0);
        });
```

Then inject:

- `IHyperLiquidExchange` — HL-specific surface (adds `Vaults`, `Staking`)
- `IAsterExchange` — Aster-specific surface (currently identical to `IExchangeClient`)
- `IDydxExchange` — dYdX-specific surface (currently identical to `IExchangeClient`)
- `IExchangeClient` — cross-DEX surface for venue-agnostic strategies

When a host registers more than one venue, the cross-DEX clients are exposed via keyed
DI: `sp.GetRequiredKeyedService<IExchangeClient>("hyperliquid")` / `"aster"` / `"dydx"`.

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
- [API reference](https://polius2007.github.io/EasyTrading/) — auto-generated from XML docs (DocFX)
- [README](README.md) — project overview and quick start
- [CHANGELOG](CHANGELOG.md) — phase history and roadmap
- [CONTRIBUTING](CONTRIBUTING.md) — how to add a DEX or contribute changes
- [HyperLiquid API docs](https://hyperliquid.gitbook.io/hyperliquid-docs) — upstream reference for HL-specific behavior
