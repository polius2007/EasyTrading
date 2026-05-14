# EasyTrading

> Modern, multi-DEX trading client for .NET — HyperLiquid first, then Aster and dYdX.

[![NuGet](https://img.shields.io/nuget/v/EasyTrading.HyperLiquid?label=EasyTrading.HyperLiquid&logo=nuget)](https://www.nuget.org/packages/EasyTrading.HyperLiquid)
[![Build](https://github.com/polius2007/EasyTrading/actions/workflows/ci.yml/badge.svg)](https://github.com/polius2007/EasyTrading/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

EasyTrading is a unified .NET client for decentralised perpetual and spot exchanges. The same `IExchangeClient` interface drives every supported DEX, so you write your strategy once and switch venues by changing the registration.

🌐 **Home:** [easytrading.pw](https://easytrading.pw)

> **Status — pre-alpha.** Phase 1 (scaffolding + full public API surface) is in place. The HyperLiquid client compiles and exposes its full interface; real exchange calls land in the following phases — see the [roadmap](#roadmap).

## Supported DEXes

| Exchange    | Package                              | REST | WebSocket | Signing |
|-------------|--------------------------------------|:----:|:---------:|:-------:|
| HyperLiquid | `EasyTrading.HyperLiquid`            |  🚧  |    🚧     |   🚧    |
| Aster       | `EasyTrading.Aster` *(planned)*      |   —  |     —     |    —    |
| dYdX v4     | `EasyTrading.Dydx` *(planned)*       |   —  |     —     |    —    |

## Install

```bash
dotnet add package EasyTrading.HyperLiquid
```

This single command also installs `EasyTrading.Abstractions` and `EasyTrading.Core` transitively. For builder-fee routing add `dotnet add package EasyTrading.Broker`.

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
var exchange = app.Services.GetRequiredService<IHyperLiquidExchange>();

// Market data — public, no signing
var mids = await exchange.Markets.GetAllMidsAsync();
Console.WriteLine($"BTC mid: {mids["BTC"]}");

// Place a post-only limit buy
var placed = await exchange.Orders.PlaceLimitAsync(
    symbol: "BTC", side: OrderSide.Buy,
    price:  60_000m, size: 0.01m,
    tif:    TimeInForce.Alo);

Console.WriteLine($"Order id: {placed.OrderId}");

// Stream your own fills
await foreach (var fill in exchange.Streams.MyFillsAsync(default))
    Console.WriteLine($"{fill.Symbol} {fill.Side} {fill.Size} @ {fill.Price}");
```

## API surface

Methods are grouped by **entity** — all order operations live under `Orders`, position operations under `Positions`, and so on. This keeps the API discoverable: when you want to do something with an order, IntelliSense shows you every option in one place.

| Group              | What it covers                                                |
|--------------------|---------------------------------------------------------------|
| `Markets`          | Symbols, order book, candles, mids, funding, public trades    |
| `Orders`           | Place / modify / cancel / batch / TWAP / open / history       |
| `Positions`        | Read positions, set leverage, add/reduce margin, close        |
| `Trades`           | Your fills (by symbol, by order, by time)                     |
| `Account`          | Balances, fees, portfolio, sub-accounts, agents, rate limit   |
| `Transfers`        | Withdraw, internal transfers, spot ↔ perp, sub-account moves  |
| `Streams`          | WebSocket subscriptions (public + user) via `IAsyncEnumerable`|
| `Vaults` (HL only) | Vault details, deposit, withdraw                              |
| `Staking` (HL only)| Delegate / undelegate / rewards                               |
| `Builder` (HL only)| Builder-fee approvals (used by `EasyTrading.Broker`)          |

## Roadmap

- [x] **Phase 1** — Solution scaffold, full public API surface, CI/CD, docs site
- [ ] **Phase 2** — HyperLiquid `Info` endpoint (all read types)
- [ ] **Phase 3** — HyperLiquid `Exchange` endpoint + EIP-712 signing
- [ ] **Phase 4** — HyperLiquid WebSocket streaming
- [ ] **Phase 5** — `EasyTrading.Broker` — builder-fee / rebate layer
- [ ] **Phase 6** — `EasyTrading.Aster` client
- [ ] **Phase 7** — `EasyTrading.Dydx` (dYdX v4) client

## Documentation

- 📖 API reference (auto-generated from XML doc-comments): [easytrading.pw](https://easytrading.pw)
- 🤖 AI/agent guide: [`AGENTS.md`](AGENTS.md) — patterns for Cursor, Claude Code, Copilot, Aider, …
- 📝 Source: [`github.com/polius2007/EasyTrading`](https://github.com/polius2007/EasyTrading)
- 📋 Changelog & phase progress: [`CHANGELOG.md`](CHANGELOG.md)

## For AI coding assistants

This repo ships first-class instructions for AI coding tools so that any IDE assistant gives correct code for EasyTrading out of the box:

| Tool                | File                                                                 |
|---------------------|----------------------------------------------------------------------|
| Universal           | [`AGENTS.md`](AGENTS.md)                                             |
| Claude Code         | [`CLAUDE.md`](CLAUDE.md)                                             |
| GitHub Copilot      | [`.github/copilot-instructions.md`](.github/copilot-instructions.md) |
| Cursor IDE          | [`.cursor/rules/easytrading.mdc`](.cursor/rules/easytrading.mdc)     |
| LLM crawlers / RAG  | [`llms.txt`](llms.txt)                                               |

These files travel with the source, so any consumer fork / clone / install gets correct AI guidance automatically.

## Repository setup checklist

Before publishing your first release:

1. **Update `<GitHubOwner>` in [`Directory.Build.props`](Directory.Build.props)** to your GitHub username/org (currently `polius2007`). All repo URLs are derived from this one value.
2. **Push to GitHub.** Either `git push` after creating an empty `EasyTrading` repo, or use Visual Studio: *Git → Push → Publish to GitHub*.
3. **Add a `NUGET_API_KEY` secret** in your repo settings — get the key from [nuget.org/account/apikeys](https://www.nuget.org/account/apikeys) with **Push** scope for the `EasyTrading.*` glob pattern.
4. **Enable GitHub Pages** (Settings → Pages → Source: *GitHub Actions*) so the DocFX site can deploy via the `docs.yml` workflow. Optionally point `easytrading.pw` at the Pages URL via a CNAME.

Release a new version with a single tag push:

```bash
git tag v0.1.0-alpha.1 && git push --tags
```

The `release.yml` workflow builds, packs, and pushes all `EasyTrading.*` packages to NuGet automatically.

## Disclaimer

This software is provided "as is", without warranty of any kind. Trading derivatives carries significant risk; use at your own responsibility. The authors are not affiliated with HyperLiquid, Aster, dYdX, or any other exchange. Builder-fee routing is opt-out; see [`docs/modules/builder.md`](docs/modules/builder.md).

## License

[MIT](LICENSE) © 2026 EasyTrading.pw
