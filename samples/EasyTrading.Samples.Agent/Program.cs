using EasyTrading.Abstractions;
using EasyTrading.HyperLiquid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Read-only, public testnet data. This sample never places orders or approves fees.
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
