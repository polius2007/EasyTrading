using System.Globalization;
using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.Aster.Infrastructure;

namespace EasyTrading.Aster.Modules;

/// <summary>Aster implementation of <see cref="ITransfers"/>.</summary>
internal sealed class Transfers(RestClient rest) : ITransfers
{
    /// <summary>External (on-chain) withdraw to an arbitrary address.</summary>
    public async Task<TransferResult> WithdrawAsync(string destinationAddress, decimal amount, CancellationToken ct = default)
    {
        // Aster's V3 docs route external withdraws through /fapi/v3/asset/wallet/transfer with a
        // destination parameter for cross-chain bridges. The exact param names track Binance's
        // capitalAssetWithdraw — we send the documented shape and surface any error verbatim.
        var p = new Dictionary<string, string>
        {
            ["asset"]   = "USDT",
            ["address"] = destinationAddress,
            ["amount"]  = Fmt(amount),
        };
        try
        {
            var raw = await rest.SendSignedAsync<TransferResultRaw>(HttpMethod.Post, "/fapi/v3/withdraw", p, ct).ConfigureAwait(false);
            return new TransferResult(TransferId: raw.TranId.ToString(CultureInfo.InvariantCulture), Success: raw.Status == "SUCCESS", ErrorMessage: null);
        }
        catch (ExchangeApiException ex)
        {
            return new TransferResult(TransferId: null, Success: false, ErrorMessage: ex.Message);
        }
    }

    public Task<TransferResult> TransferUsdAsync(string destinationAddress, decimal amount, CancellationToken ct = default)
        => WithdrawAsync(destinationAddress, amount, ct);

    public Task<TransferResult> TransferTokenAsync(string destinationAddress, string token, decimal amount, CancellationToken ct = default)
    {
        // Aster's spot ↔ futures lives on the same API; arbitrary token withdrawals aren't part of
        // the futures-API surface in V3. Callers needing them should use Aster's spot endpoints
        // (separate spot API) — not implemented here.
        return Task.FromResult(new TransferResult(
            TransferId: null, Success: false,
            ErrorMessage: $"Cross-token withdraws of '{token}' are not supported through Aster's futures API; use the spot API."));
    }

    public async Task<TransferResult> SpotToPerpAsync(decimal amount, CancellationToken ct = default)
        => await InternalTransferAsync(amount, fromFutures: false, ct).ConfigureAwait(false);

    public async Task<TransferResult> PerpToSpotAsync(decimal amount, CancellationToken ct = default)
        => await InternalTransferAsync(amount, fromFutures: true, ct).ConfigureAwait(false);

    private async Task<TransferResult> InternalTransferAsync(decimal amount, bool fromFutures, CancellationToken ct)
    {
        // /fapi/v3/asset/wallet/transfer: type=1 transfers FROM spot TO futures; type=2 the other way.
        var p = new Dictionary<string, string>
        {
            ["asset"]  = "USDT",
            ["amount"] = Fmt(amount),
            ["type"]   = fromFutures ? "2" : "1",
        };
        try
        {
            var raw = await rest.SendSignedAsync<TransferResultRaw>(HttpMethod.Post, "/fapi/v3/asset/wallet/transfer", p, ct).ConfigureAwait(false);
            return new TransferResult(TransferId: raw.TranId.ToString(CultureInfo.InvariantCulture), Success: raw.Status == "SUCCESS", ErrorMessage: null);
        }
        catch (ExchangeApiException ex)
        {
            return new TransferResult(TransferId: null, Success: false, ErrorMessage: ex.Message);
        }
    }

    public async Task<TransferResult> ToSubAccountAsync(string subAccount, decimal amount, CancellationToken ct = default)
    {
        var p = new Dictionary<string, string>
        {
            ["subAccount"] = subAccount,
            ["asset"]      = "USDT",
            ["amount"]     = Fmt(amount),
        };
        try
        {
            var raw = await rest.SendSignedAsync<TransferResultRaw>(HttpMethod.Post, "/fapi/v3/subAccountTransfer", p, ct).ConfigureAwait(false);
            return new TransferResult(TransferId: raw.TranId.ToString(CultureInfo.InvariantCulture), Success: raw.Status == "SUCCESS", ErrorMessage: null);
        }
        catch (ExchangeApiException ex)
        {
            return new TransferResult(TransferId: null, Success: false, ErrorMessage: ex.Message);
        }
    }

    private static string Fmt(decimal d) => d.ToString("0.##########", CultureInfo.InvariantCulture);
}
