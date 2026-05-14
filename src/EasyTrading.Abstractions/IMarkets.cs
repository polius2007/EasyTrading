using EasyTrading.Abstractions.Models;

namespace EasyTrading.Abstractions;

/// <summary>Public market data — symbols, order book, candles, mids, funding, public trades.</summary>
public interface IMarkets
{
    /// <summary>List markets supported by the exchange, optionally filtered by kind.</summary>
    /// <param name="kind">Which kinds of market to include. Defaults to <see cref="MarketKind.All"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Markets matching the filter.</returns>
    Task<IReadOnlyList<Symbol>> GetSymbolsAsync(MarketKind kind = MarketKind.All, CancellationToken ct = default);

    /// <summary>Get metadata for a single market.</summary>
    /// <param name="symbol">Canonical symbol (e.g. <c>"BTC"</c> for a HyperLiquid perp).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The market metadata.</returns>
    /// <exception cref="ExchangeApiException">The symbol does not exist.</exception>
    Task<Symbol> GetSymbolAsync(string symbol, CancellationToken ct = default);

    /// <summary>Get a snapshot of the order book.</summary>
    /// <param name="symbol">Market symbol.</param>
    /// <param name="depth">Maximum number of levels per side to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The order book snapshot.</returns>
    Task<OrderBook> GetOrderBookAsync(string symbol, int depth = 20, CancellationToken ct = default);

    /// <summary>Get OHLCV candles.</summary>
    /// <param name="symbol">Market symbol.</param>
    /// <param name="interval">Candle interval.</param>
    /// <param name="from">Start of the time range (inclusive).</param>
    /// <param name="to">End of the time range (exclusive).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Candles in ascending time order.</returns>
    Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, Interval interval, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>Get the current mid price for every market in a single call.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Symbol to mid-price map.</returns>
    Task<IReadOnlyDictionary<string, decimal>> GetAllMidsAsync(CancellationToken ct = default);

    /// <summary>Get the current mid price for one market.</summary>
    /// <param name="symbol">Market symbol.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The mid price.</returns>
    Task<decimal> GetMidAsync(string symbol, CancellationToken ct = default);

    /// <summary>Get the current funding-rate snapshot for a perpetual market.</summary>
    /// <param name="symbol">Market symbol.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The funding snapshot.</returns>
    Task<FundingInfo> GetFundingAsync(string symbol, CancellationToken ct = default);

    /// <summary>Get historical funding records for a perpetual market.</summary>
    /// <param name="symbol">Market symbol.</param>
    /// <param name="from">Start of the time range (inclusive).</param>
    /// <param name="to">End of the time range (exclusive).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Funding records in ascending time order.</returns>
    Task<IReadOnlyList<FundingRecord>> GetFundingHistoryAsync(string symbol, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>Get the most recent public trades for a market.</summary>
    /// <param name="symbol">Market symbol.</param>
    /// <param name="limit">Maximum number of trades to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Trades in descending time order (newest first).</returns>
    Task<IReadOnlyList<PublicTrade>> GetRecentTradesAsync(string symbol, int limit = 100, CancellationToken ct = default);

    /// <summary>Get the current open interest for a perpetual market, in base asset.</summary>
    /// <param name="symbol">Market symbol.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Open interest in base units.</returns>
    Task<decimal> GetOpenInterestAsync(string symbol, CancellationToken ct = default);
}
