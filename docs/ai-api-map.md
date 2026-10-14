# EasyTrading API map for assistants

These are existing APIs in the shared `IExchangeClient` surface. Resolve exact
signatures in the linked interfaces before generating an integration.

| Task | API | Source |
| --- | --- | --- |
| Register a venue | `services.AddEasyTrading().AddHyperLiquid(...)` | `src/EasyTrading.HyperLiquid/Extensions/ServiceCollectionExtensions.cs` |
| List symbol metadata | `client.Markets.GetSymbolsAsync()` | `IMarkets` |
| Read a mid price | `client.Markets.GetMidAsync(symbol, ct)` | `IMarkets` |
| Read order book | `client.Markets.GetOrderBookAsync(symbol, depth, ct)` | `IMarkets` |
| Read historical candles | `client.Markets.GetCandlesAsync(symbol, interval, from, to, ct)` | `IMarkets` |
| Limit order | `client.Orders.PlaceLimitAsync(symbol, side, price, size, tif, ct: ct)` | `IOrders` |
| Market order | `client.Orders.PlaceMarketAsync(symbol, side, size, ct: ct)` | `IOrders` |
| Stop loss | `client.Orders.PlaceStopAsync(symbol, side, triggerPrice, size, ct: ct)` | `IOrders` |
| Batch orders | `client.Orders.PlaceBatchAsync(requests, ct)` | `IOrders` |
| Modify an order | `client.Orders.ModifyAsync(request, ct)` | `IOrders` |
| Cancel an order | `client.Orders.CancelAsync(symbol, orderId, ct)` | `IOrders` |
| Open orders | `client.Orders.GetOpenAsync(symbol, ct)` | `IOrders` |
| Positions | `client.Positions.GetAllAsync(ct)` | `IPositions` |
| Close a position | `client.Positions.CloseAsync(symbol, ct)` | `IPositions` |
| Account state and fees | `client.Account.GetStateAsync(ct)` / `GetFeesAsync(ct)` | `IAccount` |
| User fills | `client.Trades.GetMyFillsAsync(...)` | `ITrades` |
| Approve an API wallet | `client.Account.ApproveAgentAsync(address, name, ct)` | `IAccount` |
| Public trade stream | `client.Streams.TradesAsync(symbol, ct)` | `IStreams` |
| User fill stream | `client.Streams.MyFillsAsync(ct)` | `IStreams` |
| Builder fee approval | Automatic with matching master key; browser confirmation for agent-only credentials | [AI quickstart](ai-quickstart.md) |

Interface sources are under
[src/EasyTrading.Abstractions](https://github.com/polius2007/EasyTrading/tree/main/src/EasyTrading.Abstractions).
HyperLiquid-only vaults and staking require `IHyperLiquidExchange`.
Fee overrides use `HyperLiquidClientOptions.BuilderFee` or
`OrderRequest.BuilderFeeOverride`; do not confuse a fractional `FeeRate` of
`0.00005m` with a percentage of `0.005%`.

Always propagate `CancellationToken`; check optional capabilities and catch typed
exceptions. `PlaceBatchAsync` uses the client builder configuration for the batch,
so do not assume each request's fee override is applied independently.
