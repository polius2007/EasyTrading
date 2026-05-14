using System.Text.Json.Serialization;

namespace EasyTrading.Dydx.Infrastructure;

// ─── dYdX v4 Indexer — raw REST DTOs ────────────────────────────────────────
//
// All Indexer numeric values come back as JSON strings to preserve precision; with
// NumberHandling.AllowReadingFromString in JsonOptions decimals parse cleanly.

// ── /perpetualMarkets ────────────────────────────────────────────────────────

internal sealed record PerpetualMarketsRaw(
    [property: JsonPropertyName("markets")] Dictionary<string, PerpetualMarketRaw> Markets);

internal sealed record PerpetualMarketRaw(
    [property: JsonPropertyName("clobPairId")] string ClobPairId,
    [property: JsonPropertyName("ticker")] string Ticker,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("oraclePrice")] decimal? OraclePrice,
    [property: JsonPropertyName("priceChange24H")] decimal? PriceChange24H,
    [property: JsonPropertyName("volume24H")] decimal? Volume24H,
    [property: JsonPropertyName("trades24H")] int? Trades24H,
    [property: JsonPropertyName("nextFundingRate")] decimal? NextFundingRate,
    [property: JsonPropertyName("initialMarginFraction")] decimal? InitialMarginFraction,
    [property: JsonPropertyName("maintenanceMarginFraction")] decimal? MaintenanceMarginFraction,
    [property: JsonPropertyName("openInterest")] decimal? OpenInterest,
    [property: JsonPropertyName("atomicResolution")] int? AtomicResolution,
    [property: JsonPropertyName("quantumConversionExponent")] int? QuantumConversionExponent,
    [property: JsonPropertyName("tickSize")] decimal? TickSize,
    [property: JsonPropertyName("stepSize")] decimal? StepSize,
    [property: JsonPropertyName("stepBaseQuantums")] decimal? StepBaseQuantums,
    [property: JsonPropertyName("subticksPerTick")] int? SubticksPerTick,
    [property: JsonPropertyName("marketType")] string? MarketType);

// ── /orderbooks/perpetualMarket/{ticker} ────────────────────────────────────

internal sealed record OrderbookRaw(
    [property: JsonPropertyName("bids")] IReadOnlyList<OrderbookLevelRaw> Bids,
    [property: JsonPropertyName("asks")] IReadOnlyList<OrderbookLevelRaw> Asks);

internal sealed record OrderbookLevelRaw(
    [property: JsonPropertyName("price")] decimal Price,
    [property: JsonPropertyName("size")] decimal Size);

// ── /trades/perpetualMarket/{ticker} ────────────────────────────────────────

internal sealed record TradesRaw(
    [property: JsonPropertyName("trades")] IReadOnlyList<PublicTradeRaw> Trades);

internal sealed record PublicTradeRaw(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("size")] decimal Size,
    [property: JsonPropertyName("price")] decimal Price,
    [property: JsonPropertyName("createdAt")] string CreatedAt,
    [property: JsonPropertyName("createdAtHeight")] string? CreatedAtHeight,
    [property: JsonPropertyName("type")] string? Type);

// ── /candles/perpetualMarkets/{ticker} ──────────────────────────────────────

internal sealed record CandlesRaw(
    [property: JsonPropertyName("candles")] IReadOnlyList<CandleRaw> Candles);

internal sealed record CandleRaw(
    [property: JsonPropertyName("startedAt")] string StartedAt,
    [property: JsonPropertyName("ticker")] string Ticker,
    [property: JsonPropertyName("resolution")] string Resolution,
    [property: JsonPropertyName("low")] decimal Low,
    [property: JsonPropertyName("high")] decimal High,
    [property: JsonPropertyName("open")] decimal Open,
    [property: JsonPropertyName("close")] decimal Close,
    [property: JsonPropertyName("baseTokenVolume")] decimal BaseTokenVolume,
    [property: JsonPropertyName("usdVolume")] decimal? UsdVolume,
    [property: JsonPropertyName("trades")] int? Trades,
    [property: JsonPropertyName("startingOpenInterest")] decimal? StartingOpenInterest);

// ── /historicalFunding/{ticker} ─────────────────────────────────────────────

internal sealed record HistoricalFundingRaw(
    [property: JsonPropertyName("historicalFunding")] IReadOnlyList<FundingEntryRaw> HistoricalFunding);

internal sealed record FundingEntryRaw(
    [property: JsonPropertyName("ticker")] string Ticker,
    [property: JsonPropertyName("rate")] decimal Rate,
    [property: JsonPropertyName("price")] decimal Price,
    [property: JsonPropertyName("effectiveAt")] string EffectiveAt);
