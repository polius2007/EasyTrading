using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;

namespace EasyTrading.HyperLiquid.Modules;

/// <summary>HyperLiquid implementation of <see cref="ITransfers"/>. All operations are writes and land in Phase 3.</summary>
internal sealed class HlTransfers : ITransfers
{
    public Task<TransferResult> WithdrawAsync(string destination, decimal amountUsdc, CancellationToken ct = default) => Fail();
    public Task<TransferResult> TransferUsdAsync(string toAddress, decimal amount, CancellationToken ct = default) => Fail();
    public Task<TransferResult> TransferTokenAsync(string toAddress, string token, decimal amount, CancellationToken ct = default) => Fail();
    public Task<TransferResult> SpotToPerpAsync(decimal amount, CancellationToken ct = default) => Fail();
    public Task<TransferResult> PerpToSpotAsync(decimal amount, CancellationToken ct = default) => Fail();
    public Task<TransferResult> ToSubAccountAsync(string subAccount, decimal amount, CancellationToken ct = default) => Fail();

    private static Task<TransferResult> Fail() =>
        Task.FromException<TransferResult>(new NotImplementedException(HyperLiquidClient.WriteOpPhase3Message));
}
