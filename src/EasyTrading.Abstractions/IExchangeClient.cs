namespace EasyTrading.Abstractions;

/// <summary>
/// Top-level cross-DEX trading client. All exchange-specific clients (e.g.
/// <c>HyperLiquidClient</c>) implement this contract and expose the same surface,
/// so a strategy can switch venues by changing registration only.
/// </summary>
/// <remarks>
/// Sub-clients are grouped by entity, not by intent — every order operation lives on
/// <see cref="Orders"/>, every position operation on <see cref="Positions"/>, and so on.
/// Inspect <see cref="Capabilities"/> to check whether an optional feature (TWAP, vaults,
/// builder fees, etc.) is supported by the current venue before calling into it.
/// </remarks>
public interface IExchangeClient : IAsyncDisposable
{
    /// <summary>Canonical exchange identifier — <c>"hyperliquid"</c>, <c>"aster"</c>, <c>"dydx"</c>, etc.</summary>
    string ExchangeId { get; }

    /// <summary>Optional features advertised by this exchange.</summary>
    ExchangeCapabilities Capabilities { get; }

    /// <summary>Public market data — markets, order book, candles, mids, funding, public trades.</summary>
    IMarkets Markets { get; }

    /// <summary>Order operations — place, modify, cancel, batch, TWAP, query open / history.</summary>
    IOrders Orders { get; }

    /// <summary>Position operations — read positions, set leverage, add / reduce margin, close.</summary>
    IPositions Positions { get; }

    /// <summary>Trade history — your fills (by symbol, by order, by time).</summary>
    ITrades Trades { get; }

    /// <summary>Account state — balances, fees, portfolio, sub-accounts, agents, rate limit.</summary>
    IAccount Account { get; }

    /// <summary>Transfers — withdraw, internal transfers, spot ↔ perp, sub-account moves.</summary>
    ITransfers Transfers { get; }

    /// <summary>WebSocket subscriptions (public + user-scoped).</summary>
    IStreams Streams { get; }
}
