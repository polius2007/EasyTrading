using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;

namespace EasyTrading.Dydx.Modules;

// ─── "Not yet wired" messages ───────────────────────────────────────────────
// These cover the remaining gaps after 1.2.0 — the Cosmos SDK signing
// primitives are in place, but the specific Msg* types (withdraw / deposit /
// transfer between subaccounts) need their own protobuf packing wired and an
// integration test against testnet before they're enabled.

internal static class Phase
{
    public const string TransferWrite =
        "Transfer messages (MsgWithdrawFromSubaccount / MsgDepositToSubaccount / "
      + "MsgCreateTransfer) aren't wired into EasyTrading.Dydx yet — use the dYdX UI "
      + "or the Cosmos SDK CLI for funding operations until they ship in a follow-up release.";

    public const string PositionWrite =
        "Per-position writes (SetLeverage / SetMarginMode / AddMargin / ReduceMargin / Close) "
      + "aren't wired into EasyTrading.Dydx yet. dYdX v4 leverage is cross-margin and "
      + "account-wide (no per-symbol setting); margin add/remove + reduce-only close need the "
      + "Cosmos Msg* types packed and are on the follow-up roadmap. To close a position "
      + "manually use Orders.PlaceLimitAsync with reduceOnly=true at a far-from-market price.";
}

// ─── Transfers — Cosmos transfer Msgs not wired yet ─────────────────────────

internal sealed class Transfers : ITransfers
{
    public Task<TransferResult> WithdrawAsync(string destinationAddress, decimal amount, CancellationToken ct = default) => throw new NotImplementedException(Phase.TransferWrite);
    public Task<TransferResult> TransferUsdAsync(string destinationAddress, decimal amount, CancellationToken ct = default) => throw new NotImplementedException(Phase.TransferWrite);
    public Task<TransferResult> TransferTokenAsync(string destinationAddress, string token, decimal amount, CancellationToken ct = default) => throw new NotImplementedException(Phase.TransferWrite);
    public Task<TransferResult> SpotToPerpAsync(decimal amount, CancellationToken ct = default) => throw new NotSupportedException("dYdX v4 has no separate spot account — funds live in the trading subaccount.");
    public Task<TransferResult> PerpToSpotAsync(decimal amount, CancellationToken ct = default) => throw new NotSupportedException("dYdX v4 has no separate spot account — funds live in the trading subaccount.");
    public Task<TransferResult> ToSubAccountAsync(string subAccount, decimal amount, CancellationToken ct = default) => throw new NotImplementedException(Phase.TransferWrite);
}
