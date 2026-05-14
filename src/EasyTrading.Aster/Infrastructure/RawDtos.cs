using System.Text.Json.Serialization;

namespace EasyTrading.Aster.Infrastructure;

// ─── Aster V3 Futures REST — raw DTOs ───────────────────────────────────────
//
// One file per category, lower camelCase fields matched explicitly with
// JsonPropertyName attributes (Aster's JSON is camelCase, no exceptions).
// Numeric values come back as strings to preserve precision — AsterJsonOptions
// turns on NumberHandling.AllowReadingFromString so decimal parses cleanly.

// ── /fapi/v3/exchangeInfo ────────────────────────────────────────────────────

internal sealed record ExchangeInfoRaw(
    [property: JsonPropertyName("timezone")] string Timezone,
    [property: JsonPropertyName("serverTime")] long ServerTime,
    [property: JsonPropertyName("symbols")] IReadOnlyList<ExchangeSymbolRaw> Symbols);

internal sealed record ExchangeSymbolRaw(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("pair")] string? Pair,
    [property: JsonPropertyName("contractType")] string? ContractType,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("baseAsset")] string BaseAsset,
    [property: JsonPropertyName("quoteAsset")] string QuoteAsset,
    [property: JsonPropertyName("pricePrecision")] int PricePrecision,
    [property: JsonPropertyName("quantityPrecision")] int QuantityPrecision,
    [property: JsonPropertyName("baseAssetPrecision")] int? BaseAssetPrecision,
    [property: JsonPropertyName("quotePrecision")] int? QuotePrecision,
    [property: JsonPropertyName("filters")] IReadOnlyList<SymbolFilterRaw>? Filters,
    [property: JsonPropertyName("orderTypes")] IReadOnlyList<string>? OrderTypes,
    [property: JsonPropertyName("timeInForce")] IReadOnlyList<string>? TimeInForce);

internal sealed record SymbolFilterRaw(
    [property: JsonPropertyName("filterType")] string FilterType,
    [property: JsonPropertyName("minPrice")] decimal? MinPrice = null,
    [property: JsonPropertyName("maxPrice")] decimal? MaxPrice = null,
    [property: JsonPropertyName("tickSize")] decimal? TickSize = null,
    [property: JsonPropertyName("minQty")] decimal? MinQty = null,
    [property: JsonPropertyName("maxQty")] decimal? MaxQty = null,
    [property: JsonPropertyName("stepSize")] decimal? StepSize = null,
    [property: JsonPropertyName("limit")] int? Limit = null,
    [property: JsonPropertyName("notional")] decimal? Notional = null,
    [property: JsonPropertyName("multiplierUp")] decimal? MultiplierUp = null,
    [property: JsonPropertyName("multiplierDown")] decimal? MultiplierDown = null);

// ── /fapi/v3/depth ───────────────────────────────────────────────────────────

internal sealed record DepthRaw(
    [property: JsonPropertyName("lastUpdateId")] long LastUpdateId,
    [property: JsonPropertyName("E")] long? EventTime,
    [property: JsonPropertyName("T")] long? TransactionTime,
    [property: JsonPropertyName("bids")] IReadOnlyList<IReadOnlyList<decimal>> Bids,
    [property: JsonPropertyName("asks")] IReadOnlyList<IReadOnlyList<decimal>> Asks);

// ── /fapi/v3/ticker/24hr, /fapi/v3/ticker/price, /fapi/v3/ticker/bookTicker ─

internal sealed record Ticker24hRaw(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("lastPrice")] decimal LastPrice,
    [property: JsonPropertyName("openPrice")] decimal OpenPrice,
    [property: JsonPropertyName("highPrice")] decimal HighPrice,
    [property: JsonPropertyName("lowPrice")] decimal LowPrice,
    [property: JsonPropertyName("volume")] decimal Volume,
    [property: JsonPropertyName("quoteVolume")] decimal QuoteVolume,
    [property: JsonPropertyName("openTime")] long OpenTime,
    [property: JsonPropertyName("closeTime")] long CloseTime,
    [property: JsonPropertyName("priceChange")] decimal? PriceChange,
    [property: JsonPropertyName("priceChangePercent")] decimal? PriceChangePercent);

internal sealed record PriceTickerRaw(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("price")] decimal Price,
    [property: JsonPropertyName("time")] long? Time);

internal sealed record BookTickerRaw(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("bidPrice")] decimal BidPrice,
    [property: JsonPropertyName("bidQty")] decimal BidQty,
    [property: JsonPropertyName("askPrice")] decimal AskPrice,
    [property: JsonPropertyName("askQty")] decimal AskQty,
    [property: JsonPropertyName("time")] long? Time);

// ── /fapi/v3/premiumIndex (mark price + funding) ────────────────────────────

internal sealed record PremiumIndexRaw(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("markPrice")] decimal MarkPrice,
    [property: JsonPropertyName("indexPrice")] decimal IndexPrice,
    [property: JsonPropertyName("estimatedSettlePrice")] decimal? EstimatedSettlePrice,
    [property: JsonPropertyName("lastFundingRate")] decimal LastFundingRate,
    [property: JsonPropertyName("interestRate")] decimal? InterestRate,
    [property: JsonPropertyName("nextFundingTime")] long NextFundingTime,
    [property: JsonPropertyName("time")] long Time);

// ── /fapi/v3/fundingRate (history) ──────────────────────────────────────────

internal sealed record FundingRateEntryRaw(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("fundingRate")] decimal FundingRate,
    [property: JsonPropertyName("fundingTime")] long FundingTime);

// ── /fapi/v3/openInterest ───────────────────────────────────────────────────

internal sealed record OpenInterestRaw(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("openInterest")] decimal OpenInterest,
    [property: JsonPropertyName("time")] long Time);

// ── /fapi/v3/order — single order response (POST/GET/PUT/DELETE) ─────────────

internal sealed record OrderResponseRaw(
    [property: JsonPropertyName("orderId")] long OrderId,
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("clientOrderId")] string? ClientOrderId,
    [property: JsonPropertyName("price")] decimal? Price,
    [property: JsonPropertyName("avgPrice")] decimal? AvgPrice,
    [property: JsonPropertyName("origQty")] decimal OrigQty,
    [property: JsonPropertyName("executedQty")] decimal ExecutedQty,
    [property: JsonPropertyName("cumQuote")] decimal? CumQuote,
    [property: JsonPropertyName("timeInForce")] string? TimeInForce,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("origType")] string? OrigType,
    [property: JsonPropertyName("reduceOnly")] bool? ReduceOnly,
    [property: JsonPropertyName("closePosition")] bool? ClosePosition,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("positionSide")] string? PositionSide,
    [property: JsonPropertyName("stopPrice")] decimal? StopPrice,
    [property: JsonPropertyName("workingType")] string? WorkingType,
    [property: JsonPropertyName("priceProtect")] bool? PriceProtect,
    [property: JsonPropertyName("updateTime")] long? UpdateTime,
    [property: JsonPropertyName("time")] long? Time);

// ── /fapi/v3/userTrades — fills ──────────────────────────────────────────────

internal sealed record UserTradeRaw(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("orderId")] long OrderId,
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("price")] decimal Price,
    [property: JsonPropertyName("qty")] decimal Quantity,
    [property: JsonPropertyName("quoteQty")] decimal? QuoteQty,
    [property: JsonPropertyName("realizedPnl")] decimal? RealizedPnl,
    [property: JsonPropertyName("commission")] decimal? Commission,
    [property: JsonPropertyName("commissionAsset")] string? CommissionAsset,
    [property: JsonPropertyName("time")] long Time,
    [property: JsonPropertyName("buyer")] bool? Buyer,
    [property: JsonPropertyName("maker")] bool? Maker,
    [property: JsonPropertyName("positionSide")] string? PositionSide);

// ── /fapi/v3/account — account snapshot ─────────────────────────────────────

internal sealed record AccountInfoRaw(
    [property: JsonPropertyName("totalWalletBalance")] decimal TotalWalletBalance,
    [property: JsonPropertyName("totalUnrealizedProfit")] decimal TotalUnrealizedProfit,
    [property: JsonPropertyName("totalMarginBalance")] decimal TotalMarginBalance,
    [property: JsonPropertyName("totalInitialMargin")] decimal TotalInitialMargin,
    [property: JsonPropertyName("totalMaintMargin")] decimal TotalMaintMargin,
    [property: JsonPropertyName("availableBalance")] decimal AvailableBalance,
    [property: JsonPropertyName("maxWithdrawAmount")] decimal MaxWithdrawAmount,
    [property: JsonPropertyName("updateTime")] long? UpdateTime,
    [property: JsonPropertyName("assets")] IReadOnlyList<AccountAssetRaw>? Assets,
    [property: JsonPropertyName("positions")] IReadOnlyList<AccountPositionRaw>? Positions);

internal sealed record AccountAssetRaw(
    [property: JsonPropertyName("asset")] string Asset,
    [property: JsonPropertyName("walletBalance")] decimal WalletBalance,
    [property: JsonPropertyName("unrealizedProfit")] decimal? UnrealizedProfit,
    [property: JsonPropertyName("marginBalance")] decimal? MarginBalance,
    [property: JsonPropertyName("maintMargin")] decimal? MaintMargin,
    [property: JsonPropertyName("initialMargin")] decimal? InitialMargin,
    [property: JsonPropertyName("availableBalance")] decimal? AvailableBalance,
    [property: JsonPropertyName("maxWithdrawAmount")] decimal? MaxWithdrawAmount);

internal sealed record AccountPositionRaw(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("positionSide")] string? PositionSide,
    [property: JsonPropertyName("positionAmt")] decimal PositionAmt,
    [property: JsonPropertyName("entryPrice")] decimal EntryPrice,
    [property: JsonPropertyName("unrealizedProfit")] decimal? UnrealizedProfit,
    [property: JsonPropertyName("leverage")] int? Leverage,
    [property: JsonPropertyName("marginType")] string? MarginType,
    [property: JsonPropertyName("isolatedMargin")] decimal? IsolatedMargin,
    [property: JsonPropertyName("liquidationPrice")] decimal? LiquidationPrice,
    [property: JsonPropertyName("markPrice")] decimal? MarkPrice,
    [property: JsonPropertyName("notional")] decimal? Notional,
    [property: JsonPropertyName("isolatedWallet")] decimal? IsolatedWallet,
    [property: JsonPropertyName("updateTime")] long? UpdateTime);

// ── /fapi/v3/positionRisk — position risk view ──────────────────────────────

internal sealed record PositionRiskRaw(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("positionSide")] string? PositionSide,
    [property: JsonPropertyName("positionAmt")] decimal PositionAmt,
    [property: JsonPropertyName("entryPrice")] decimal EntryPrice,
    [property: JsonPropertyName("markPrice")] decimal MarkPrice,
    [property: JsonPropertyName("unRealizedProfit")] decimal UnrealizedProfit,
    [property: JsonPropertyName("liquidationPrice")] decimal LiquidationPrice,
    [property: JsonPropertyName("leverage")] int Leverage,
    [property: JsonPropertyName("marginType")] string MarginType,
    [property: JsonPropertyName("isolatedMargin")] decimal? IsolatedMargin,
    [property: JsonPropertyName("notional")] decimal? Notional,
    [property: JsonPropertyName("isolatedWallet")] decimal? IsolatedWallet,
    [property: JsonPropertyName("updateTime")] long? UpdateTime);

// ── /fapi/v3/listenKey ──────────────────────────────────────────────────────

internal sealed record ListenKeyRaw(
    [property: JsonPropertyName("listenKey")] string ListenKey);

// ── /fapi/v3/asset/wallet/transfer ──────────────────────────────────────────

internal sealed record TransferResultRaw(
    [property: JsonPropertyName("tranId")] long TranId,
    [property: JsonPropertyName("status")] string Status);
