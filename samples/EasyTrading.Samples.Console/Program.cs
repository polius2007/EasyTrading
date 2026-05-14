using EasyTrading.Abstractions;
using EasyTrading.HyperLiquid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// ─── EasyTrading sample (1.0-rc.1) ───────────────────────────────────────────
//
// Wires up the HyperLiquid client through DI and walks through every major
// feature of the public surface, organised so each block stands alone.
//
// Read-only sections hit live HyperLiquid mainnet and need no credentials.
// Write / stream sections (commented out) need `options.Credentials` set —
// for production trading use an agent wallet (`ApproveAgentAsync` once, then
// trade with the agent key) rather than the master private key.

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddEasyTrading()
    .AddHyperLiquid(options =>
    {
        options.Network = HyperLiquidNetwork.Mainnet;

        // ── Resilience knobs (new in 1.0-rc.1) ─────────────────────────────
        // Defaults are sensible (3 attempts, 200ms initial, 2x backoff capped
        // at 5s, ±25% jitter, honours Retry-After). Tweak per environment.
        options.RetryPolicy = new HyperLiquidRetryOptions
        {
            MaxAttempts        = 4,
            InitialDelay       = TimeSpan.FromMilliseconds(250),
            MaxDelay           = TimeSpan.FromSeconds(10),
            BackoffMultiplier  = 2.0,
            RetryOnServerError = true,
            RetryOnRateLimit   = true,
        };

        // ── Credentials (uncomment to exercise write / stream methods) ─────
        // options.Credentials = new HyperLiquidCredentials(
        //     masterAddress: "0xYourMasterAddress",
        //     privateKey:    Environment.GetEnvironmentVariable("HL_PRIVATE_KEY")!,
        //     agentName:     "easy-bot");
    });

using var app = builder.Build();
var ex = app.Services.GetRequiredService<IHyperLiquidExchange>();
var ct = CancellationToken.None;

Console.WriteLine($"Connected to {ex.ExchangeId}");
Console.WriteLine($"Capabilities: {ex.Capabilities}");
Console.WriteLine();

// ─── 1. Read — live mainnet, no creds ────────────────────────────────────────

Console.WriteLine("─── 1. Read (live mainnet) ─────────────────────────────────────");

var mids = await ex.Markets.GetAllMidsAsync(ct);
Console.WriteLine($"  GetAllMidsAsync       → {mids.Count} markets; BTC = {mids["BTC"]}");

var book = await ex.Markets.GetOrderBookAsync("BTC", depth: 3, ct: ct);
Console.WriteLine($"  GetOrderBookAsync     → best bid {book.Bids[0].Price}, best ask {book.Asks[0].Price}");

var symbols = await ex.Markets.GetSymbolsAsync(ct: ct);
Console.WriteLine($"  GetSymbolsAsync       → {symbols.Count} symbols (perp + spot)");

Console.WriteLine();

// ─── 2. Pre-flight order validation (new in 1.0-rc.1) ────────────────────────
//
// Invalid orders are rejected client-side with a precise message BEFORE the
// network round-trip. No credentials needed to demonstrate — these throw on
// the validator path, not on the wire.

Console.WriteLine("─── 2. Pre-flight validation ──────────────────────────────────");

// Below $10 notional → InvalidOrderException with a precise message.
try
{
    await ex.Orders.PlaceLimitAsync("BTC", OrderSide.Buy, price: 1m, size: 1m, tif: TimeInForce.Alo, ct: ct);
}
catch (InvalidOrderException iex)
{
    Console.WriteLine($"  [expected] $1 notional rejected before send:\n    {iex.Message}");
}

// Too many price decimals for BTC (perp, szDecimals=5 → max 1 fractional digit).
try
{
    await ex.Orders.PlaceLimitAsync("BTC", OrderSide.Buy, price: 60_000.123m, size: 0.01m, tif: TimeInForce.Alo, ct: ct);
}
catch (InvalidOrderException iex)
{
    Console.WriteLine($"  [expected] 60000.123 rejected for BTC:\n    {iex.Message}");
}

Console.WriteLine();

// ─── 3. Write — uncomment after setting Credentials ──────────────────────────
//
// Console.WriteLine("─── 3. Write (signed) ──────────────────────────────────────────");
//
// var placed = await ex.Orders.PlaceLimitAsync(
//     symbol: "BTC", side: OrderSide.Buy,
//     price:  Math.Floor(mids["BTC"] * 0.9m),   // safe distance below mid
//     size:   0.001m,
//     tif:    TimeInForce.Alo, ct: ct);
// Console.WriteLine($"  PlaceLimitAsync       → orderId {placed.OrderId}, status {placed.Status}");
//
// var cancelled = await ex.Orders.CancelAsync("BTC", placed.OrderId, ct);
// Console.WriteLine($"  CancelAsync           → success {cancelled.Success}");

// ─── 4. Stream — uncomment after setting Credentials ─────────────────────────
//
// Console.WriteLine("─── 4. Streams (WebSocket) ─────────────────────────────────────");
// Console.WriteLine("  Press Ctrl+C to stop.");
//
// using var streamCts = new CancellationTokenSource();
// Console.CancelKeyPress += (_, e) => { e.Cancel = true; streamCts.Cancel(); };
//
// // Public stream — no creds needed.
// var tradesTask = Task.Run(async () =>
// {
//     await foreach (var t in ex.Streams.TradesAsync("BTC", streamCts.Token))
//         Console.WriteLine($"  trade BTC {t.Trade.Side} {t.Trade.Size}@{t.Trade.Price}");
// });
//
// // User stream — auto gap-fills missing fills on every reconnect.
// var fillsTask = Task.Run(async () =>
// {
//     await foreach (var f in ex.Streams.MyFillsAsync(streamCts.Token))
//         Console.WriteLine($"  fill  {f.Fill.Symbol} {f.Fill.Side} {f.Fill.Size}@{f.Fill.Price}");
// });
//
// await Task.WhenAll(tradesTask, fillsTask);

Console.WriteLine();
Console.WriteLine("Uncomment sections 3+4 after setting `options.Credentials` to exercise write / stream paths.");
