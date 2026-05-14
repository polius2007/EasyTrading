# EasyTrading

A .NET client for decentralised perpetual and spot exchanges. One `IExchangeClient` interface across every supported DEX; per-DEX clients add only what's venue-specific (e.g. vaults and staking on HyperLiquid).

[![NuGet](https://img.shields.io/nuget/v/EasyTrading.HyperLiquid?label=EasyTrading.HyperLiquid&logo=nuget)](https://www.nuget.org/packages/EasyTrading.HyperLiquid)
[![Build](https://github.com/polius2007/EasyTrading/actions/workflows/ci.yml/badge.svg)](https://github.com/polius2007/EasyTrading/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

- **Targets:** `net8.0`, `net9.0`
- **API reference (auto-generated):** [polius2007.github.io/EasyTrading](https://polius2007.github.io/EasyTrading/)
- **API reference:** auto-generated from XML doc-comments, deployed at the site above
- **Source:** [github.com/polius2007/EasyTrading](https://github.com/polius2007/EasyTrading)

## Status

| Exchange    | Package                              | REST       | WebSocket | Signing | Latest               |
|-------------|--------------------------------------|:----------:|:---------:|:-------:|:--------------------:|
| HyperLiquid | `EasyTrading.HyperLiquid`            |  ✅        |    ✅     |   ✅    | `1.0.0`              |
| Aster       | `EasyTrading.Aster`                  | reads only |   wip     |   wip   | `1.1.0-alpha.1` (wip) |
| dYdX v4     | `EasyTrading.Dydx` *(planned)*       |   —        |    —      |    —    |     —                |

HyperLiquid coverage at `1.0.0`:

- All Info (read) and Exchange (write) endpoints, including TWAP, scheduled cancel, sub-accounts, vaults, and staking.
- EIP-712 L1 and user-signed actions; byte-identical msgpack encoding with the Python reference SDK.
- WebSocket: 9 channels, per-subscriber back-pressure, automatic reconnect with exponential backoff.
- Pre-flight order validation (tick / lot / min-notional) — invalid orders are rejected client-side.
- REST retry policy with backoff + jitter, honours `Retry-After` on 429.
- WebSocket gap recovery: on reconnect, user streams (`MyFills` / `MyOrders` / `MyFundings`) auto-fetch missed events via REST and deduplicate against the live feed.
- 89 unit tests + 5 read-only integration tests against live mainnet, all green.

## Install

```bash
dotnet add package EasyTrading.HyperLiquid
```

This pulls `EasyTrading.Abstractions` and `EasyTrading.Core` transitively.

## Quick start

```csharp
using EasyTrading.Abstractions;
using EasyTrading.HyperLiquid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = Host.CreateApplicationBuilder(args);

host.Services
    .AddEasyTrading()
    .AddHyperLiquid(options =>
    {
        options.Network = HyperLiquidNetwork.Mainnet;
        options.Credentials = new HyperLiquidCredentials(
            masterAddress: "0xYourMasterAddress",
            privateKey:    Environment.GetEnvironmentVariable("HL_PRIVATE_KEY")!,
            agentName:     "easy-bot");
    });

using var app = host.Build();
var ex = app.Services.GetRequiredService<IHyperLiquidExchange>();

// Read (no signing needed)
var mids = await ex.Markets.GetAllMidsAsync();
var book = await ex.Markets.GetOrderBookAsync("BTC", depth: 20);

// Write (signed)
var placed = await ex.Orders.PlaceLimitAsync(
    symbol: "BTC", side: OrderSide.Buy,
    price:  60_000m, size: 0.01m,
    tif:    TimeInForce.Alo);

// Stream
await foreach (var trade in ex.Streams.TradesAsync("BTC", default))
    Console.WriteLine($"{trade.Trade.Price} {trade.Trade.Size}");
```

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

- [x] HyperLiquid — REST + WebSocket + EIP-712 signing + hardening → `1.0.0`
- [ ] Aster — `EasyTrading.Aster` *(in progress: scaffold + Markets reads landed in `1.1.0-alpha.1`; signed reads, writes via EIP-712, and WebSocket pending)*
- [ ] dYdX v4 — `EasyTrading.Dydx` (STARK signatures)

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
