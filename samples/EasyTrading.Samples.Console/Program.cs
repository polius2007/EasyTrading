using System.Globalization;
using EasyTrading.Abstractions;
using EasyTrading.Aster;
using EasyTrading.Dydx;
using EasyTrading.HyperLiquid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// ─── EasyTrading sample (1.2.0) ──────────────────────────────────────────────
//
// One process registering three DEX clients side-by-side: HyperLiquid, Aster,
// dYdX v4. Every venue lives behind the same `IExchangeClient`-shaped surface
// (`Markets`, `Orders`, `Positions`, `Trades`, `Account`, `Transfers`, `Streams`),
// so a strategy written against the abstraction works on all three.
//
// Read-only sections hit live mainnet and need no credentials. Write / user-
// stream sections (commented out at the bottom) need `options.Credentials` set.

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddEasyTrading()
    .AddHyperLiquid(o =>
    {
        o.Network = HyperLiquidNetwork.Mainnet;
        o.RetryPolicy = new HyperLiquidRetryOptions { MaxAttempts = 4 };
        // o.Credentials = new HyperLiquidCredentials(
        //     masterAddress: "0xYourMasterAddress",
        //     privateKey:    Environment.GetEnvironmentVariable("HL_AGENT_KEY")!,
        //     agentName:     "easy-bot");
    })
    .AddAster(o =>
    {
        o.Network = AsterNetwork.Mainnet;
        // o.Credentials = new AsterCredentials(
        //     MasterAddress: "0xYourMasterAddress",
        //     SignerAddress: "0xYourSignerAddress",
        //     PrivateKey:    Environment.GetEnvironmentVariable("ASTER_SIGNER_KEY")!);
    })
    .AddDydx(o =>
    {
        o.Network = DydxNetwork.Mainnet;
        // o.Credentials = new DydxCredentials(
        //     Address:  "dydx1…",
        //     Mnemonic: Environment.GetEnvironmentVariable("DYDX_MNEMONIC")!,
        //     SubaccountNumber: 0);
    });

using var app = builder.Build();
var hl    = app.Services.GetRequiredService<IHyperLiquidExchange>();
var aster = app.Services.GetRequiredService<IAsterExchange>();
var dydx  = app.Services.GetRequiredService<IDydxExchange>();
var ct = CancellationToken.None;

await DemoVenueAsync("HyperLiquid",  hl,    "BTC",      ct);
await DemoVenueAsync("Aster",        aster, "BTCUSDT",  ct);
await DemoVenueAsync("dYdX v4",      dydx,  "BTC-USD",  ct);

Console.WriteLine();
Console.WriteLine("── Pre-flight validation (HyperLiquid) ────────────────────────");
await DemoValidationAsync(hl, ct);

Console.WriteLine();
Console.WriteLine("Uncomment credentials + the write/stream blocks below to exercise signed paths.");

// ─── Reusable demo helper — works against any IExchangeClient ────────────────

static async Task DemoVenueAsync(string label, IExchangeClient ex, string sampleSymbol, CancellationToken ct)
{
    Console.WriteLine($"── {label} ({ex.ExchangeId}) ─────────────────────────────────────");
    Console.WriteLine($"   Capabilities: {ex.Capabilities}");

    try
    {
        var mids = await ex.Markets.GetAllMidsAsync(ct);
        Console.WriteLine($"   GetAllMidsAsync       → {mids.Count} markets; {sampleSymbol} = {(mids.TryGetValue(sampleSymbol, out var px) ? px.ToString("0.##", CultureInfo.InvariantCulture) : "<missing>")}");

        var book = await ex.Markets.GetOrderBookAsync(sampleSymbol, depth: 3, ct: ct);
        if (book.Bids.Count > 0 && book.Asks.Count > 0)
            Console.WriteLine($"   GetOrderBookAsync     → best bid {book.Bids[0].Price}, best ask {book.Asks[0].Price}");

        var symbols = await ex.Markets.GetSymbolsAsync(ct: ct);
        Console.WriteLine($"   GetSymbolsAsync       → {symbols.Count} symbols");
    }
    catch (Exception ex2)
    {
        Console.WriteLine($"   read failed: {ex2.GetType().Name}: {ex2.Message}");
    }
    Console.WriteLine();
}

static async Task DemoValidationAsync(IHyperLiquidExchange hl, CancellationToken ct)
{
    try
    {
        await hl.Orders.PlaceLimitAsync("BTC", OrderSide.Buy, price: 1m, size: 1m, tif: TimeInForce.Alo, ct: ct);
    }
    catch (InvalidOrderException iex)
    {
        Console.WriteLine($"   [expected] $1 notional rejected before send:");
        Console.WriteLine($"     {iex.Message}");
    }

    try
    {
        await hl.Orders.PlaceLimitAsync("BTC", OrderSide.Buy, price: 60_000.123m, size: 0.01m, tif: TimeInForce.Alo, ct: ct);
    }
    catch (InvalidOrderException iex)
    {
        Console.WriteLine($"   [expected] 60000.123 rejected for BTC:");
        Console.WriteLine($"     {iex.Message}");
    }
}

// ─── Write demo (uncomment after setting Credentials) ────────────────────────
//
// Mainnet trade — start with a price well below market so the order rests rather than fills.
//
// var book = await hl.Markets.GetOrderBookAsync("BTC", depth: 1, ct: ct);
// var safePrice = Math.Floor(book.Bids[0].Price * 0.9m);
// var placed = await hl.Orders.PlaceLimitAsync(
//     symbol: "BTC", side: OrderSide.Buy,
//     price:  safePrice, size: 0.001m,
//     tif:    TimeInForce.Alo, ct: ct);
// Console.WriteLine($"   PlaceLimitAsync       → orderId {placed.OrderId}, status {placed.Status}");
// await hl.Orders.CancelAsync("BTC", placed.OrderId, ct);

// ─── Stream demo (uncomment after setting Credentials) ───────────────────────
//
// using var streamCts = new CancellationTokenSource();
// Console.CancelKeyPress += (_, e) => { e.Cancel = true; streamCts.Cancel(); };
//
// await foreach (var f in hl.Streams.MyFillsAsync(streamCts.Token))
//     Console.WriteLine($"   fill  {f.Fill.Symbol} {f.Fill.Side} {f.Fill.Size}@{f.Fill.Price}");
