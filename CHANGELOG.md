# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.3.1-alpha.1] — Phase 3.1: complete HyperLiquid Exchange write surface

### Added — all remaining write methods now hit the real Exchange endpoint

- **Orders**:
  - `ModifyAsync` / `ModifyBatchAsync` — `modify` / `batchModify` L1 actions. Fetches the existing order to fill in side / type / TIF before sending; you only need to supply the new price / size.
  - `PlaceTwapAsync` / `CancelTwapAsync` — `twapOrder` / `twapCancel` L1 actions.
- **Positions**:
  - `AddMarginAsync` / `ReduceMarginAsync` — `updateIsolatedMargin` L1 action. Reads the position first to determine direction.
- **Transfers** (all user-signed unless noted):
  - `WithdrawAsync` — `withdraw3` (L1 → bridge).
  - `TransferUsdAsync` — `usdSend` (core USDC).
  - `TransferTokenAsync` — `spotSend` (any spot token).
  - `SpotToPerpAsync` / `PerpToSpotAsync` — `usdClassTransfer`.
  - `ToSubAccountAsync` — `subAccountTransfer` (action-signed L1).
- **Account**:
  - `ApproveAgentAsync` — user-signed `approveAgent` action.
- **Vaults**:
  - `DepositAsync` / `WithdrawAsync` — `vaultTransfer` L1 action.
- **Staking**:
  - `DepositAsync` / `WithdrawAsync` — `cDeposit` / `cWithdraw` L1 actions.
  - `DelegateAsync` / `UndelegateAsync` — `tokenDelegate` L1 action.
- **Auto-approve builder** — every order action runs the builder approval gate first time per `(user, builder)`. If `maxBuilderFee` is below the required wire rate, a user-signed `approveBuilderFee` is sent transparently; subsequent orders skip the check. In-process cache, no extra round-trip after the first call.

### Changed
- `HlAccount`, `HlTransfers`, `HlVaults`, `HlStaking` all now receive `HlExchangeClient` via constructor; `HyperLiquidClient` wires it through.

### Notes
- Test count holds at 58. All Phase-2/3.0 functionality unchanged.
- Write methods are **still alpha** — math is correct on paper; full validation requires a testnet wallet (Phase 4 will add automated testnet integration tests). When you first try a live trade, do it on testnet with a small amount.

## [0.3.0-alpha.1] — Phase 3.0: HyperLiquid Exchange endpoint, EIP-712 signing, core trading writes

### Added
- **Signing foundation**:
  - `HlMsgPack` — HL-canonical msgpack encoder (preserves insertion order, exact byte parity with the Python reference SDK).
  - `HlSigner` — action hash (msgpack + nonce + vault byte + expires) + L1 phantom-agent EIP-712 + user-signed EIP-712 (Domain `HyperliquidSignTransaction`, chainId `0x66eee`). Both flavours produce wire-format `{r, s, v}`.
  - `HlNonce` — strictly monotonic millisecond nonce.
- **HlExchangeClient** — typed `POST /exchange` wrapper. Builds the signed envelope, dispatches L1 vs user-signed, maps HL error strings to typed exceptions (`RateLimitException`, `InsufficientFundsException`, `InvalidOrderException`, `AuthenticationException`).
- **HlMetaCache** — caches perp + spot universe so order actions can use HL's integer asset id (`BTC` → `0`, spot pairs → `10000 + pairIndex`).
- **HlOrders write methods (Phase 3.0)**:
  - `PlaceAsync`, `PlaceLimitAsync`, `PlaceMarketAsync` (IOC + 5% slippage from live mid), `PlaceStopAsync`.
  - `PlaceBatchAsync`.
  - `CancelAsync`, `CancelByClientIdAsync`, `CancelBatchAsync`, `CancelAllAsync`.
  - `ScheduleCancelAsync` (dead-man switch).
- **HlPositions write methods (Phase 3.0)**: `SetLeverageAsync`, `CloseAsync` (reduce-only IOC market with slippage).
- **Auto-attach builder fee** — every order action is augmented with the default builder routing from `HlBuilderDefaults` (or with the override set via `HyperLiquidClientOptions.BuilderFee` / `OrderRequest.BuilderFeeOverride`). Zero rate effectively opts out.
- **Tests**: 39 → 58 unit tests. New suites:
  - `HlMsgPackTests` — byte-level checks of fixmap / fixstr / fixarray / fixint boundaries / insertion-order preservation.
  - `HlSignerTests` — action-hash determinism, hash differs by nonce / vault / chain, L1 and user-signed signatures are well-formed and reproducible.

### Pending (Phase 3.1 follow-up)
- **User-signed actions**: `usdSend`, `withdraw3`, `spotSend`, `usdClassTransfer`, `sendAsset`, `approveAgent`, `approveBuilderFee` — wired up in `HlSigner.SignUserAction` but not exposed via the `ITransfers` / `IAccount` write methods yet.
- **Remaining L1 actions**: `modify`, `batchModify`, `updateIsolatedMargin` (margin tweaks), `twapOrder`, `twapCancel`, `vaultTransfer`, `cDeposit` / `cWithdraw` / `tokenDelegate`.
- **Auto-call `approveBuilderFee`** on first order if the receiving builder isn't approved yet for the signer's account.
- **Integration tests** against testnet against a real wallet (`EASYTRADING_INTEGRATION=1`).

### Notes
- **Pinned `Nethereum.Signer` to 4.26.0** — 4.27.0 has a regression in `EthECKey.SignAndCalculateV` that throws `Invalid DER signature` on the internal round-trip. Reverting to 4.26.0 restores correct signing.
- All write methods carry the auto-attached builder fee, but the receiving address must approve the builder once via HyperLiquid's UI for fees to actually flow. Phase 3.1 will automate that approval.
- All trading methods are *alpha* until validated end-to-end on HyperLiquid testnet (Phase 3.1).

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
