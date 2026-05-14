using System.Globalization;
using EasyTrading.Abstractions;
using EasyTrading.Abstractions.Models;
using EasyTrading.HyperLiquid.Infrastructure;

namespace EasyTrading.HyperLiquid.Modules;

/// <summary>HyperLiquid implementation of <see cref="ITransfers"/>. All operations are signed actions (user-signed for L1 / bridge transfers, action-signed for sub-account transfers).</summary>
internal sealed class HlTransfers(
    HlExchangeClient exchange,
    HyperLiquidClientOptions options) : ITransfers
{
    /// <summary>Withdraw USDC to an external L1 (bridge) destination. User-signed <c>withdraw3</c> action.</summary>
    public async Task<TransferResult> WithdrawAsync(string destination, decimal amountUsdc, CancellationToken ct = default)
    {
        EnsureCredentials();
        var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var action = new HlMap()
            .Add("type", "withdraw3")
            .Add("destination", destination.ToLowerInvariant())
            .Add("amount", FloatToWire(amountUsdc))
            .Add("time", time);

        var schema = new (string Name, string Type)[]
        {
            ("hyperliquidChain", "string"),
            ("destination",      "string"),
            ("amount",           "string"),
            ("time",             "uint64"),
        };

        await exchange.SendUserAsync(action, "Withdraw", schema, ct).ConfigureAwait(false);
        return new TransferResult(null, true, null);
    }

    /// <summary>Send USDC to another address on the HL core ledger. User-signed <c>usdSend</c> action.</summary>
    public async Task<TransferResult> TransferUsdAsync(string toAddress, decimal amount, CancellationToken ct = default)
    {
        EnsureCredentials();
        var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var action = new HlMap()
            .Add("type", "usdSend")
            .Add("destination", toAddress.ToLowerInvariant())
            .Add("amount", FloatToWire(amount))
            .Add("time", time);

        var schema = new (string Name, string Type)[]
        {
            ("hyperliquidChain", "string"),
            ("destination",      "string"),
            ("amount",           "string"),
            ("time",             "uint64"),
        };

        await exchange.SendUserAsync(action, "UsdSend", schema, ct).ConfigureAwait(false);
        return new TransferResult(null, true, null);
    }

    /// <summary>Send a spot token to another address. User-signed <c>spotSend</c> action.</summary>
    public async Task<TransferResult> TransferTokenAsync(string toAddress, string token, decimal amount, CancellationToken ct = default)
    {
        EnsureCredentials();
        var time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var action = new HlMap()
            .Add("type", "spotSend")
            .Add("destination", toAddress.ToLowerInvariant())
            .Add("token", token)
            .Add("amount", FloatToWire(amount))
            .Add("time", time);

        var schema = new (string Name, string Type)[]
        {
            ("hyperliquidChain", "string"),
            ("destination",      "string"),
            ("token",            "string"),
            ("amount",           "string"),
            ("time",             "uint64"),
        };

        await exchange.SendUserAsync(action, "SpotSend", schema, ct).ConfigureAwait(false);
        return new TransferResult(null, true, null);
    }

    public Task<TransferResult> SpotToPerpAsync(decimal amount, CancellationToken ct = default)
        => UsdClassTransferAsync(amount, toPerp: true, ct);

    public Task<TransferResult> PerpToSpotAsync(decimal amount, CancellationToken ct = default)
        => UsdClassTransferAsync(amount, toPerp: false, ct);

    private async Task<TransferResult> UsdClassTransferAsync(decimal amount, bool toPerp, CancellationToken ct)
    {
        EnsureCredentials();
        var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var action = new HlMap()
            .Add("type", "usdClassTransfer")
            .Add("amount", FloatToWire(amount))
            .Add("toPerp", toPerp)
            .Add("nonce", nonce);

        var schema = new (string Name, string Type)[]
        {
            ("hyperliquidChain", "string"),
            ("amount",           "string"),
            ("toPerp",           "bool"),
            ("nonce",            "uint64"),
        };

        await exchange.SendUserAsync(action, "UsdClassTransfer", schema, ct).ConfigureAwait(false);
        return new TransferResult(null, true, null);
    }

    /// <summary>Transfer USDC to a sub-account. This is an L1 (action-signed) <c>subAccountTransfer</c> action, not user-signed.</summary>
    public async Task<TransferResult> ToSubAccountAsync(string subAccount, decimal amount, CancellationToken ct = default)
    {
        EnsureCredentials();
        var usd = (long)decimal.Round(amount * 1_000_000m, MidpointRounding.ToEven);
        var action = new HlMap()
            .Add("type", "subAccountTransfer")
            .Add("subAccountUser", subAccount.ToLowerInvariant())
            .Add("isDeposit", true)
            .Add("usd", usd);

        await exchange.SendL1Async(action, null, ct).ConfigureAwait(false);
        return new TransferResult(null, true, null);
    }

    private void EnsureCredentials()
    {
        if (options.Credentials is null)
            throw new AuthenticationException(
                "HyperLiquidClientOptions.Credentials are required for transfer actions.");
    }

    private static string FloatToWire(decimal value)
    {
        if (value == 0m) return "0";
        var s = value.ToString("0.########", CultureInfo.InvariantCulture);
        return s == "-0" ? "0" : s;
    }
}
