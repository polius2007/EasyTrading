using System.Text.Json.Serialization;

namespace EasyTrading.HyperLiquid.Infrastructure;

// ─── Internal raw DTOs ───────────────────────────────────────────────────────
//
// One file for every Info-endpoint response shape we currently consume. Names
// map 1-to-1 with the HyperLiquid JSON; mapping into the common
// EasyTrading.Abstractions.Models types happens in Mapper.
//
// HyperLiquid encodes numeric values as JSON strings (for precision). With
// JsonNumberHandling.AllowReadingFromString on JsonOptions we can parse them
// straight into `decimal` fields below.

// ── meta (perp universe) ─────────────────────────────────────────────────────

internal sealed record MetaResponseRaw(
    [property: JsonPropertyName("universe")] IReadOnlyList<MetaAssetRaw> Universe);

internal sealed record MetaAssetRaw(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("szDecimals")] int SzDecimals,
    [property: JsonPropertyName("maxLeverage")] int MaxLeverage,
    [property: JsonPropertyName("onlyIsolated")] bool? OnlyIsolated = null,
    [property: JsonPropertyName("isDelisted")] bool? IsDelisted = null,
    [property: JsonPropertyName("marginTableId")] int? MarginTableId = null);

// ── asset contexts (markPx, funding, openInterest, midPx) ─────────────────────

internal sealed record AssetCtxRaw(
    [property: JsonPropertyName("dayNtlVlm")] decimal DayNtlVlm,
    [property: JsonPropertyName("funding")] decimal Funding,
    [property: JsonPropertyName("markPx")] decimal MarkPx,
    [property: JsonPropertyName("midPx")] decimal? MidPx,
    [property: JsonPropertyName("openInterest")] decimal OpenInterest,
    [property: JsonPropertyName("oraclePx")] decimal OraclePx,
    [property: JsonPropertyName("premium")] decimal? Premium,
    [property: JsonPropertyName("prevDayPx")] decimal PrevDayPx);

// ── spot meta ────────────────────────────────────────────────────────────────

internal sealed record SpotMetaResponseRaw(
    [property: JsonPropertyName("tokens")] IReadOnlyList<SpotTokenRaw> Tokens,
    [property: JsonPropertyName("universe")] IReadOnlyList<SpotPairRaw> Universe);

internal sealed record SpotTokenRaw(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("szDecimals")] int SzDecimals,
    [property: JsonPropertyName("weiDecimals")] int WeiDecimals,
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("tokenId")] string? TokenId,
    [property: JsonPropertyName("isCanonical")] bool IsCanonical);

internal sealed record SpotPairRaw(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("tokens")] IReadOnlyList<int> Tokens,
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("isCanonical")] bool IsCanonical);

internal sealed record SpotAssetCtxRaw(
    [property: JsonPropertyName("dayNtlVlm")] decimal DayNtlVlm,
    [property: JsonPropertyName("markPx")] decimal MarkPx,
    [property: JsonPropertyName("midPx")] decimal? MidPx,
    [property: JsonPropertyName("prevDayPx")] decimal PrevDayPx);

// ── l2 book ──────────────────────────────────────────────────────────────────

internal sealed record L2BookRaw(
    [property: JsonPropertyName("coin")] string Coin,
    [property: JsonPropertyName("time")] long Time,
    [property: JsonPropertyName("levels")] IReadOnlyList<IReadOnlyList<L2LevelRaw>> Levels);

internal sealed record L2LevelRaw(
    [property: JsonPropertyName("px")] decimal Px,
    [property: JsonPropertyName("sz")] decimal Sz,
    [property: JsonPropertyName("n")] int N);

// ── candles ──────────────────────────────────────────────────────────────────

internal sealed record CandleRaw(
    [property: JsonPropertyName("t")] long OpenTime,
    [property: JsonPropertyName("T")] long CloseTime,
    [property: JsonPropertyName("s")] string Symbol,
    [property: JsonPropertyName("i")] string Interval,
    [property: JsonPropertyName("o")] decimal Open,
    [property: JsonPropertyName("h")] decimal High,
    [property: JsonPropertyName("l")] decimal Low,
    [property: JsonPropertyName("c")] decimal Close,
    [property: JsonPropertyName("v")] decimal Volume,
    [property: JsonPropertyName("n")] int N);

// ── clearinghouseState (perp account) ────────────────────────────────────────

internal sealed record ClearinghouseStateRaw(
    [property: JsonPropertyName("assetPositions")] IReadOnlyList<AssetPositionWrapperRaw> AssetPositions,
    [property: JsonPropertyName("crossMaintenanceMarginUsed")] decimal CrossMaintenanceMarginUsed,
    [property: JsonPropertyName("crossMarginSummary")] MarginSummaryRaw CrossMarginSummary,
    [property: JsonPropertyName("marginSummary")] MarginSummaryRaw MarginSummary,
    [property: JsonPropertyName("time")] long Time,
    [property: JsonPropertyName("withdrawable")] decimal Withdrawable);

internal sealed record MarginSummaryRaw(
    [property: JsonPropertyName("accountValue")] decimal AccountValue,
    [property: JsonPropertyName("totalMarginUsed")] decimal TotalMarginUsed,
    [property: JsonPropertyName("totalNtlPos")] decimal TotalNtlPos,
    [property: JsonPropertyName("totalRawUsd")] decimal TotalRawUsd);

internal sealed record AssetPositionWrapperRaw(
    [property: JsonPropertyName("position")] PositionRaw Position,
    [property: JsonPropertyName("type")] string Type);

internal sealed record PositionRaw(
    [property: JsonPropertyName("coin")] string Coin,
    [property: JsonPropertyName("szi")] decimal Szi,
    [property: JsonPropertyName("entryPx")] decimal? EntryPx,
    [property: JsonPropertyName("positionValue")] decimal PositionValue,
    [property: JsonPropertyName("unrealizedPnl")] decimal UnrealizedPnl,
    [property: JsonPropertyName("returnOnEquity")] decimal ReturnOnEquity,
    [property: JsonPropertyName("liquidationPx")] decimal? LiquidationPx,
    [property: JsonPropertyName("marginUsed")] decimal MarginUsed,
    [property: JsonPropertyName("maxLeverage")] int MaxLeverage,
    [property: JsonPropertyName("leverage")] LeverageRaw Leverage,
    [property: JsonPropertyName("cumFunding")] CumFundingRaw? CumFunding);

internal sealed record LeverageRaw(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("value")] int Value,
    [property: JsonPropertyName("rawUsd")] decimal? RawUsd);

internal sealed record CumFundingRaw(
    [property: JsonPropertyName("allTime")] decimal AllTime,
    [property: JsonPropertyName("sinceChange")] decimal SinceChange,
    [property: JsonPropertyName("sinceOpen")] decimal SinceOpen);

// ── spotClearinghouseState ───────────────────────────────────────────────────

internal sealed record SpotClearinghouseStateRaw(
    [property: JsonPropertyName("balances")] IReadOnlyList<SpotBalanceRaw> Balances);

internal sealed record SpotBalanceRaw(
    [property: JsonPropertyName("coin")] string Coin,
    [property: JsonPropertyName("token")] int Token,
    [property: JsonPropertyName("total")] decimal Total,
    [property: JsonPropertyName("hold")] decimal Hold,
    [property: JsonPropertyName("entryNtl")] decimal EntryNtl);

// ── orders ───────────────────────────────────────────────────────────────────

internal sealed record OpenOrderRaw(
    [property: JsonPropertyName("coin")] string Coin,
    [property: JsonPropertyName("limitPx")] decimal LimitPx,
    [property: JsonPropertyName("oid")] long Oid,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("sz")] decimal Sz,
    [property: JsonPropertyName("timestamp")] long Timestamp,
    [property: JsonPropertyName("cloid")] string? Cloid = null,
    [property: JsonPropertyName("origSz")] decimal? OrigSz = null,
    [property: JsonPropertyName("orderType")] string? OrderType = null,
    [property: JsonPropertyName("reduceOnly")] bool? ReduceOnly = null,
    [property: JsonPropertyName("tif")] string? Tif = null,
    [property: JsonPropertyName("isPositionTpsl")] bool? IsPositionTpsl = null,
    [property: JsonPropertyName("isTrigger")] bool? IsTrigger = null,
    [property: JsonPropertyName("triggerCondition")] string? TriggerCondition = null,
    [property: JsonPropertyName("triggerPx")] decimal? TriggerPx = null);

internal sealed record OrderStatusResponseRaw(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("order")] OrderStatusOrderWrapperRaw? Order);

internal sealed record OrderStatusOrderWrapperRaw(
    [property: JsonPropertyName("order")] OpenOrderRaw Order,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("statusTimestamp")] long StatusTimestamp);

internal sealed record HistoricalOrderRaw(
    [property: JsonPropertyName("order")] OpenOrderRaw Order,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("statusTimestamp")] long StatusTimestamp);

// ── fills ────────────────────────────────────────────────────────────────────

internal sealed record UserFillRaw(
    [property: JsonPropertyName("coin")] string Coin,
    [property: JsonPropertyName("px")] decimal Px,
    [property: JsonPropertyName("sz")] decimal Sz,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("time")] long Time,
    [property: JsonPropertyName("startPosition")] decimal StartPosition,
    [property: JsonPropertyName("dir")] string Dir,
    [property: JsonPropertyName("closedPnl")] decimal ClosedPnl,
    [property: JsonPropertyName("hash")] string Hash,
    [property: JsonPropertyName("oid")] long Oid,
    [property: JsonPropertyName("crossed")] bool Crossed,
    [property: JsonPropertyName("fee")] decimal Fee,
    [property: JsonPropertyName("tid")] long Tid,
    [property: JsonPropertyName("feeToken")] string FeeToken,
    [property: JsonPropertyName("cloid")] string? Cloid = null);

internal sealed record TwapSliceFillRaw(
    [property: JsonPropertyName("fill")] UserFillRaw Fill,
    [property: JsonPropertyName("twapId")] long TwapId);

// ── funding history ──────────────────────────────────────────────────────────

internal sealed record FundingHistoryEntryRaw(
    [property: JsonPropertyName("coin")] string Coin,
    [property: JsonPropertyName("fundingRate")] decimal FundingRate,
    [property: JsonPropertyName("premium")] decimal Premium,
    [property: JsonPropertyName("time")] long Time);

// ── user fees ────────────────────────────────────────────────────────────────

internal sealed record UserFeesRaw(
    [property: JsonPropertyName("userCrossRate")] decimal UserCrossRate,
    [property: JsonPropertyName("userAddRate")] decimal UserAddRate,
    [property: JsonPropertyName("userSpotCrossRate")] decimal? UserSpotCrossRate = null,
    [property: JsonPropertyName("userSpotAddRate")] decimal? UserSpotAddRate = null,
    [property: JsonPropertyName("activeReferralDiscount")] decimal? ActiveReferralDiscount = null,
    [property: JsonPropertyName("activeStakingDiscount")] StakingDiscountRaw? ActiveStakingDiscount = null);

internal sealed record StakingDiscountRaw(
    [property: JsonPropertyName("discount")] decimal Discount);

// ── userRateLimit ────────────────────────────────────────────────────────────

internal sealed record UserRateLimitRaw(
    [property: JsonPropertyName("cumVlm")] decimal CumVlm,
    [property: JsonPropertyName("nRequestsUsed")] int NRequestsUsed,
    [property: JsonPropertyName("nRequestsCap")] int NRequestsCap);

// ── sub-accounts ─────────────────────────────────────────────────────────────

internal sealed record SubAccountRaw(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("subAccountUser")] string SubAccountUser,
    [property: JsonPropertyName("master")] string Master,
    [property: JsonPropertyName("clearinghouseState")] ClearinghouseStateRaw ClearinghouseState,
    [property: JsonPropertyName("spotState")] SpotClearinghouseStateRaw? SpotState);

// ── vaults ───────────────────────────────────────────────────────────────────

internal sealed record VaultDetailsRaw(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("vaultAddress")] string VaultAddress,
    [property: JsonPropertyName("leader")] string Leader,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("apr")] decimal? Apr = null,
    [property: JsonPropertyName("followers")] IReadOnlyList<VaultFollowerRaw>? Followers = null,
    [property: JsonPropertyName("portfolio")] System.Text.Json.JsonElement? Portfolio = null);

internal sealed record VaultFollowerRaw(
    [property: JsonPropertyName("user")] string User,
    [property: JsonPropertyName("vaultEquity")] decimal VaultEquity,
    [property: JsonPropertyName("pnl")] decimal? Pnl = null,
    [property: JsonPropertyName("allTimePnl")] decimal? AllTimePnl = null,
    [property: JsonPropertyName("daysFollowing")] int? DaysFollowing = null,
    [property: JsonPropertyName("vaultEntryTime")] long? VaultEntryTime = null,
    [property: JsonPropertyName("lockupUntil")] long? LockupUntil = null);

internal sealed record UserVaultEquityRaw(
    [property: JsonPropertyName("vaultAddress")] string VaultAddress,
    [property: JsonPropertyName("equity")] decimal Equity,
    [property: JsonPropertyName("lockedUntilTimestamp")] long? LockedUntilTimestamp = null);

// ── staking ──────────────────────────────────────────────────────────────────

internal sealed record DelegationRaw(
    [property: JsonPropertyName("validator")] string Validator,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("lockedUntilTimestamp")] long LockedUntilTimestamp);

internal sealed record DelegatorSummaryRaw(
    [property: JsonPropertyName("delegated")] decimal Delegated,
    [property: JsonPropertyName("undelegated")] decimal Undelegated,
    [property: JsonPropertyName("totalPendingWithdrawal")] decimal TotalPendingWithdrawal,
    [property: JsonPropertyName("nPendingWithdrawals")] int NPendingWithdrawals);

internal sealed record DelegatorRewardRaw(
    [property: JsonPropertyName("time")] long Time,
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("totalAmount")] decimal TotalAmount);
