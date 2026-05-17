# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.2.3] — Signature-recovery regression suite + audit-pass docs refresh

No library code changes. This release pins everything that 1.2.1 / 1.2.2 fixed
and refreshes the docs to reflect the live-mainnet audit.

### Tests
- New `HlSignatureRecoveryRegressionTests` (21 facts) — for every signed action
  the library can produce, sign it with a known private key, then ECDSA-recover
  the signer address from the resulting `(digest, r, s, v)` and assert it
  equals the signing key's public address. This is the exact firewall that
  would have caught the 1.2.1 user-signed prefix bug and the 1.2.2 trigger
  field-order bug. Covers:
  - L1 actions: `order` (Limit GTC, with clientOrderId, with vaultAddress, with
    expiresAfter), Stop-Market / Stop-Limit / Take-Profit trigger orders,
    `cancel`, `cancelByCloid`, `modify`, `batchModify`, `scheduleCancel`,
    `updateLeverage`.
  - User-signed actions: `usdClassTransfer`, `usdSend`, `spotSend`, `withdraw3`,
    `approveAgent`, `approveBuilderFee`.

### Docs
- All status-line version references bumped 1.2.0 → 1.2.3 across `README`,
  `AGENTS`, `CLAUDE`, `copilot-instructions`, cursor rules, `llms.txt`,
  `docs/index`.
- README + AGENTS now record the live mainnet audit result and the recovery
  regression suite explicitly, so prospective users see both the verification
  story and the bug-class firewall.

## [1.2.2] — Trigger orders, modify response, sub-accounts null (live-audit fixes)

Three more HL mainnet bugs surfaced by a comprehensive live audit run that
exercises every read method, every order type, and every batch / modify /
cancel variation against a real master / agent account.

### Fixed
- **Trigger orders (Stop-Market / Stop-Limit / Take-Profit) were broken since
  1.0.0** — the wire-format for the inner `trigger` dict had its fields in the
  wrong order (`{triggerPx, isMarket, tpsl}` instead of HL's required
  `{isMarket, triggerPx, tpsl}`). msgpack preserves insertion order, the action
  hash is computed over `msgpack(action)`, and HL rejected every trigger order
  signature with `"User or API Wallet 0x… does not exist"` pointing at the
  garbled recovered address. Fixed by reordering the fields to match the
  hyperliquid-python-sdk reference.
- **`Orders.ModifyAsync` threw `KeyNotFoundException`** when HL responded with
  the `{type:"default"}` bare-success envelope instead of `{type:"order",data:…}`.
  Now defensively probes for both shapes and returns a sensible `ModifyResult`
  in either case.
- **`Account.GetSubAccountsAsync` threw `ExchangeApiException: null JSON
  document`** for addresses with no sub-accounts — HL returns the literal JSON
  `null` instead of `[]`. Now treats `null` as an empty list.

### Tests
- New `HyperLiquidMainnetFullAuditTests` — comprehensive gated live-mainnet
  exercise that walks 11 sections: every read method on Markets / Account /
  Positions / Trades / Orders / Vaults / Staking, every order type
  (Limit GTC / ALO / IOC / FOK + Stop-Market + Stop-Limit + Take-Profit),
  Modify, PlaceBatch × 3, ModifyBatch × 2, CancelBatch, CancelByClientId,
  CancelAll, ScheduleCancel (expected $1M volume gate), and 9 stream
  subscriptions (5 public + 4 user). All passing on live mainnet against an
  agent wallet. Skipped unless every required env var is set
  (`EASYTRADING_LIVE_AUDIT=1` + master / agent key creds).

## [1.2.1] — Critical mainnet user-signed action fix + builder approval resilience

### Fixed — user-signed EIP-712 typeHash (CRITICAL — affected every transfer / withdraw / approval on mainnet since 1.0.0)

The `Signer.UserSignedDigest` typeHash was computed against
`"<ActionName>(field1 type1,...)"` instead of the HL-required
`"HyperliquidTransaction:<ActionName>(field1 type1,...)"`. The missing
`HyperliquidTransaction:` prefix changed the EIP-712 typeHash, which changed the
structHash, which changed the digest, which made the signature recover on an
unrelated address. HyperLiquid then rejected the request with
`Must deposit before performing actions. User: 0x…` pointing at the wrong recovered
address.

Affected every user-signed action across the library:
`withdraw3`, `usdSend`, `spotSend`, `usdClassTransfer`, `approveBuilderFee`,
`approveAgent`. Verified live against HL mainnet during 1.2.1 development.

L1 (order / cancel / modify) signing was NOT affected — it uses a separate
phantom-agent digest that didn't have the prefix bug.

The fix is one-line in `Signer.UserSignedDigest` — prepend the prefix unless the
caller already passes it. All existing call sites pick up the fix automatically.

### Fixed — builder fee approval resilience
- `approveBuilderFee` failures (typical when signing with an agent wallet — HL
  requires the master wallet for that specific action) no longer abort
  `PlaceAsync` / `PlaceBatchAsync`. The library logs a warning, skips builder
  attribution for that single order, and re-attempts approval on every subsequent
  order until it succeeds. Previously the entire order would throw, blocking all
  trading on mainnet for setups that follow the recommended master/agent split.
- Approval-failure cache poisoning: a failed approval no longer marks the
  `(user, builder)` pair as approved — so a manual UI approval is picked up on the
  very next order.

### Added
- `HyperLiquidCredentials.MasterPrivateKey` (optional). When supplied, used
  exclusively for `approveBuilderFee` so that agent-keyed setups can complete the
  one-time approval without manual UI steps. Falls back to `PrivateKey` when null.
- `ExchangeClient.SendUserAsyncWithKey` overload that signs a user-action with an
  explicit private key (rather than the credentials' default). Used internally for
  the master-key approval path; available to advanced consumers that need the same
  separation.
- `Orders` now accepts an optional `ILogger?` (wired automatically by
  `HyperLiquidClient`) so the approval-failure warning surfaces through whatever
  logging pipeline the host has configured.

### Docs
- README clarifies the master-vs-agent requirement for builder approval and links
  to [`app.hyperliquid.xyz/builderCodes`](https://app.hyperliquid.xyz/builderCodes)
  for manual one-time approval, plus the `MasterPrivateKey` programmatic path.

### Tests
- New `HlSignerTests.User_signed_signature_recovers_to_signing_key_address` —
  the regression test that would have caught the typeHash bug. Signs a
  `usdClassTransfer` action, then ECDSA-recovers the signer address from the
  resulting (digest, r, s, v) and asserts it equals the signing key's public
  address. Until now, no test verified end-to-end recovery for user-signed
  actions — only signature shape. This is the test the v1.0.0 → v1.2.0 line
  was missing.
- `HlSignerTests.User_signed_typeHash_uses_HyperliquidTransaction_prefix`
  asserts idempotency: passing the primary type with or without the
  `HyperliquidTransaction:` prefix produces the same digest.
- New `HyperLiquidMainnetSmokeTests` (gated, never auto-runs) provides a
  one-shot live-mainnet PlaceLimit + Cancel through a real master / agent
  pair. Used during 1.2.1 development to verify the fixes end-to-end against
  real HL.
- New `HlBuilderApprovalFallbackTests` covers four scenarios end-to-end through a
  scripted `HttpMessageHandler`:
  1. Approve fails → order still placed with no `builder` field on the wire.
  2. Approve fails → `(user, builder)` cache not poisoned (next call re-checks
     `maxBuilderFee`).
  3. Approve fails → a warning-level log entry mentions both the master address
     and the `builderCodes` URL.
  4. `MasterPrivateKey` supplied → captured approve signature recovers to the
     master key (verified by re-signing with the candidate key and comparing
     against the wire `(r, s, v)`, since RFC-6979 makes secp256k1 ECDSA
     deterministic).

## [1.2.0] — dYdX v4 goes live on NuGet

Lands the complete Cosmos SDK transaction-signing path for dYdX v4 and ships it.
Markets reads (Phase 7.0) and Indexer signed reads (Phase 7.1) were already wired;
Phase 7.2 closes the loop with end-to-end signed writes through the validator's
REST broadcast endpoint. Verified end-to-end on testnet (PlaceLimit + Cancel of a
far-from-market BTC-USD post-only buy from a freshly-generated, faucet-funded
wallet — chain accepts both the placement and the cancel), so `EasyTrading.Dydx`
flips to `<IsPackable>true</IsPackable>` and publishes alongside HL / Aster at 1.2.0.

### Fixed — dYdX subticks formula

- **`MarketsCache.ToSubticks`** — sign bug in the exponent caused subticks to be
  scaled by `10^13` instead of `10^5` for BTC-USD, i.e. every order was 10⁸ times
  bigger than intended. The chain rejected with `NewlyUndercollateralized` even
  for trivial sizes. Derivation corrected to
  `subticks = price × 10^(atomicResolution − quoteAtomicResolution − qce)`
  (was: `quoteAtomic − atomic − qce` with a flipped sign on the first two terms).
  Cross-checked against the Indexer's `subticksPerTick` for BTC: at tickSize=$1
  and subticksPerTick=100,000, one USD step = 10⁵ subticks. The `MarketsCacheTests`
  expected values are corrected to match.

Added — Cosmos signing foundation:

- **`Signer`** — BIP-39 mnemonic → BIP-32 derivation at the Cosmos default path
  (`m/44'/118'/0'/0/0`) → secp256k1 keypair → bech32 `dydx1…` address. ECDSA
  signing uses BouncyCastle's RFC-6979 deterministic-k path with low-S
  canonicalisation. Returns the 64-byte raw `r ‖ s` Cosmos signature format.
- **Vendored .proto + Grpc.Tools codegen** — vendored 27 .proto files from
  cosmos-sdk and dydxprotocol/v4-chain (plus gogoproto / cosmos_proto / amino
  extensions). Compiled at build time via `<Protobuf>` items in the csproj
  (`GrpcServices="None"` since we broadcast via REST, not gRPC).
- **`TransactionBuilder`** — assembles `TxBody` + `AuthInfo` + `SignDoc`,
  signs SHA-256(SignDoc), and packs `TxRaw`. Cosmos-correct `Any.TypeUrl` prefix
  ("/" rather than "type.googleapis.com/").
- **`CosmosClient`** — `GetAccountAsync(address)` fetches account_number + sequence
  from `/cosmos/auth/v1beta1/accounts/{address}`; `BroadcastAsync(txBytes, mode)`
  POSTs base64-encoded `TxRaw` to `/cosmos/tx/v1beta1/txs`. Maps responses to a
  typed `BroadcastResult(Success, TxHash, ErrorMessage)`.
- **`MarketsCache`** — caches per-market `atomicResolution` +
  `quantumConversionExponent` from `/perpetualMarkets` and exposes
  `ToQuantums(decimal)` / `ToSubticks(decimal)` so callers can hand the module
  ordinary `decimal` price + size and the conversion to dYdX's on-chain integer
  representation happens transparently.

Wired writes:

- **`Orders.PlaceLimitAsync`** — builds a LONG_TERM `MsgPlaceOrder` (good_til_block_time
  = now + 2 min), packs into a Cosmos tx, signs, and broadcasts via REST.
  `TimeInForce.Gtc/Ioc/Fok/Alo` map onto dYdX's analogues (Fok → Ioc since dYdX
  deprecated their FILL_OR_KILL enum value). `clientOrderId` accepts a uint32
  string; if omitted, the module generates a random one.
- **`Orders.CancelByClientIdAsync`** — wraps `MsgCancelOrder` with the same
  subaccount + clob-pair + client_id triplet. Cosmos cancels are stateless and
  reference the placement triple, not a single integer id; `CancelAsync(long)`
  therefore now throws `NotSupportedException` directing callers to use the
  client-id flavour.
- Methods that don't translate cleanly onto dYdX v4 throw `NotSupportedException`
  with explanatory text: `PlaceBatchAsync`, `ModifyAsync` (no native modify on
  dYdX — cancel + re-place), `CancelBatchAsync`, `ScheduleCancelAsync`,
  `PlaceTwapAsync` (no TWAP), and transfers' spot↔perp methods (no separate
  spot account on v4).

Configuration:

- `DydxClientOptions.ValidatorRestUrl` + `ChainId` (replacing the unused
  `ValidatorGrpcUrl` slot from 7.0).
- `GetEffectiveValidatorRestUrl()` defaults to public community endpoints
  (`https://dydx-dao-api.polkachu.com` for mainnet,
  `https://dydx-testnet-api.polkachu.com` for testnet) — both verified
  reachable when this was wired.
- `GetEffectiveChainId()` defaults to `dydx-mainnet-1` / `dydx-testnet-4`.

Dependencies (central package management):

- `NBitcoin 8.0.5` — BIP-39 + BIP-32 + bech32.
- `BouncyCastle.Cryptography 2.5.1` — secp256k1 ECDSA with RFC-6979.
- `Google.Protobuf 3.28.3` — runtime for the codegen'd messages.
- `Grpc.Tools 2.66.0` (PrivateAssets="All") — protoc + the MSBuild target.

Tests — 145 unit + 6 integration green:

- `SignerTests` (7) — deterministic, low-S form, address derivation matches what
  Keplr / Leap produce for the BIP-39 test mnemonic.
- `TransactionBuilderTests` (5) — deterministic bytes for same inputs, parseable
  `TxRaw`, signature differs by sequence / chain_id, Cosmos-correct typeUrl prefix.
- `MarketsCacheTests` (5) — quantum / subtick conversion matches the on-chain
  reference for BTC-USD (`humanSize × 10^10` quantums, `humanPrice × 10^5`
  subticks) and rejects negative inputs.
- `DydxClientSmokeTests` (12) — construction, URL resolution, capability flags,
  AuthenticationException on signed paths without credentials.
- `DydxIntegrationTests` (6 reads + 1 write) — live mainnet reads
  (perpetualMarkets, depth, allMids), WS trades for BTC-USD, the validator
  account-query for the well-known test mnemonic, **and the end-to-end
  Testnet_PlaceLimit_and_Cancel write**: places a far-from-market post-only
  BTC-USD buy at half the live mid and cancels it by client id; the chain
  accepts both placements. Gated by `DYDX_TESTNET_MNEMONIC` so library users
  without a testnet wallet still get green tests by default.
- `TestnetBootstrap` (1) — gated helper that generates a fresh BIP-39 mnemonic,
  derives the `dydx1…` address, and POSTs to the public testnet faucet at
  `https://faucet.v4testnet.dydx.exchange/faucet/tokens` to seed a brand-new
  wallet with USDC. Persists the mnemonic to `%TEMP%/easytrading-dydx-testnet.mnemonic`
  so the follow-up write test can pick it up via `DYDX_TESTNET_MNEMONIC`. Gated
  by `EASYTRADING_BOOTSTRAP_FAUCET=1` + `EASYTRADING_INTEGRATION=1` — never runs
  in CI.

### Earlier dYdX phases folded into 1.2.0

Phases 7.0 (Indexer REST + public WebSocket), 7.1 (signed Indexer reads), and 7.2
(Cosmos SDK transaction signing) shipped together in 1.2.0. The release notes
below are the original phase entries kept for archaeological reference.

#### Phase 7.0 — `EasyTrading.Dydx` scaffold

First dYdX v4 work. The scaffold landed under `src/EasyTrading.Dydx/` with
Markets reads + public WebSocket streams working end-to-end against live mainnet.

#### Added — `EasyTrading.Dydx`

- `DydxClient`, `IDydxExchange`, `DydxClientOptions`, `DydxCredentials`, `DydxNetwork`
  (Mainnet / Testnet), `DydxRetryOptions`.
- `Markets` — all 10 `IMarkets` methods backed by the v4 Indexer REST
  (`/perpetualMarkets`, `/orderbooks/perpetualMarket/{ticker}`, `/candles`,
  `/historicalFunding/{ticker}`, `/trades/perpetualMarket/{ticker}`).
- `WebSocketClient` — Binance-style multiplex over `wss://indexer.dydx.trade/v4/ws`.
- `Streams` — public channels (`v4_trades`, `v4_orderbook`, `v4_candles`, `v4_markets`).
  `BestBidOffer` derived client-side from the orderbook channel.
- DI: `services.AddEasyTrading().AddDydx(o => o.Network = DydxNetwork.Mainnet)`.
- Stubs for `Orders`, `Positions`, `Trades`, `Account`, `Transfers`, and user `Streams` —
  each throws `NotImplementedException` with a Phase 7.1 / 7.2 pointer (or
  `NotSupportedException` for TWAP / spot ↔ perp transfers which don't exist on dYdX v4).

#### Notes

- dYdX v4 is built on a Cosmos SDK app-chain (CometBFT). Signing for writes is **secp256k1
  over Cosmos transaction protobufs**, NOT EIP-712 (that was v3 / StarkEx, deprecated).
  Phase 7.2 will add Cosmos transaction building + validator gRPC broadcast.
- 11 unit tests + 4 integration tests against live mainnet (`perpetualMarkets`,
  `orderbooks/BTC-USD`, `allMids`, WebSocket `v4_trades` for BTC-USD) — all green.

## [1.1.1] — Drop venue prefix from internal types

Internal-only polish. The public surface (`AsterClient`, `HyperLiquidClient`, options, credentials, network enums, builder, interfaces) is unchanged.

### Changed

Renamed `internal` types in both venues to drop the venue prefix — they live in venue-specific
namespaces (`EasyTrading.HyperLiquid.Infrastructure`, `EasyTrading.Aster.Modules`, …) so the
prefix was redundant and noisy in stack traces and code reads. The cross-DEX interfaces
(`IOrders`, `IAccount`, …) and public client / options types keep their distinctive prefix so
mixed-venue codebases can import both without collisions.

Examples:

- `HlOrders` → `Orders`        in `EasyTrading.HyperLiquid.Modules`
- `HlSigner` → `Signer`        in `EasyTrading.HyperLiquid.Infrastructure`
- `HlWebSocketClient` → `WebSocketClient` in `EasyTrading.HyperLiquid.Infrastructure`
- `AsterOrders` → `Orders`     in `EasyTrading.Aster.Modules`
- `AsterRestClient` → `RestClient` in `EasyTrading.Aster.Infrastructure`
- …and so on for every `internal sealed class` in `Modules/` and `Infrastructure/`.

### Impact

- **Public API**: zero change. Source-compatible for every consumer of `EasyTrading.HyperLiquid`
  or `EasyTrading.Aster`.
- **Tests**: in-tree test projects (which reach into internals via `InternalsVisibleTo`) updated
  in lockstep.
- **Binary compat**: code that reflected on internal type names would need to update — almost
  certainly nobody in the wild.

Tests: 89 HL + 25 Aster = **114 green** on `net8.0` + `net9.0`. Build clean.

## [1.1.0] — Aster Finance client (full surface) + HyperLiquid bumped to 1.1.0

This release ships **EasyTrading.Aster `1.1.0`** with a full Aster Finance V3 surface:
REST reads + writes + EIP-712 signing + WebSocket streams. `EasyTrading.HyperLiquid` /
`Abstractions` / `Core` are also bumped to `1.1.0` from `1.0.3` for version alignment; the
HyperLiquid surface itself is unchanged from `1.0.3`.

### Added — `EasyTrading.Aster` (feature-complete, was scaffold-only in `1.1.0-alpha.1`)

- **`AsterSigner`** — EIP-712 signer matching Aster's published flow: domain
  `AsterSignTransaction` v1, chainId 1666, verifyingContract `0x0`; signs the URL-encoded
  form of each request (with `nonce` + `signer` appended) and produces a 65-byte
  `r ‖ s ‖ v` hex signature ready to drop into the request as `signature`.
- **`AsterRestClient`** — public + signed transport. `SendSignedAsync` injects nonce/signer,
  EIP-712-signs, and posts via `application/x-www-form-urlencoded` (POST/PUT) or query string
  (GET/DELETE). Honours the project-wide retry policy via `AsterHttp`.
- **`AsterMetaCache`** — caches per-symbol `PRICE_FILTER`, `LOT_SIZE`, `MIN_NOTIONAL` from
  `/fapi/v3/exchangeInfo`; refreshed on demand.
- **`AsterOrderValidator`** — client-side rejection on tick / step / min-qty / max-qty /
  min-notional violations before the network round-trip.
- **`AsterOrders`** — `Place / PlaceLimit / PlaceMarket / PlaceStop / PlaceBatch /
  Modify / ModifyBatch / Cancel / CancelByClientId / CancelBatch / CancelAll /
  ScheduleCancel`. (`PlaceTwap` / `CancelTwap` throw `NotSupportedException` — Aster's V3
  API doesn't expose a TWAP order type.)
- **`AsterPositions`** — `GetAll / Get / SetLeverage / SetMarginMode / AddMargin /
  ReduceMargin / Close`. Margin-type idempotency: `-4046` ("no need to change margin type")
  is treated as success.
- **`AsterTrades`** — `GetMyFills(symbol, from, to)`. Aster requires `symbol` on
  `/fapi/v3/userTrades`, so the cross-DEX `GetMyFillsByOrderAsync(orderId)` throws
  `NotSupportedException` directing callers to provide a symbol hint.
- **`AsterAccount`** — `GetState / GetBalance(s) / GetFees / GetPortfolio / GetSubAccounts /
  GetRateLimit / ApproveAgent`. (`Portfolio`, `SubAccounts`, `RateLimit` return empty
  snapshots until Aster exposes the corresponding V3 endpoints.)
- **`AsterTransfers`** — `Withdraw / TransferUsd / SpotToPerp / PerpToSpot / ToSubAccount`.
- **`AsterWebSocketClient`** — Binance-style multiplex over `wss://fstream.asterdex.com/ws`.
  Subscribe via `{"method":"SUBSCRIBE","params":[…],"id":N}`; reader dispatches by stream
  key reconstructed from `e` + `s` (or by `stream` field on combined-stream replies).
  Exponential reconnect with automatic re-subscribe; `Reconnected` event for downstream
  gap-recovery hooks.
- **`AsterStreams`** — all 9 `IStreams` channels wired up:
  - Market (no auth): `Trades` (aggTrade), `OrderBook` (partial depth 5/10/20),
    `Candles` (kline), `AllMids` (`!markPrice@arr@1s`), `BestBidOffer` (bookTicker).
  - User (listenKey-bound, signed): `MyOrders` (ORDER_TRADE_UPDATE),
    `MyFills` (ORDER_TRADE_UPDATE filtered to `executionType=TRADE`),
    `MyNotifications` (MARGIN_CALL). `MyFundings` is an empty stream pending Aster funding-
    event coverage (use `Trades.GetMyFillsAsync` polling for now).
  - User socket uses a separate connection bound to the `listenKey`; library auto-runs a
    30-min PUT keepalive while the stream is alive.
- **DI**: `services.AddEasyTrading().AddAster(o => o.Credentials = new AsterCredentials(...))`.

### Tests

- 89 HL + 23 Aster = **112 unit tests** all green. New suites:
  - `AsterSignerTests` (5 tests) — determinism, shape (0x + 130 hex + v ∈ {1b,1c}),
    different message / key produces different signature, edge cases.
  - `AsterOrderValidatorTests` (10 tests) — every filter rule + reduce-only short-circuit.
- 8 integration tests against live mainnet (HL 5 + Aster 5: exchangeInfo, depth(BTCUSDT),
  allMids, WS trades(BTCUSDT), WS bookTicker(BTCUSDT)) — all green.

### Notes

- Without a testnet wallet, the **signed write path is verified at unit-test level
  (signer + validator + form-encoding) but not end-to-end against the live Aster venue**.
  When users supply credentials and place their first order, please verify the response
  matches expectations and report any errors to the issue tracker — happy to iterate.
- `EasyTrading.HyperLiquid 1.1.0` is a metadata-version-bump release; the HL public surface
  and behaviour are unchanged from `1.0.3`.

## [1.0.3] — Canonical EasyTrading mark for infrastructure packages + docs URL fix

### Changed

- **`EasyTrading.Abstractions`** and **`EasyTrading.Core`** now ship with the canonical
  EasyTrading "ET" project logo (purple bubble + mint ET monogram) instead of the auto-generated
  approximation from 1.0.2. Same brand asset that lives inside the venue-composite icons.
- **API reference link** updated from `easytrading.pw` (custom domain, not currently mapped)
  to the actual DocFX deployment at <https://polius2007.github.io/EasyTrading/>. All references
  in `README.md`, `docs/index.md`, `docs/getting-started.md`, `docs/recipes.md`, `AGENTS.md`,
  `CLAUDE.md`, and `llms.txt` updated. Once the custom domain is wired through GitHub Pages,
  switching back is a one-line README change.
- `docs/index.md` status block bumped to reflect 1.0.x and Phase 6.0 Aster scaffold.

No code changes; build clean, tests still 98/98 green.

## [1.0.2] — NuGet metadata polish (Authors, ProjectUrl, per-package tags + icons)

Tightens up every package's NuGet listing so each one is independently discoverable and
visually consistent on nuget.org.

### Changed

- **`Authors`** / **`Company`** changed from `EasyTrading.pw` to `Elinesoft` — the legal entity
  behind the project. The brand "EasyTrading.pw" is retained as the friendly product name in
  the package description and on the website.
- **`PackageProjectUrl`** now points to the GitHub repository
  (`https://github.com/polius2007/EasyTrading`) rather than the marketing site. Source and
  releases live there; the docs site (`easytrading.pw`) is still linked from the README and
  from the auto-generated DocFX deployment.
- **`PackageTags`** now use a layered approach: a project-wide base set lives in
  `Directory.Build.props`, and each csproj appends its own venue-specific tags via
  `$(PackageTags);…`. Examples:
  - Abstractions → `…;abstractions;interfaces;contracts`
  - Core → `…;core;http;websocket;polly;retry`
  - HyperLiquid → `…;hyperliquid;hl;eip712;perp;arbitrum`
  - Aster (when published) → `…;aster;asterdex;eip712;perp;binance-style`
- **Per-package icons** — every published package now ships a custom icon. The two
  infrastructure packages get a clean "ET" monogram (`assets/icons/abstractions.png`,
  `core.png`); HyperLiquid keeps the venue-composite icon shipped in 1.0.1.

### Tests / verification

- 89 HL + 9 Aster = 98 tests green on `net8.0` + `net9.0`.
- `dotnet pack` inspection confirms each nupkg has its own `icon.png` at the root and the
  expected `<authors>`, `<projectUrl>`, `<tags>`, and `<repository>` metadata in `.nuspec`.

## [1.0.1] — Package icons + small polish

`EasyTrading.HyperLiquid` and (forthcoming) `EasyTrading.Aster` now ship with a custom NuGet
icon. The icons live in `assets/icons/` and are referenced from each venue's csproj via
`<PackageIcon>icon.png</PackageIcon>`. The composite shows the venue logo next to a small
EasyTrading "ET" badge — purely a third-party-integration mark, no claim on the venue's
trademark.

### Changed

- `EasyTrading.Aster` is now marked `<IsPackable>false</IsPackable>` until Phase 6.2 lands —
  the scaffold lives in the source tree and on `main` for development, but the half-implemented
  Aster client doesn't reach NuGet until the write surface is in. (The 1.1.0-alpha.1 entry below
  was never published to NuGet — only tagged for the in-tree Aster scaffold work.)
- `IEasyTradingBuilder` and `AddEasyTrading()` moved to `EasyTrading.Abstractions` (same move
  described under 1.1.0-alpha.1). Callers may need to add `using EasyTrading.Abstractions;`.

## [1.1.0-alpha.1] — Aster scaffold (Phase 6.0) — internal, never published

The Aster scaffold landed under this tag for the development branch only. It was never
published to NuGet; the version label has been retired in favour of the 1.0.x line until Phase 6
is feature-complete, at which point Aster will publish as `1.1.0`.

### Added — `EasyTrading.Aster` (not published)

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
