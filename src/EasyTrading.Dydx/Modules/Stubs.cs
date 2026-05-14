using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;

namespace EasyTrading.Dydx.Modules;

// ─── Phase pointers ─────────────────────────────────────────────────────────

internal static class Phase
{
    public const string Read       = "Pending Phase 7.1 — dYdX signed Indexer reads."; // most reads now live; remains as a marker for any future-deferred read.
    public const string Write      = "Pending Phase 7.2 — dYdX Cosmos SDK transaction signing + validator gRPC broadcast.";
    public const string UserStream = "Pending Phase 7.2 — dYdX user WebSocket channels require a signed subaccount subscription.";
}

// ─── Transfers — writes only, pending Phase 7.2 ─────────────────────────────

internal sealed class Transfers : ITransfers
{
    public Task<TransferResult> WithdrawAsync(string destinationAddress, decimal amount, CancellationToken ct = default) => throw new NotImplementedException(Phase.Write);
    public Task<TransferResult> TransferUsdAsync(string destinationAddress, decimal amount, CancellationToken ct = default) => throw new NotImplementedException(Phase.Write);
    public Task<TransferResult> TransferTokenAsync(string destinationAddress, string token, decimal amount, CancellationToken ct = default) => throw new NotImplementedException(Phase.Write);
    public Task<TransferResult> SpotToPerpAsync(decimal amount, CancellationToken ct = default) => throw new NotSupportedException("dYdX v4 has no separate spot account — funds live in the trading subaccount.");
    public Task<TransferResult> PerpToSpotAsync(decimal amount, CancellationToken ct = default) => throw new NotSupportedException("dYdX v4 has no separate spot account — funds live in the trading subaccount.");
    public Task<TransferResult> ToSubAccountAsync(string subAccount, decimal amount, CancellationToken ct = default) => throw new NotImplementedException(Phase.Write);
}
