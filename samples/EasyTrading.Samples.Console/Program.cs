using EasyTrading.Abstractions;
using EasyTrading.HyperLiquid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// ─── EasyTrading sample (Phase 1 scaffolding) ───────────────────────────────
//
// Wire up the HyperLiquid client through DI, then walk through the planned
// API surface. In Phase 1, real exchange calls throw NotImplementedException —
// this sample exists to show that the public surface compiles end-to-end
// today and to document how the library will be used once Phase 2+ lands.

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddEasyTrading()
    .AddHyperLiquid(options =>
    {
        options.Network = HyperLiquidNetwork.Testnet;
        // options.Credentials = new HyperLiquidCredentials(
        //     masterAddress: "0xYourMasterAddress",
        //     privateKey:    Environment.GetEnvironmentVariable("HL_PRIVATE_KEY")!,
        //     agentName:     "easy-bot");
    });

using var app = builder.Build();
var exchange = app.Services.GetRequiredService<IHyperLiquidExchange>();

Console.WriteLine($"Connected to {exchange.ExchangeId} (Phase 1 scaffolding)");
Console.WriteLine($"Capabilities: {exchange.Capabilities}");
Console.WriteLine();
Console.WriteLine("─── Planned API surface ──────────────────────────────────────────");
Console.WriteLine("(every call below currently throws NotImplementedException)");
Console.WriteLine();

await TryAsync("Markets.GetAllMidsAsync()",     () => exchange.Markets.GetAllMidsAsync());
await TryAsync("Markets.GetOrderBookAsync(BTC)", () => exchange.Markets.GetOrderBookAsync("BTC"));
await TryAsync("Account.GetStateAsync()",        () => exchange.Account.GetStateAsync());
await TryAsync("Positions.GetAllAsync()",        () => exchange.Positions.GetAllAsync());
await TryAsync("Orders.GetOpenAsync()",          () => exchange.Orders.GetOpenAsync());
await TryAsync("Vaults.GetMyEquitiesAsync()",    () => exchange.Vaults.GetMyEquitiesAsync());
await TryAsync("Builder.GetApprovedAsync()",     () => exchange.Builder.GetApprovedAsync());

Console.WriteLine();
Console.WriteLine("See README.md and the upcoming docs site for the full roadmap.");

static async Task TryAsync(string label, Func<Task> action)
{
    try
    {
        await action();
        Console.WriteLine($"  ✓ {label} succeeded.");
    }
    catch (NotImplementedException ex)
    {
        Console.WriteLine($"  ⏳ {label} — {ex.Message.Split('.')[0]}.");
    }
}
