# EasyTrading

> Modern, multi-DEX trading client for .NET.

EasyTrading is a unified .NET client for decentralised perpetual and spot exchanges. The same `IExchangeClient` interface drives every supported DEX, so a strategy can switch venues by changing the registration.

This site is the **API reference**, auto-generated from the XML doc-comments on every public type, method, and parameter. See the [README on GitHub](https://github.com/polius2007/EasyTrading) for the project overview, roadmap, and quick start.

## Where to start

| If you want to…                              | Look at                                                                      |
|----------------------------------------------|------------------------------------------------------------------------------|
| Place / cancel / query orders                | `EasyTrading.Abstractions.IOrders`                                           |
| Read markets, order book, candles, funding   | `EasyTrading.Abstractions.IMarkets`                                          |
| Read positions, set leverage, close          | `EasyTrading.Abstractions.IPositions`                                        |
| See your fills                               | `EasyTrading.Abstractions.ITrades`                                           |
| Read balances / fees / portfolio             | `EasyTrading.Abstractions.IAccount`                                          |
| Withdraw / transfer                          | `EasyTrading.Abstractions.ITransfers`                                        |
| Subscribe to WebSocket streams               | `EasyTrading.Abstractions.IStreams`                                          |
| HyperLiquid vaults / staking                 | `EasyTrading.HyperLiquid.IVaults`, `EasyTrading.HyperLiquid.IStaking`        |
| Wire everything up through DI                | `EasyTrading.HyperLiquid.ServiceCollectionExtensions`                        |

## Status

🚧 Alpha — Phase 2 (HyperLiquid Info endpoint) is live; all market-data and account-state reads work against live mainnet. Order placement, transfers, and WebSocket streaming land in Phases 3 and 4. See the [roadmap on GitHub](https://github.com/polius2007/EasyTrading#roadmap).

## License

MIT — see [LICENSE](https://github.com/polius2007/EasyTrading/blob/main/LICENSE).
