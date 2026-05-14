using EasyTrading.HyperLiquid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// ─── EasyTrading sample ─────────────────────────────────────────────────────
//
// Wire up the HyperLiquid client through DI and exercise a few read-only Info
// endpoints against mainnet. Phase 2 ships the Info endpoint, so the read
// calls below return real data — no credentials required.
//
// To attempt user-state queries (account / positions / orders) set
// `options.Credentials` with your master address + private key (or, better,
// an agent wallet's key). Write operations and WebSocket streams land in
// Phases 3 and 4 respectively.

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddEasyTrading()
    .AddHyperLiquid(options =>
    {
        options.Network = HyperLiquidNetwork.Mainnet;
        // options.Credentials = new HyperLiquidCredentials(
        //     masterAddress: "0xYourMasterAddress",
        //     privateKey:    Environment.GetEnvironmentVariable("HL_PRIVATE_KEY")!,
        //     agentName:     "easy-bot");
    });

using var app = builder.Build();
var exchange = app.Services.GetRequiredService<IHyperLiquidExchange>();

Console.WriteLine($"Connected to {exchange.ExchangeId}");
Console.WriteLine($"Capabilities: {exchange.Capabilities}");
Console.WriteLine();

// ─── Read-only Info calls — these hit live HyperLiquid mainnet ──────────────

Console.WriteLine("─── Live HyperLiquid mainnet ───────────────────────────────────");

var mids = await exchange.Markets.GetAllMidsAsync();
Console.WriteLine($"  Markets.GetAllMidsAsync     → {mids.Count} markets; BTC = {mids["BTC"]}");

var btcBook = await exchange.Markets.GetOrderBookAsync("BTC", depth: 3);
Console.WriteLine($"  Markets.GetOrderBookAsync   → best bid {btcBook.Bids[0].Price}, best ask {btcBook.Asks[0].Price}");

var symbols = await exchange.Markets.GetSymbolsAsync();
Console.WriteLine($"  Markets.GetSymbolsAsync     → {symbols.Count} symbols (perp + spot)");

Console.WriteLine();
Console.WriteLine("Add Credentials to your options to exercise user-state methods (Account / Positions / Orders / …).");
