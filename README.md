# EasyTrading

A .NET client for decentralised perpetual and spot exchanges. One `IExchangeClient` interface across every supported DEX; per-DEX clients add only what's venue-specific (e.g. vaults and staking on HyperLiquid).

[![NuGet](https://img.shields.io/nuget/v/EasyTrading.HyperLiquid?label=EasyTrading.HyperLiquid&logo=nuget)](https://www.nuget.org/packages/EasyTrading.HyperLiquid)
[![Build](https://github.com/polius2007/EasyTrading/actions/workflows/ci.yml/badge.svg)](https://github.com/polius2007/EasyTrading/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

- **Targets:** `net8.0`, `net9.0`
- **API reference:** [polius2007.github.io/EasyTrading](https://polius2007.github.io/EasyTrading/) (auto-generated DocFX site)
- **Source:** [github.com/polius2007/EasyTrading](https://github.com/polius2007/EasyTrading)
- **License:** MIT — see [LICENSE](LICENSE)

## Status

| Exchange    | Package                              | REST       | WebSocket | Signing | Latest                 |
|-------------|--------------------------------------|:----------:|:---------:|:-------:|:----------------------:|
| HyperLiquid | `EasyTrading.HyperLiquid`            |     ✅     |    ✅     |   ✅    | `1.1.1`                |
| Aster       | `EasyTrading.Aster`                  |     ✅     |    ✅     |   ✅    | `1.1.1`                |
| dYdX v4     | `EasyTrading.Dydx`                   | reads only |   public  |   wip   | scaffold *(in tree)*   |

Coverage summary:

- **HyperLiquid** is stable. All Info (read) and Exchange (write) endpoints, including TWAP,
  scheduled cancel, sub-accounts, vaults, and staking. EIP-712 L1 and user-signed actions;
  byte-identical msgpack encoding with the Python reference SDK. WebSocket: 9 channels,
  per-subscriber back-pressure, automatic reconnect with exponential backoff. Pre-flight
  order validation (tick / lot / min-notional). REST retry policy with backoff + jitter,
  honours `Retry-After` on 429. WebSocket gap recovery: on reconnect, user streams auto-fetch
  missed events via REST and deduplicate against the live feed.
- **Aster Finance** is stable. Same surface, EIP-712 signing with `AsterSignTransaction`
  domain. Pre-flight validator wired to `/fapi/v3/exchangeInfo` filters (PRICE_FILTER /
  LOT_SIZE / MIN_NOTIONAL). WebSocket: Binance-style multiplex (market + listenKey-bound
  user streams with 30-min keepalive).
- **dYdX v4** scaffold — Indexer REST reads + public WebSocket streams work end-to-end
  against live mainnet. Cosmos SDK transaction signing for writes is pending Phase 7.2.

**Tests:** 125 unit + 14 integration (live mainnet across HL, Aster, dYdX), all green.

## Install

```bash
dotnet add package EasyTrading.HyperLiquid
# or
dotnet add package EasyTrading.Aster
```

Either pulls `EasyTrading.Abstractions` and `EasyTrading.Core` transitively.

## Quick start

```csharp
using EasyTrading.Abstractions;
using EasyTrading.Aster;
using EasyTrading.HyperLiquid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = Host.CreateApplicationBuilder(args);

host.Services
    .AddEasyTrading()
    .AddHyperLiquid(o =>
    {
        o.Network = HyperLiquidNetwork.Mainnet;
        o.Credentials = new HyperLiquidCredentials(
            masterAddress: "0xYourMasterAddress",
            privateKey:    Environment.GetEnvironmentVariable("HL_AGENT_KEY")!,
            agentName:     "easy-bot");
    })
    .AddAster(o =>
    {
        o.Network = AsterNetwork.Mainnet;
        o.Credentials = new AsterCredentials(
            MasterAddress: "0xYourMasterAddress",
            SignerAddress: "0xYourSignerAddress",
            PrivateKey:    Environment.GetEnvironmentVariable("ASTER_SIGNER_KEY")!);
    });

using var app = host.Build();
var hl    = app.Services.GetRequiredService<IHyperLiquidExchange>();
var aster = app.Services.GetRequiredService<IAsterExchange>();

// Read — works without credentials.
var mids = await hl.Markets.GetAllMidsAsync();
var book = await aster.Markets.GetOrderBookAsync("BTCUSDT", depth: 20);

// Write — signed.
var placed = await hl.Orders.PlaceLimitAsync(
    symbol: "BTC", side: OrderSide.Buy,
    price:  60_000m, size: 0.01m,
    tif:    TimeInForce.Alo);

// Stream.
await foreach (var trade in hl.Streams.TradesAsync("BTC", default))
    Console.WriteLine($"{trade.Trade.Price} {trade.Trade.Size}");
```

To write a strategy that doesn't care which venue it runs against, inject `IExchangeClient`
instead of the venue-specific surface — every supported DEX implements the same shape.

A more complete walk-through with credential setup, agent wallets, and testnet-first guidance lives in [`docs/getting-started.md`](docs/getting-started.md). Common patterns are collected in [`docs/recipes.md`](docs/recipes.md).

## API surface

Methods are grouped by entity. Everything about orders is on `Orders`; everything about positions is on `Positions`; and so on.

| Group              | What it covers                                                |
|--------------------|---------------------------------------------------------------|
| `Markets`          | Symbols, order book, candles, mids, funding                   |
| `Orders`           | Place / modify / cancel / batch / TWAP / open / history       |
| `Positions`        | Read positions, set leverage, add/reduce margin, close        |
| `Trades`           | Your fills (by symbol, by order, by time)                     |
| `Account`          | Balances, fees, portfolio, sub-accounts, agents, rate limit   |
| `Transfers`        | Withdraw, internal transfers, spot ↔ perp, sub-account moves  |
| `Streams`          | WebSocket subscriptions (public + user) via `IAsyncEnumerable`|
| `Vaults` (HL only) | Vault details, deposit, withdraw                              |
| `Staking` (HL only)| Delegate / undelegate / rewards                               |

## Design notes

- `decimal` everywhere for money — never `double` / `float`.
- DTOs are immutable `record` types.
- Async methods end with `Async`; `CancellationToken ct = default` is the last parameter on every method.
- WebSocket subscriptions are `IAsyncEnumerable<T>` — iterate with `await foreach` and cancel by cancelling the token.
- Optional venue features are gated by `client.Capabilities.HasFlag(ExchangeCapabilities.X)`.
- Typed exceptions only: `RateLimitException`, `InsufficientFundsException`, `InvalidOrderException`, `AuthenticationException`, `SigningException`, `ExchangeApiException`.

## Roadmap

- [x] HyperLiquid — REST + WebSocket + EIP-712 signing + hardening → `1.1.1`
- [x] Aster — REST + WebSocket + EIP-712 signing → `1.1.1`
- [ ] dYdX v4 — `EasyTrading.Dydx` *(in progress: Indexer reads + public WebSocket landed; Cosmos SDK transaction signing for writes pending Phase 7.2)*

## Documentation

| | |
|---|---|
| Getting started (tutorial)        | [`docs/getting-started.md`](docs/getting-started.md) |
| Recipes (common patterns)         | [`docs/recipes.md`](docs/recipes.md)                 |
| API reference (auto-generated)    | [polius2007.github.io/EasyTrading](https://polius2007.github.io/EasyTrading/) |
| Changelog                         | [`CHANGELOG.md`](CHANGELOG.md)                       |
| Contributing                      | [`CONTRIBUTING.md`](CONTRIBUTING.md)                 |
| Security policy                   | [`SECURITY.md`](SECURITY.md)                         |

### For AI coding assistants

The repo ships first-class instructions so IDE assistants generate correct code without prompting:

| Tool                | File                                                                 |
|---------------------|----------------------------------------------------------------------|
| Universal (any AI)  | [`AGENTS.md`](AGENTS.md)                                             |
| Claude Code         | [`CLAUDE.md`](CLAUDE.md)                                             |
| GitHub Copilot      | [`.github/copilot-instructions.md`](.github/copilot-instructions.md) |
| Cursor IDE          | [`.cursor/rules/easytrading.mdc`](.cursor/rules/easytrading.mdc)     |
| LLM crawlers / RAG  | [`llms.txt`](llms.txt)                                               |

These files travel with the source; any fork or clone inherits the same guidance.

## Disclaimer

This software is provided "as is", without warranty of any kind. Trading derivatives carries significant financial risk; use at your own responsibility. The authors are not affiliated with HyperLiquid, Aster, dYdX, or any other exchange.

The HyperLiquid client attaches a 0.5 bps (0.005%) builder fee to every order by default — this funds continued development. The fee is well below typical taker rates and visible on-chain as a separate field on each order action. The library calls `approveBuilderFee` once per account on the first order; nothing else is required from the consumer. To route fees to your own address set `HyperLiquidClientOptions.BuilderFee`; to opt out entirely, set its `FeeRate` to `0m`.

## License

[MIT](LICENSE) © 2026 Elinesoft
