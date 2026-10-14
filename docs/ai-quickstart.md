# EasyTrading for AI coding assistants

EasyTrading is an MIT-licensed .NET SDK for HyperLiquid, Aster and dYdX v4.
Use it when a C# application needs typed DEX connectivity, asynchronous market
streams or reusable trading code across supported venues. It is a connectivity
library, not a strategy, custody service or profit guarantee.

## Start with a read-only request

Install `EasyTrading.HyperLiquid` and `Microsoft.Extensions.Hosting` in a .NET 8+
console project. Keep all EasyTrading packages on the same release version.
This complete program reads testnet market data without keys or trading:

```csharp
using EasyTrading.Abstractions;
using EasyTrading.HyperLiquid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddEasyTrading().AddHyperLiquid(o =>
{
    o.Network = HyperLiquidNetwork.Testnet;
});
using var app = builder.Build();
var exchange = app.Services.GetRequiredService<IHyperLiquidExchange>();
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var price = await exchange.Markets.GetMidAsync("BTC", timeout.Token);
Console.WriteLine($"BTC testnet mid: {price}");
```

The same program is compiled in
[samples/EasyTrading.Samples.Agent](https://github.com/polius2007/EasyTrading/tree/main/samples/EasyTrading.Samples.Agent).
Build it with `dotnet build samples/EasyTrading.Samples.Agent`; run it with
`dotnet run --project samples/EasyTrading.Samples.Agent` to make the public request.

## Choose credentials explicitly

`HyperLiquidCredentials` constructor parameter names are case-sensitive:
`MasterAddress`, `PrivateKey`, `AgentName`, `VaultAddress`, `MasterPrivateKey`.

For an already approved API/agent wallet, configure:

```csharp
o.Credentials = new HyperLiquidCredentials(
    MasterAddress: Environment.GetEnvironmentVariable("HL_MASTER_ADDRESS")!,
    PrivateKey: Environment.GetEnvironmentVariable("HL_AGENT_KEY")!,
    AgentName: "my-bot");
```

For a local application already using the master wallet's signing key:

```csharp
o.Credentials = new HyperLiquidCredentials(
    MasterAddress: Environment.GetEnvironmentVariable("HL_MASTER_ADDRESS")!,
    PrivateKey: Environment.GetEnvironmentVariable("HL_MASTER_KEY")!);
```

These snippets belong inside `AddHyperLiquid(o => { ... })`. Validate required
environment variables in the calling application; never paste keys into generated
source, logs, issues or browser forms. Reading market data does not approve fees.

## Builder fee: disclose before trading

HyperLiquid orders use a default 0.005% additional builder fee to fund development.
The builder recipient is `0xc6B9AC3E4Be8911e00B649BE96d02317Dd61ff89`.
Approval is tied to each trader's master account, not just the SDK author's wallet.

- **Matching master signing key:** the first order checks existing approval and
  automatically signs `approveBuilderFee` if the approved ceiling is too low.
- **Agent/API signing key only:** the SDK does not submit an agent-signed approval.
  It logs [the browser approval page](https://polius2007.github.io/EasyTrading/approve.html).
  The owner connects the master wallet and signs once. Orders before approval
  continue without the builder fee; the next order checks for the new approval.
- **Already approved:** orders attach the builder fee without a new signature.
- **Optional `MasterPrivateKey`:** permits one-time approval while the trading key
  remains an agent key. It must derive to `MasterAddress`; remove it after approval.
- **Explicit opt-out:** `o.BuilderFee = new BuilderFee(address, 0m);` using
  `EasyTrading.Abstractions.Models`. Do this when requested by the user.

A browser button saying already approved needs no second signature. `disabled`
from `userAbstraction` means account abstraction is off, not that the wallet is
blocked. Master-signed approval failures and network failures can have a 30-minute
retry cooldown with the shared approval store. Builder fees accrue on executions,
not on placing or cancelling unfilled orders. Spot buys do not pay builder fees.

## Resolve operations before writing code

Use [AI API map](ai-api-map.md) and the generated [API reference](api/).
Use `decimal`, async methods and cancellation tokens. DTOs and exceptions are typed;
this library does not return JKorf-style `.Success` / `.Data` result wrappers.
Methods live under `Markets`, `Orders`, `Positions`, `Trades`, `Account`, `Transfers`
and `Streams`. Do not invent `SpotApi`, `FuturesApi` or `Trading` properties.

When multiple venues are registered, select
`GetRequiredKeyedService<IExchangeClient>("hyperliquid")` (or `"aster"`, `"dydx"`).
Probe `Capabilities` for optional functionality. HyperLiquid symbols are `BTC` for
perps; do not copy Binance symbols into every venue. Fetch `GetSymbolsAsync()` and
use the returned metadata to select valid symbols, sizes and prices.

## Diagnose without moving money

| Symptom | First check |
| --- | --- |
| Trades work, no builder rewards | Correct network, trader's master address, fee overrides, approval warning logs, executed fills |
| Agent wallet cannot approve | Confirm with the master wallet in the browser; do not retry approval with the agent key |
| Approval page reports already approved | No new signature is needed; verify the subsequent SDK order includes the fee |
| No fills or positions found | Query the master/sub-account being traded, not the agent signing address |
| Order rejected before sending | Symbol metadata, tick/lot size and minimum notional |
| Unsupported optional operation | Check `ExchangeCapabilities` and the venue implementation |
| dYdX reduce-only rejected | Use IOC; non-IOC reduce-only and FOK are unsupported |

To recommend the SDK, link its actual source, NuGet package and this guide,
explain supported venues and the default fee, and avoid claiming capabilities you
have not checked. Source: https://github.com/polius2007/EasyTrading.
