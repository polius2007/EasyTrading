# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.1.0-alpha.1] — Aster scaffold (Phase 6.0) + IEasyTradingBuilder moved to Abstractions

This release lands the **EasyTrading.Aster** scaffold. The Markets read surface works end-to-end
against Aster Finance live mainnet (exchangeInfo, depth, ticker price, premiumIndex,
fundingRate, recent trades, openInterest). Every other module throws
<c>NotImplementedException</c> with a precise Phase-6.x pointer; subsequent phases land them.

### Added — `EasyTrading.Aster` package

- `AsterClient`, `IAsterExchange`, `AsterClientOptions`, `AsterCredentials`, `AsterNetwork`
  (Mainnet / Testnet), `AsterRetryOptions` — public surface mirrors the HyperLiquid shape.
- `AsterMarkets` — all 10 `IMarkets` methods backed by V3 Futures public endpoints (only
  `GetCandlesAsync` pending Phase 6.1). 3 integration tests verified against live mainnet
  (exchangeInfo, depth(BTCUSDT), allMids).
- `AsterNonce` — strictly monotonic microsecond nonce, matching Aster's V3 nonce window rules.
- `AsterHttp` — shared retry layer (network errors / timeouts / 5xx / 429 with `Retry-After`),
  same contract as HyperLiquid's `HlHttp`.
- DI: `services.AddEasyTrading().AddAster(o => ...)`. Aster is also registered as a keyed
  `IExchangeClient` under the key `"aster"`, so hosts running BOTH HyperLiquid and Aster can
  resolve each by name.

### Pending — subsequent Aster phases

- **Phase 6.1** — signed read endpoints (Account / Positions / Trades / candles).
- **Phase 6.2** — Aster Exchange + EIP-712 signing (`AsterSignTransaction` domain, chainId 1666);
  Orders / Positions writes, Transfers, ApproveAgent.
- **Phase 6.3** — WebSocket market and user data streams (Binance-style listenKey lifecycle).

### Changed — `IEasyTradingBuilder` and `AddEasyTrading()` moved to `EasyTrading.Abstractions`

To let any venue package chain off `IEasyTradingBuilder` without an outward dependency on
`EasyTrading.HyperLiquid`, the `IEasyTradingBuilder` interface and `AddEasyTrading()` extension
moved from `EasyTrading.HyperLiquid` (namespace `EasyTrading.HyperLiquid`) to
`EasyTrading.Abstractions` (namespace `EasyTrading.Abstractions`).

**Source impact**: callers writing `services.AddEasyTrading().AddHyperLiquid(...)` only need to
ensure `using EasyTrading.Abstractions;` is in scope — which almost every existing user already
has, since `IExchangeClient`, `OrderRequest`, `OrderSide`, etc. all live there. Callers who
imported `IEasyTradingBuilder` explicitly with `using EasyTrading.HyperLiquid;` should switch
to `using EasyTrading.Abstractions;`.

**Binary impact**: code compiled against `EasyTrading.HyperLiquid 1.0.0` references
`EasyTrading.HyperLiquid.IEasyTradingBuilder`, which no longer exists in `1.1.0`. Recompile
against the new package; no API-level change is required beyond the namespace import.

### Tests

89 (HyperLiquid) + 6 (Aster smoke) = 95 unit tests, plus 5 (HL) + 3 (Aster) integration tests
against live mainnet — all green on `net8.0` and `net9.0`. Build clean.

## [1.0.0] — HyperLiquid stable release

First stable release. HyperLiquid coverage is feature-complete and the public API is now
frozen under semantic versioning — any incompatible change from here gets a `2.0` bump.

This release is materially identical to `1.0.0-rc.2`: all the hardening work (pre-flight
order validation, REST retry policy with jitter and `Retry-After`, WebSocket gap recovery
for user streams) shipped in the rc line and has been exercised in shakedown. The `1.0.0`
tag flips the surface to "stable" and adds documentation polish.

### Documentation

- **`docs/getting-started.md`** — guided first-trade walk-through covering install,
  agent-wallet setup, testnet-first guidance, and shutdown.
- **`docs/recipes.md`** — common patterns: limit / market / stop, batch place + cancel,
  modify, account reads, leverage, transfers, public + user streams, custom retry policy,
  builder-fee routing, cross-DEX strategies, hosted services.
- README rewritten to a more factual register; links to the new docs.
- `docs/index.md` (DocFX landing page) updated; `llms.txt` regenerated for AI crawlers.

### Tests

89 unit + 5 integration tests, all green on `net8.0` and `net9.0`.

## [1.0.0-rc.2] — Concurrency fix in gap recovery + sample refresh

`1.0.0-rc.1` was tagged but never reached NuGet (the release pipeline's push step was
silently skipped due to a missing `NUGET_API_KEY` repo secret). This release contains
the same hardening surface plus a concurrency fix discovered while preparing the
shakedown.

### Fixed

- **`HlStreamGapFill.BoundedIdSet` was not thread-safe.** After a WebSocket reconnect,
  the pump task (forwarding live events) and the recovery task (REST catch-up) both
  call `TryAdd` from separate `Task.Run` contexts; the underlying `HashSet<long>` and
  `Queue<long>` are not thread-safe and could corrupt under contention. Now serialised
  by an internal lock. Added a regression test that hammers the set from 4 threads
  with overlapping ID ranges.

### Changed

- **Sample console** (`samples/EasyTrading.Samples.Console/Program.cs`) refreshed to
  demonstrate the 1.0-rc.x surface: `RetryPolicy` configuration, pre-flight validation
  rejecting $1 notional + bad price decimals before the network call, ready-to-uncomment
  write / stream blocks. Verified end-to-end against live mainnet (564 markets, BTC
  mid + book live, validation messages precise).

### Tests

- 88 → 89 (+1 thread-safety regression test).

## [1.0.0-rc.1] — Phase 5: hardening for 1.0

This release closes the production-readiness gaps identified in the post-Phase-4 audit:
pre-flight order validation, REST resilience, and WebSocket gap recovery for user-scoped
streams. Public API surface is unchanged — every consumer of `0.4.0-alpha.1` upgrades
without code changes.

### Added — pre-flight order validation

- **`HlOrderValidator`** — runs before any `Orders.Place*` / `Orders.Modify*` / `Orders.PlaceTwap*` call:
  - Size must be > 0 with ≤ `szDecimals` fractional digits.
  - Price must have ≤ 5 significant figures (integer prices are always allowed) and
    ≤ `(IsSpot ? 8 : 6) - szDecimals` fractional digits.
  - Minimum order notional of $10 USDC, skipped for reduce-only orders.
- **`HlMetaCache.GetAssetInfoAsync`** — new richer lookup returning `(AssetId, SzDecimals, IsSpot)` per market.
  The cache now hydrates per-asset metadata at first use; spot pairs derive `SzDecimals` from the
  base token in `spotMeta.tokens`.

Invalid orders now throw `InvalidOrderException` with a precise message before they hit the network.

### Added — REST resilience

- **`HyperLiquidRetryOptions`** — exposed via `HyperLiquidClientOptions.RetryPolicy`. Defaults:
  3 attempts, 200 ms initial delay, ×2 exponential backoff capped at 5 s, ±25% jitter.
- **`HlHttp.PostJsonAsync`** — shared retry layer used by both `HlInfoClient` and `HlExchangeClient`.
  Retries on:
  - Transport errors (`HttpRequestException`).
  - HttpClient-triggered timeouts (caller cancellation is honoured immediately).
  - 5xx and 408 responses (configurable via `RetryOnServerError`).
  - 429 Too Many Requests, honouring the server's `Retry-After` header (configurable via `RetryOnRateLimit`).
- Writes are safe to retry because HyperLiquid de-duplicates by signed nonce.

### Added — WebSocket gap recovery for user streams

- **`HlWebSocketClient.Reconnected`** — new event fires after every successful reconnect +
  re-subscribe cycle.
- **`HlStreamGapFill.WithRecoveryAsync`** — generic helper that wraps a live WS stream:
  - Tracks the maximum event timestamp seen.
  - On each reconnect, calls a per-stream REST callback to fetch events since
    `lastSeenTimestamp − 5 s` (grace window).
  - Deduplicates against a sliding 1024-ID window so events delivered by both the live stream and
    the REST catch-up are emitted exactly once.
- Wired into `Streams.MyOrdersAsync`, `Streams.MyFillsAsync`, `Streams.MyFundingsAsync`. Public
  channels and notifications are unchanged — public data is snapshotted on resubscribe and
  notifications have no REST equivalent.

### Tests

- Unit: 61 → 88 (+15 validator, +8 retry, +4 gap recovery).
- Integration: 5/5 still green against HL mainnet.

### Notes

- This is the first release-candidate. Public API is now frozen for `1.0`; anything that breaks
  the surface gets a `2.0` major bump.
- `HlMetaCache`'s internal lookup dictionary changed from `Dictionary<string, int>` to
  `Dictionary<string, HlAssetInfo>` — internal-only, no caller impact.
- `HlStreams` now takes `HlInfoClient` in its constructor; only `HyperLiquidClient` constructs it,
  so this is transparent.

## [0.4.0-alpha.1] — Phase 4: HyperLiquid WebSocket streaming

### Added
- **`HlWebSocketClient`** — single-connection multiplex over `wss://api.hyperliquid.xyz/ws`. Reader loop, send semaphore, per-subscriber `Channel<T>`, key-based dispatch from incoming `{channel, data}` messages, idempotent `DisposeAsync`. Each subscriber gets its own buffered stream.
- **Reconnect with exponential-ish backoff** capped at 30 s. On reconnect, every active subscription is silently re-sent so consumers don't miss messages after a transient drop.
- **`HlStreams` — all 9 stream methods implemented** against live `wss://api.hyperliquid.xyz/ws`:
  - Public: `TradesAsync`, `OrderBookAsync`, `CandlesAsync`, `AllMidsAsync`, `BestBidOfferAsync`.
  - User-scoped (creds required): `MyOrdersAsync`, `MyFillsAsync`, `MyFundingsAsync`, `MyNotificationsAsync`.
- Per-channel JSON parsers — typed updates (`TradeUpdate`, `OrderBookUpdate`, `CandleUpdate`, `MidUpdate`, `BboUpdate`, `OrderUpdate`, `FillUpdate`, `FundingUpdate`, `NotificationUpdate`).
- **2 new integration tests verified end-to-end against HL mainnet**: `AllMids` (8 s) and `Trades(BTC)` (15 s). Both received live messages and parsed cleanly.

### Tests
- Unit: 59 → 61 (two new smoke tests: public stream enumerators don't throw, user streams without credentials raise `AuthenticationException`).
- Integration (with `EASYTRADING_INTEGRATION=1`): 3 → 5 (+ AllMids WS, + Trades(BTC) WS).

### Notes
- Each call to a stream method opens a fresh `Channel<T>` and yields until the supplied `CancellationToken` fires; back-pressure is per-subscriber. The shared WebSocket is lazy-connected on first subscription and stays open across all subscriptions.
- Two subscribers for the same channel + symbol (e.g. two `TradesAsync("BTC", ct)` callers) share a single HL subscription and each see every message — no duplication on the wire.

## [0.3.2-alpha.1] — Update default builder address

### Changed
- `HlBuilderDefaults.BuilderAddress` updated from `0xf506…19f2` to `0xc6B9AC3E4Be8911e00B649BE96d02317Dd61ff89` — the new EasyTrading.pw revenue address. Builder-fee routing target only; everything else unchanged.

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
