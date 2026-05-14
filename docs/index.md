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
| Withdraw / transfer / approve agent          | `EasyTrading.Abstractions.ITransfers`, `IAccount.ApproveAgentAsync`          |
| Subscribe to WebSocket streams               | `EasyTrading.Abstractions.IStreams`                                          |
| HyperLiquid vaults / staking                 | `EasyTrading.HyperLiquid.IVaults`, `EasyTrading.HyperLiquid.IStaking`        |
| Wire everything up through DI                | `EasyTrading.HyperLiquid.ServiceCollectionExtensions`                        |

## Status

✅ **`1.0-rc.1` — HyperLiquid is production-grade.** Read, write, and WebSocket streaming all work end-to-end against live mainnet, verified by integration tests. EIP-712 signing for L1 and user-signed actions. Pre-flight order validation (tick / lot / min-notional). REST retry policy with backoff + `Retry-After`. WS gap recovery for user streams. Builder fees are auto-attached and auto-approved on the first order.

Aster and dYdX v4 clients are next.

## License

MIT — see [LICENSE](https://github.com/polius2007/EasyTrading/blob/main/LICENSE).
