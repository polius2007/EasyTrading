# Claude Code instructions for EasyTrading

This file is read automatically by Claude Code when working in this repository. For tool-neutral guidance see [AGENTS.md](AGENTS.md) — the conventions are the same; this file just adds Claude-specific layout notes.

## Repository layout

- `src/EasyTrading.Abstractions/` — cross-DEX interfaces and models, no runtime deps
- `src/EasyTrading.Core/` — shared infrastructure (HTTP, WebSocket, signing helpers)
- `src/EasyTrading.HyperLiquid/` — HyperLiquid client (REST + WebSocket)
- `tests/EasyTrading.HyperLiquid.UnitTests/` — unit tests (xUnit + NSubstitute)
- `samples/EasyTrading.Samples.Console/` — usage demo
- `docs/` — DocFX site (deploys to https://easytrading.pw via the `docs.yml` workflow)
- `.github/workflows/` — `ci.yml` (build + test), `release.yml` (NuGet on tag), `docs.yml` (Pages)

## Phases

The library ships in phases. See [CHANGELOG.md](CHANGELOG.md) for the live state.

| # | Scope                                            | Status |
|---|--------------------------------------------------|--------|
| 1 | Scaffolding + full public surface                | ✅     |
| 2 | HyperLiquid Info endpoint (read-only)            | ✅     |
| 3 | HyperLiquid Exchange endpoint + EIP-712 signing  | ⏳     |
| 4 | HyperLiquid WebSocket streaming                  | ⏳     |
| 5 | Aster client                                     | ⏳     |
| 6 | dYdX v4 client                                   | ⏳     |

## Coding conventions

- `decimal` for money; never `double` / `float`
- `Async` suffix on every async method
- `CancellationToken ct = default` as the last parameter
- `from` / `to` for time ranges (CA1716 suppressed globally so we use plain English names)
- File-scoped namespaces, implicit usings, nullable enabled
- XML doc-comment every public type and member
- Records for DTOs; classes only where behavior or mutation is needed
- Methods grouped by **entity** (Orders, Positions, Markets, …), not by intent (Trading, MarketData, …)
- Central package management via `Directory.Packages.props` — `PackageReference` entries omit `Version`

## Commands

```powershell
dotnet build EasyTrading.slnx
dotnet test  EasyTrading.slnx

# Integration tests against live HyperLiquid mainnet (read-only):
$env:EASYTRADING_INTEGRATION="1"
dotnet test EasyTrading.slnx --filter "Category=Integration"

# Sample console (hits live mainnet):
dotnet run --project samples/EasyTrading.Samples.Console
```

## Release flow

```powershell
git tag v0.2.1-alpha.1
git push --tags
```

The `release.yml` workflow builds, packs, and pushes every `EasyTrading.*` NuGet package automatically.

## When to ask the user before doing something

- Changing the public API surface in `EasyTrading.Abstractions` — discuss first; this is a contract for every DEX implementation
- Adding a new runtime dependency — discuss first (uses central package management; new deps go in `Directory.Packages.props`)
- Adding a new DEX — follow the existing pattern: implement `IExchangeClient`, add a `*-specific` extension interface for venue-only features, ship as its own NuGet package
- Touching `HlBuilderDefaults` or the builder-fee handling logic in `HlExchangeClient` (Phase 3) — discuss first; that's the commercial component

## Brand & ownership

- Brand: **EasyTrading.pw**
- License: MIT
- A small default builder fee is attached to every HyperLiquid order action. Defaults live in `EasyTrading.HyperLiquid.Infrastructure.HlBuilderDefaults` (internal); users override per-client via `HyperLiquidClientOptions.BuilderFee` or per-order via `OrderRequest.BuilderFeeOverride`.

For the full coding patterns and recommended snippets, see [AGENTS.md](AGENTS.md).
