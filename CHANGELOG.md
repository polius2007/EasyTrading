# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.2.1-alpha.1] — Builder-fee handling reorganised

### Changed
- The standalone `EasyTrading.Broker` package was removed. Builder-fee routing now lives inside `EasyTrading.HyperLiquid` itself — simpler dependency graph, one fewer package to install.
- `IBuilder` is no longer part of `IHyperLiquidExchange`'s public surface. The HL-only sub-clients are now just `Vaults` and `Staking`.
- New internal `HlBuilderDefaults` holds the default builder address and fee rate that Phase 3's Exchange-endpoint serializer attaches to every order action.
- New `HyperLiquidClientOptions.BuilderFee` lets callers override the default per client; per-order overrides via `OrderRequest.BuilderFeeOverride` still work.

### Removed
- `EasyTrading.Broker` project, `BrokerOptions`, `IBuilder` interface, `HlBuilder` module, `BuilderApproval` model.

### Notes
- Builder-fee handling on HyperLiquid is now built into the HyperLiquid client by default (small fraction of notional, well below typical taker fees). See [README.md § Disclaimer](README.md#disclaimer) for opt-out instructions.

## [0.2.0-alpha.1] — Phase 2: HyperLiquid Info endpoint (read-only)

### Added
- HTTP infrastructure: `HlInfoClient` wraps `POST /info` with typed and raw JSON variants; shared `HlJsonOptions` handles HyperLiquid's case-sensitive (`t` vs `T`) and string-numeric fields via `NumberHandling.AllowReadingFromString`.
- 30+ raw DTOs covering every consumed Info response: `meta`, `metaAndAssetCtxs`, `spotMeta`, `l2Book`, `candleSnapshot`, `allMids`, `clearinghouseState`, `spotClearinghouseState`, `openOrders`, `frontendOpenOrders`, `orderStatus`, `historicalOrders`, `userFills`, `userFillsByTime`, `fundingHistory`, `userFees`, `userRateLimit`, `portfolio`, `subAccounts`, `vaultDetails`, `userVaultEquities`, `delegations`, `delegatorSummary`, `delegatorRewards`, `userTwapSliceFills`, `maxBuilderFee`.
- `HlMapper` — raw HL DTOs → `EasyTrading.Abstractions.Models` (Symbol, OrderBook, Candle, Position, Order, Fill, AccountState, FundingInfo / FundingRecord, FeeSchedule, RateLimitInfo, SubAccount, Portfolio, VaultDetails / VaultEquity, Delegation / DelegatorSummary / Reward).
- Real implementations across all read methods:
  - `HlMarkets`: `GetSymbols`, `GetSymbol`, `GetOrderBook`, `GetCandles`, `GetAllMids`, `GetMid`, `GetFunding`, `GetFundingHistory`, `GetOpenInterest` (`GetRecentTrades` correctly raises `NotSupportedException` — HL exposes this only via WebSocket).
  - `HlAccount`: `GetState`, `GetBalance(s)`, `GetFees`, `GetPortfolio`, `GetSubAccounts`, `GetRateLimit`.
  - `HlPositions`: `GetAll`, `Get`.
  - `HlOrders`: `GetOpen`, `Get`, `GetByClientId`, `GetHistory`, `GetTwapFills`.
  - `HlTrades`: `GetMyFills`, `GetMyFillsByOrder`.
  - `HlVaults`: `GetDetails`, `GetMyEquities`.
  - `HlStaking`: `GetMyDelegations`, `GetMySummary`, `GetMyRewards`.
  - `HlBuilder`: `GetMaxFee`.
- 11 mapper unit tests using embedded JSON fixtures patterned after live HL payloads.
- 3 read-only integration tests against HL mainnet (`GetAllMids`, `GetSymbols`, `GetOrderBook`), gated by `EASYTRADING_INTEGRATION=1` env var so CI stays offline.
- `InternalsVisibleTo` for the unit-test project (lets tests touch raw DTOs / mapper without exposing them publicly).

### Changed
- `HyperLiquidClient` now owns an `HttpClient` (created internally or caller-supplied) and disposes it on `DisposeAsync`. Each module receives an `HlInfoClient` plus options.
- Modules split out of the Phase-1 `HlStubs.cs` into one file per sub-client.
- Unit-test count: 8 → 38 (+ 3 integration tests).

### Pending
- Phase 3: Exchange endpoint (`order`, `cancel`, `modify`, `withdraw3`, `usdSend`, `spotSend`, `approveAgent`, `approveBuilderFee`, `vaultTransfer`, `tokenDelegate`, …) with EIP-712 signing. All write methods still raise `NotImplementedException` with a message pointing to this phase.
- Phase 4: WebSocket streaming.
- Phase 5: `EasyTrading.Broker` builder-fee decorator.

## [0.1.0-alpha.1] — Phase 1: scaffolding

### Added
- Solution scaffold: `EasyTrading.slnx` with 6 projects (`Abstractions`, `Core`, `HyperLiquid`, `Broker`, unit tests, console sample).
- `EasyTrading.Abstractions` — cross-DEX contracts (`IExchangeClient` + sub-clients `IMarkets`, `IOrders`, `IPositions`, `ITrades`, `IAccount`, `ITransfers`, `IStreams`) and shared models.
- `EasyTrading.HyperLiquid` — `IHyperLiquidExchange` (extends `IExchangeClient`) adding `IVaults`, `IStaking`, `IBuilder`; client skeleton (real exchange calls land in Phase 2+).
- `EasyTrading.Core` — shared infrastructure project (HTTP, WebSocket, signing helpers — implementations land in Phase 2+).
- `EasyTrading.Broker` — builder-fee / rebate decorator project (implementation lands in Phase 5).
- Central package management via `Directory.Packages.props`.
- Shared package metadata via `Directory.Build.props`.
- MIT license.
- GitHub Actions workflows for CI and release.
- DocFX scaffold for the documentation site.

### Known limitations
- All `IExchangeClient` methods currently throw `NotImplementedException`. Real implementations land in Phase 2 onward.
