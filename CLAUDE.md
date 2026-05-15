# Claude Code instructions for EasyTrading

This file is read automatically by Claude Code when working in this repository. For tool-neutral guidance see [AGENTS.md](AGENTS.md) — the conventions are the same; this file just adds Claude-specific layout notes.

## Repository layout

- `src/EasyTrading.Abstractions/` — cross-DEX interfaces and models, no runtime deps
- `src/EasyTrading.Core/` — shared infrastructure (HTTP, WebSocket, signing helpers)
- `src/EasyTrading.HyperLiquid/` — HyperLiquid client (REST + WebSocket + EIP-712 signing) — `1.2.0` on NuGet
- `src/EasyTrading.Aster/` — Aster client (REST + WebSocket + EIP-712 signing) — `1.2.0` on NuGet
- `src/EasyTrading.Dydx/` — dYdX v4 client (Indexer REST + WebSocket + signed Indexer reads + full Cosmos SDK transaction signing: BIP-39 → secp256k1 → bech32 → protobuf TxRaw → REST broadcast) — `1.2.0` on NuGet
- `tests/EasyTrading.HyperLiquid.UnitTests/` — HL unit + integration tests (xUnit + NSubstitute)
- `tests/EasyTrading.Aster.UnitTests/` — Aster unit + integration tests (xUnit)
- `tests/EasyTrading.Dydx.UnitTests/` — dYdX unit + integration tests (xUnit)
- `samples/EasyTrading.Samples.Console/` — usage demo
- `docs/` — DocFX site (deploys to https://polius2007.github.io/EasyTrading/ via the `docs.yml` workflow)
- `.github/workflows/` — `ci.yml` (build + test), `release.yml` (NuGet on tag), `docs.yml` (Pages)

## Phases

The library ships in phases. See [CHANGELOG.md](CHANGELOG.md) for the live state.

| # | Scope                                            | Status |
|---|--------------------------------------------------|--------|
| 1 | Scaffolding + full public surface                | ✅     |
| 2 | HyperLiquid Info endpoint (read-only)            | ✅     |
| 3 | HyperLiquid Exchange endpoint + EIP-712 signing  | ✅     |
| 4 | HyperLiquid WebSocket streaming                  | ✅     |
| 5 | Hardening (validation + retry + gap recovery)    | ✅     |
| 6 | Aster client — full surface (REST + WS + EIP-712) | ✅     |
| 7.0 | dYdX v4 scaffold + Indexer reads + public WS   | ✅     |
| 7.1 | dYdX signed Indexer reads (Account/Positions)  | ✅     |
| 7.2 | dYdX writes via Cosmos SDK + secp256k1 signing | ✅ (testnet verified, shipped at 1.2.0) |

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

# Integration tests against live HyperLiquid mainnet (read-only by default):
$env:EASYTRADING_INTEGRATION="1"
dotnet test EasyTrading.slnx --filter "Category=Integration"

# Sample console (hits live mainnet):
dotnet run --project samples/EasyTrading.Samples.Console
```

## Release flow

```powershell
git tag v1.2.0
git push --tags
```

The `release.yml` workflow builds, packs, and pushes every `EasyTrading.*` NuGet package automatically. Tag pattern: `v*`.

## Build / test status to maintain

When making changes, the bar is:
- `dotnet build EasyTrading.slnx` — clean (0 warnings, 0 errors, net8.0 + net9.0)
- `dotnet test EasyTrading.slnx` — all unit tests green (currently 146: 89 HL + 25 Aster + 32 dYdX)
- With `EASYTRADING_INTEGRATION=1`, integration tests also green (currently 16: 5 HL + 5 Aster + 6 dYdX, all live mainnet/testnet reads)
- `DYDX_TESTNET_MNEMONIC` env var unlocks the testnet PlaceLimit + Cancel verification path (also gated by `EASYTRADING_INTEGRATION=1`)
- `EASYTRADING_BOOTSTRAP_FAUCET=1` + `EASYTRADING_INTEGRATION=1` enables `TestnetBootstrap` which generates a fresh BIP-39 mnemonic, derives the dydx1… address, and calls the public faucet to seed it with USDC

If a build error is an analyzer warning (CA*/IDE*), the fix is usually to either suppress it in `Directory.Build.props` `<NoWarn>` with a one-line justification, or to refactor minimally. Don't suppress to hide real bugs.

## When to ask the user before doing something

- Changing the public API surface in `EasyTrading.Abstractions` — discuss first; this is a contract for every DEX implementation
- Adding a new runtime dependency — discuss first (uses central package management; new deps go in `Directory.Packages.props`)
- Adding a new DEX — follow the existing pattern: implement `IExchangeClient`, add a `*-specific` extension interface for venue-only features, ship as its own NuGet package
- Touching `HlBuilderDefaults` or the builder-fee handling logic — discuss first; that's the commercial component
- Pinning or bumping `Nethereum.Signer` — 4.27.0 had a regression in `EthECKey.SignAndCalculateV`; we're pinned to 4.26.0 and a comment in `Directory.Packages.props` documents why

## Brand & ownership

- Brand: **EasyTrading.pw**
- License: MIT
- A small default builder fee is attached to every HyperLiquid order action. Defaults live in `EasyTrading.HyperLiquid.Infrastructure.HlBuilderDefaults` (internal); users override per-client via `HyperLiquidClientOptions.BuilderFee` or per-order via `OrderRequest.BuilderFeeOverride`. The library auto-calls `approveBuilderFee` on first order per trader's account (cached in-process); no manual setup required by consumers.

For the full coding patterns and recommended snippets, see [AGENTS.md](AGENTS.md).
