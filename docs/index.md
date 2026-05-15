# EasyTrading

A .NET client for decentralised perpetual and spot exchanges. The same `IExchangeClient` interface drives every supported DEX; per-DEX packages add only what's venue-specific.

This site is the **API reference**, auto-generated from XML doc-comments on every public type, method, and parameter. For project overview and source see [GitHub](https://github.com/polius2007/EasyTrading).

## New here?

| | |
|---|---|
| Step-by-step first trade           | [Getting started](https://github.com/polius2007/EasyTrading/blob/main/docs/getting-started.md) |
| Common patterns (recipes)          | [Recipes](https://github.com/polius2007/EasyTrading/blob/main/docs/recipes.md) |
| AI / IDE assistant guidance        | [AGENTS.md](https://github.com/polius2007/EasyTrading/blob/main/AGENTS.md) |

## API navigation

| If you want to…                              | Look at                                                                      |
|----------------------------------------------|------------------------------------------------------------------------------|
| Place / cancel / query orders                | `EasyTrading.Abstractions.IOrders`                                           |
| Read markets, order book, candles, funding   | `EasyTrading.Abstractions.IMarkets`                                          |
| Read positions, set leverage, close          | `EasyTrading.Abstractions.IPositions`                                        |
| See your fills                               | `EasyTrading.Abstractions.ITrades`                                           |
| Read balances / fees / portfolio             | `EasyTrading.Abstractions.IAccount`                                          |
| Withdraw / transfer / approve agent          | `EasyTrading.Abstractions.ITransfers`, `IAccount.ApproveAgentAsync`          |
| Subscribe to WebSocket streams               | `EasyTrading.Abstractions.IStreams`                                          |
| HyperLiquid vaults / staking                 | `EasyTrading.HyperLiquid.IVaults`, `EasyTrading.HyperLiquid.IStaking`        |
| Configure retries                            | `EasyTrading.HyperLiquid.HyperLiquidRetryOptions`                            |
| Wire everything up through DI                | `EasyTrading.HyperLiquid.ServiceCollectionExtensions`                        |

## Status

**HyperLiquid `1.2.0`** + **Aster `1.2.0`** — both feature-complete and on NuGet (REST + WebSocket + EIP-712 signing on each, with pre-flight validation, REST retry policy, and gap recovery on user streams).

**dYdX v4 `1.2.0`** — stable on NuGet. Indexer REST + public WebSocket + signed reads + full Cosmos SDK transaction signing (BIP-39 → BIP-32 → secp256k1 → bech32 → protobuf `TxRaw` → REST broadcast), end-to-end verified on testnet (PlaceLimit + Cancel from a faucet-funded wallet, chain accepts both).

## License

MIT — see [LICENSE](https://github.com/polius2007/EasyTrading/blob/main/LICENSE).
