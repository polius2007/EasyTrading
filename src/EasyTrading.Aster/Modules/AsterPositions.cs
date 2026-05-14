using System.Globalization;
using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.Aster.Infrastructure;

namespace EasyTrading.Aster.Modules;

/// <summary>Aster implementation of <see cref="IPositions"/>.</summary>
internal sealed class AsterPositions(AsterRestClient rest, AsterMetaCache meta) : IPositions
{
    public async Task<IReadOnlyList<Position>> GetAllAsync(CancellationToken ct = default)
    {
        var raw = await rest.SendSignedAsync<List<PositionRiskRaw>>(HttpMethod.Get, "/fapi/v3/positionRisk", new Dictionary<string, string>(), ct).ConfigureAwait(false);
        return raw.Where(p => p.PositionAmt != 0m).Select(AsterMapper.MapPosition).ToList();
    }

    public async Task<Position?> GetAsync(string symbol, CancellationToken ct = default)
    {
        var p = new Dictionary<string, string> { ["symbol"] = symbol };
        var raw = await rest.SendSignedAsync<List<PositionRiskRaw>>(HttpMethod.Get, "/fapi/v3/positionRisk", p, ct).ConfigureAwait(false);
        var hit = raw.FirstOrDefault(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase) && x.PositionAmt != 0m);
        return hit is null ? null : AsterMapper.MapPosition(hit);
    }

    public async Task SetLeverageAsync(string symbol, int leverage, MarginMode marginMode, CancellationToken ct = default)
    {
        // Aster splits this into two endpoints: leverage value and margin type.
        var p = new Dictionary<string, string>
        {
            ["symbol"]   = symbol,
            ["leverage"] = leverage.ToString(CultureInfo.InvariantCulture),
        };
        await rest.SendSignedRawAsync(HttpMethod.Post, "/fapi/v3/leverage", p, ct).ConfigureAwait(false);

        await SetMarginModeAsync(symbol, marginMode, ct).ConfigureAwait(false);
    }

    public async Task SetMarginModeAsync(string symbol, MarginMode marginMode, CancellationToken ct = default)
    {
        var p = new Dictionary<string, string>
        {
            ["symbol"]     = symbol,
            ["marginType"] = marginMode == MarginMode.Isolated ? "ISOLATED" : "CROSSED",
        };
        // Aster returns error code -4046 if margin type already matches — treat that as success.
        try
        {
            await rest.SendSignedRawAsync(HttpMethod.Post, "/fapi/v3/marginType", p, ct).ConfigureAwait(false);
        }
        catch (ExchangeApiException ex) when (ex.ErrorCode == "-4046")
        {
            // "No need to change margin type." — idempotent success.
        }
    }

    public Task AddMarginAsync(string symbol, decimal amount, CancellationToken ct = default)
        => AdjustMarginAsync(symbol, amount, increase: true, ct);

    public Task ReduceMarginAsync(string symbol, decimal amount, CancellationToken ct = default)
        => AdjustMarginAsync(symbol, amount, increase: false, ct);

    private async Task AdjustMarginAsync(string symbol, decimal amount, bool increase, CancellationToken ct)
    {
        var p = new Dictionary<string, string>
        {
            ["symbol"]    = symbol,
            ["amount"]    = amount.ToString("0.##########", CultureInfo.InvariantCulture),
            // 1 = add, 2 = reduce (per Aster docs).
            ["type"]      = increase ? "1" : "2",
        };
        await rest.SendSignedRawAsync(HttpMethod.Post, "/fapi/v3/positionMargin", p, ct).ConfigureAwait(false);
    }

    public async Task<PlaceOrderResult> CloseAsync(string symbol, CancellationToken ct = default)
    {
        var existing = await GetAsync(symbol, ct).ConfigureAwait(false)
            ?? throw new InvalidOrderException($"Cannot close: no open position on '{symbol}'.");

        // Reduce-only market in the opposite direction of the position.
        var side = existing.Size > 0 ? OrderSide.Sell : OrderSide.Buy;
        var size = Math.Abs(existing.Size);

        var info = await meta.GetAsync(symbol, ct).ConfigureAwait(false);
        AsterOrderValidator.Validate(info, price: 0m, size, reduceOnly: true);

        var p = new Dictionary<string, string>
        {
            ["symbol"]     = symbol,
            ["side"]       = side == OrderSide.Buy ? "BUY" : "SELL",
            ["type"]       = "MARKET",
            ["quantity"]   = size.ToString("0.##########", CultureInfo.InvariantCulture),
            ["reduceOnly"] = "true",
        };
        var resp = await rest.SendSignedAsync<OrderResponseRaw>(HttpMethod.Post, "/fapi/v3/order", p, ct).ConfigureAwait(false);
        return new PlaceOrderResult(
            OrderId:          resp.OrderId,
            ClientOrderId:    resp.ClientOrderId,
            Status:           AsterMapper.ParseStatus(resp.Status),
            FilledSize:       resp.ExecutedQty,
            AverageFillPrice: resp.AvgPrice > 0 ? resp.AvgPrice : null,
            ErrorMessage:     resp.Status == "REJECTED" ? "Aster rejected the close." : null);
    }
}
