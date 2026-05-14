# Getting started

This guide walks through a complete first-trade flow on HyperLiquid: install the package, wire up DI, authenticate, place a small testnet order, subscribe to a live stream, and shut down cleanly. Read it once end-to-end, then come back for the [recipes](recipes.md) when you want patterns for specific scenarios.

## Prerequisites

- .NET 8 or 9 SDK
- A HyperLiquid account with at least the minimum USDC balance (10 USDC for the smallest valid order)
- For trading: a private key. Generate a fresh one for the bot — **do not paste your wallet's master key.**

## 1. Install

```bash
dotnet new console -n MyBot
cd MyBot
dotnet add package EasyTrading.HyperLiquid
```

`EasyTrading.HyperLiquid` brings `EasyTrading.Abstractions` and `EasyTrading.Core` transitively.

## 2. First read call (no credentials)

The Info endpoints don't need a signature. This is the fastest sanity check that your network can reach HyperLiquid:

```csharp
using EasyTrading.Abstractions;
using EasyTrading.HyperLiquid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddEasyTrading()
    .AddHyperLiquid(options =>
    {
        options.Network = HyperLiquidNetwork.Mainnet;
    });

using var app = builder.Build();
var ex = app.Services.GetRequiredService<IHyperLiquidExchange>();

var mids = await ex.Markets.GetAllMidsAsync();
Console.WriteLine($"{mids.Count} markets; BTC mid = {mids["BTC"]}");
```

If this prints a non-empty mid for BTC, you're connected. Run it on testnet by swapping `Network = HyperLiquidNetwork.Testnet`.

## 3. Authenticate

Two key concepts:

- **Master key** — your wallet's main private key. Holds your funds.
- **Agent key** — a delegated key that can sign trades for one specific bot. It cannot withdraw funds and can be revoked at any time.

**Always use an agent key for trading.** Master keys belong in cold storage, not in environment variables.

### One-time agent approval

Approve an agent address from your master key (run this once, then delete the master key from the bot):

```csharp
builder.Services
    .AddEasyTrading()
    .AddHyperLiquid(options =>
    {
        options.Network = HyperLiquidNetwork.Mainnet;
        options.Credentials = new HyperLiquidCredentials(
            masterAddress: "0xYourMasterAddress",
            privateKey:    "<MASTER_PRIVATE_KEY>",  // one-time use
            agentName:     "my-bot");
    });

await ex.Account.ApproveAgentAsync(agentAddress: "0xYourAgentAddress",
                                   agentName:    "my-bot");
```

From now on the bot can sign with the **agent key**:

```csharp
options.Credentials = new HyperLiquidCredentials(
    masterAddress: "0xYourMasterAddress",   // the wallet you want to trade for
    privateKey:    Environment.GetEnvironmentVariable("HL_AGENT_KEY")!,
    agentName:     "my-bot");
```

`masterAddress` is the wallet whose positions and balances the bot trades against; the agent key signs the actions.

## 4. First trade — start on testnet

Set `Network = HyperLiquidNetwork.Testnet`. Pull testnet USDC from the [HyperLiquid testnet faucet](https://app.hyperliquid-testnet.xyz). Then:

```csharp
// Use a price well below the current mid so the order rests, not fills.
var book = await ex.Markets.GetOrderBookAsync("BTC", depth: 1);
var price = Math.Floor(book.Bids[0].Price * 0.9m);  // 10% below best bid

var placed = await ex.Orders.PlaceLimitAsync(
    symbol: "BTC", side: OrderSide.Buy,
    price:  price, size: 0.001m,
    tif:    TimeInForce.Alo);   // Add Liquidity Only — never crosses the spread

Console.WriteLine($"Order id {placed.OrderId}, status {placed.Status}");

// Cancel it cleanly.
await ex.Orders.CancelAsync("BTC", placed.OrderId);
```

If the order is rejected with `InvalidOrderException`, read the message — pre-flight validation tells you exactly what's wrong (decimals, sig figs, min notional, etc.).

## 5. Subscribe to a stream

WebSocket streams return `IAsyncEnumerable<T>`. You iterate with `await foreach` and cancel by cancelling the token:

```csharp
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// Public — no signing.
var trades = Task.Run(async () =>
{
    await foreach (var t in ex.Streams.TradesAsync("BTC", cts.Token))
        Console.WriteLine($"trade {t.Trade.Side} {t.Trade.Size}@{t.Trade.Price}");
});

// User — signed; auto gap-fills missed fills via REST after reconnect.
var fills = Task.Run(async () =>
{
    await foreach (var f in ex.Streams.MyFillsAsync(cts.Token))
        Console.WriteLine($"fill {f.Fill.Symbol} {f.Fill.Side} {f.Fill.Size}@{f.Fill.Price}");
});

await Task.WhenAll(trades, fills);
```

The WebSocket is a single shared connection across all subscriptions. It lazy-connects on first subscribe and reconnects automatically with exponential backoff. Multiple subscribers to the same channel + symbol share one HL subscription — no duplicate traffic on the wire.

## 6. Move to mainnet

When you're confident on testnet, switch `Network = HyperLiquidNetwork.Mainnet` and start with positions sized so the worst-case loss is acceptable. The library will not stop you from making bad trades — it only stops you from sending malformed ones.

## What's next

- **[Recipes](recipes.md)** — common patterns: market orders, stop-losses, batch operations, cross-DEX strategies, custom retry policies.
- **[AGENTS.md](../AGENTS.md)** — convention summary; useful even if you're a human reading it.
- **[API reference](https://easytrading.pw)** — every public type and method with its XML doc.
- **[HyperLiquid API docs](https://hyperliquid.gitbook.io/hyperliquid-docs)** — upstream reference for the venue itself (rate limits, fee schedule, supported actions).
