namespace EasyTrading.Abstractions.Models;

/// <summary>A single open position.</summary>
/// <param name="Symbol">Market symbol.</param>
/// <param name="Size">Signed size — positive for long, negative for short.</param>
/// <param name="EntryPrice">Volume-weighted entry price.</param>
/// <param name="MarkPrice">Current mark price used for PnL.</param>
/// <param name="UnrealizedPnl">Current unrealised PnL in quote asset.</param>
/// <param name="RealizedPnl">Realised PnL accumulated on this position.</param>
/// <param name="Leverage">Effective leverage.</param>
/// <param name="MarginMode">Cross or isolated.</param>
/// <param name="LiquidationPrice">Estimated liquidation price; <c>null</c> when not applicable (e.g. spot or fully reduced).</param>
/// <param name="Margin">Margin allocated to this position.</param>
public sealed record Position(
    string Symbol,
    decimal Size,
    decimal EntryPrice,
    decimal MarkPrice,
    decimal UnrealizedPnl,
    decimal RealizedPnl,
    int Leverage,
    MarginMode MarginMode,
    decimal? LiquidationPrice,
    decimal Margin);
