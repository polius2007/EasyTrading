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

// ── /addresses/{address} — subaccount summary ───────────────────────────────

internal sealed record AddressRaw(
    [property: JsonPropertyName("subaccounts")] IReadOnlyList<SubaccountRaw> Subaccounts,
    [property: JsonPropertyName("totalTradingRewards")] decimal? TotalTradingRewards);

internal sealed record SubaccountRaw(
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("subaccountNumber")] int SubaccountNumber,
    [property: JsonPropertyName("equity")] decimal? Equity,
    [property: JsonPropertyName("freeCollateral")] decimal? FreeCollateral,
    [property: JsonPropertyName("marginEnabled")] bool? MarginEnabled,
    [property: JsonPropertyName("updatedAtHeight")] string? UpdatedAtHeight,
    [property: JsonPropertyName("latestProcessedBlockHeight")] string? LatestProcessedBlockHeight,
    [property: JsonPropertyName("openPerpetualPositions")] Dictionary<string, PerpetualPositionRaw>? OpenPerpetualPositions,
    [property: JsonPropertyName("assetPositions")] Dictionary<string, AssetPositionRaw>? AssetPositions);

// ── /perpetualPositions ─────────────────────────────────────────────────────

internal sealed record PerpetualPositionsRaw(
    [property: JsonPropertyName("positions")] IReadOnlyList<PerpetualPositionRaw> Positions);

internal sealed record PerpetualPositionRaw(
    [property: JsonPropertyName("market")] string Market,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("size")] decimal Size,
    [property: JsonPropertyName("maxSize")] decimal? MaxSize,
    [property: JsonPropertyName("entryPrice")] decimal EntryPrice,
    [property: JsonPropertyName("realizedPnl")] decimal? RealizedPnl,
    [property: JsonPropertyName("unrealizedPnl")] decimal? UnrealizedPnl,
    [property: JsonPropertyName("createdAt")] string? CreatedAt,
    [property: JsonPropertyName("createdAtHeight")] string? CreatedAtHeight,
    [property: JsonPropertyName("closedAt")] string? ClosedAt,
    [property: JsonPropertyName("sumOpen")] decimal? SumOpen,
    [property: JsonPropertyName("sumClose")] decimal? SumClose,
    [property: JsonPropertyName("netFunding")] decimal? NetFunding,
    [property: JsonPropertyName("subaccountNumber")] int? SubaccountNumber);

// ── /assetPositions ─────────────────────────────────────────────────────────

internal sealed record AssetPositionsRaw(
    [property: JsonPropertyName("positions")] IReadOnlyList<AssetPositionRaw> Positions);

internal sealed record AssetPositionRaw(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("size")] decimal Size,
    [property: JsonPropertyName("assetId")] string? AssetId,
    [property: JsonPropertyName("subaccountNumber")] int? SubaccountNumber);

// ── /fills ──────────────────────────────────────────────────────────────────

internal sealed record FillsRaw(
    [property: JsonPropertyName("fills")] IReadOnlyList<UserFillRaw> Fills);

internal sealed record UserFillRaw(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("liquidity")] string? Liquidity,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("market")] string Market,
    [property: JsonPropertyName("marketType")] string? MarketType,
    [property: JsonPropertyName("price")] decimal Price,
    [property: JsonPropertyName("size")] decimal Size,
    [property: JsonPropertyName("fee")] decimal? Fee,
    [property: JsonPropertyName("affiliateRevShare")] decimal? AffiliateRevShare,
    [property: JsonPropertyName("createdAt")] string CreatedAt,
    [property: JsonPropertyName("createdAtHeight")] string? CreatedAtHeight,
    [property: JsonPropertyName("orderId")] string? OrderId,
    [property: JsonPropertyName("clientMetadata")] string? ClientMetadata,
    [property: JsonPropertyName("subaccountNumber")] int? SubaccountNumber);

// ── /orders ─────────────────────────────────────────────────────────────────

internal sealed record OrderRaw(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("subaccountId")] string? SubaccountId,
    [property: JsonPropertyName("clientId")] string? ClientId,
    [property: JsonPropertyName("clobPairId")] string? ClobPairId,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("size")] decimal Size,
    [property: JsonPropertyName("totalFilled")] decimal? TotalFilled,
    [property: JsonPropertyName("price")] decimal? Price,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("reduceOnly")] bool? ReduceOnly,
    [property: JsonPropertyName("orderFlags")] string? OrderFlags,
    [property: JsonPropertyName("goodTilBlock")] string? GoodTilBlock,
    [property: JsonPropertyName("goodTilBlockTime")] string? GoodTilBlockTime,
    [property: JsonPropertyName("createdAtHeight")] string? CreatedAtHeight,
    [property: JsonPropertyName("clientMetadata")] string? ClientMetadata,
    [property: JsonPropertyName("triggerPrice")] decimal? TriggerPrice,
    [property: JsonPropertyName("timeInForce")] string? TimeInForce,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("postOnly")] bool? PostOnly,
    [property: JsonPropertyName("ticker")] string Ticker,
    [property: JsonPropertyName("updatedAt")] string? UpdatedAt,
    [property: JsonPropertyName("updatedAtHeight")] string? UpdatedAtHeight);
