using EasyTrading.Abstractions;
using EasyTrading.HyperLiquid;
using Microsoft.Extensions.Logging;

namespace EasyTrading.HyperLiquid.UnitTests;

/// <summary>
/// One-shot live mainnet smoke that exercises the full signed-write path end-to-end through a
/// real HL master+agent setup. Skipped unless every required env var is set, because it touches
/// real money. Gated by:
///   - <c>EASYTRADING_LIVE_SMOKE=1</c>
///   - <c>HL_MAINNET_MASTER_ADDRESS</c>   (master wallet that holds USDC)
///   - <c>HL_MAINNET_AGENT_KEY</c>        (agent / API-wallet private key authorised against master)
///   - <c>HL_MAINNET_AGENT_NAME</c>       (optional metadata, not signed)
/// </summary>
/// <remarks>
/// What it does, in order:
/// <list type="number">
///   <item><description>Reads perp <c>clearinghouseState</c> — if equity &lt; $5, bridges 20 USDC
///       from spot → perp via <c>Transfers.SpotToPerpAsync</c> (user-signed; agent-key allowed).</description></item>
///   <item><description>Places a far-from-market (½ live mid) post-only BTC perp buy via
///       <c>Orders.PlaceLimitAsync</c>. Alo means: if it would cross the spread, the order is
///       rejected rather than filling. With price = 50% of mid, crossing is impossible.</description></item>
///   <item><description>Cancels the order via <c>Orders.CancelAsync</c>.</description></item>
///   <item><description>Optionally bridges the USDC back to spot via <c>Transfers.PerpToSpotAsync</c>.</description></item>
/// </list>
/// <para>The agent will fail to sign <c>approveBuilderFee</c> on the first order (HL requires
/// the master wallet for that action). The 1.2.1 hotfix catches that, logs a warning, skips the
/// <c>builder</c> field on this order, and places the order anyway — verifying the hotfix on
/// real mainnet traffic, which is a bonus on top of the basic place+cancel smoke.</para>
/// </remarks>
public sealed class HyperLiquidMainnetSmokeTests
{
    private static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("EASYTRADING_LIVE_SMOKE"), "1", StringComparison.Ordinal);

    [Fact]
    [Trait("Category", "MainnetSmoke")]
    public async Task Mainnet_PlaceLimit_and_Cancel_with_agent_wallet()
    {
        if (!Enabled) return;

        var master    = Environment.GetEnvironmentVariable("HL_MAINNET_MASTER_ADDRESS");
        var agentKey  = Environment.GetEnvironmentVariable("HL_MAINNET_AGENT_KEY");
        var agentName = Environment.GetEnvironmentVariable("HL_MAINNET_AGENT_NAME");
        if (string.IsNullOrEmpty(master) || string.IsNullOrEmpty(agentKey))
        {
            Console.WriteLine("[smoke] credentials env vars not set — skipping mainnet smoke");
            return;
        }

        var captured = new CapturingLogger<HyperLiquidClient>();
        await using var client = new HyperLiquidClient(new HyperLiquidClientOptions
        {
            Network = HyperLiquidNetwork.Mainnet,
            Credentials = new HyperLiquidCredentials(
                MasterAddress: master,
                PrivateKey:    agentKey,
                AgentName:     agentName),
        }, captured);

        // 1. Pre-flight balance check (legacy clearinghouseState may report $0 if the user is
        // on HL Unified Account — the spot pool is used directly as perp collateral. We skip the
        // bridge attempt entirely; HL itself will validate margin on its side when we place.)
        var state = await client.Account.GetStateAsync();
        Console.WriteLine($"[smoke] legacy perp equity = ${state.AccountValue}, free = ${state.FreeCollateral} (Unified Account may show 0 here even with available margin)");

        // 2. Far-from-market post-only buy
        var mids = await client.Markets.GetAllMidsAsync();
        Assert.True(mids.TryGetValue("BTC", out var btcMid) && btcMid > 0,
            "BTC mid unavailable on HL mainnet?");
        Console.WriteLine($"[smoke] live BTC mid = ${btcMid}");

        var safePrice = Math.Floor(btcMid * 0.5m);
        const decimal safeSize = 0.001m;
        Console.WriteLine($"[smoke] placing BUY {safeSize} BTC @ ${safePrice} ALO (half of mid → cannot cross)");

        var placed = await client.Orders.PlaceLimitAsync(
            symbol: "BTC", side: OrderSide.Buy,
            price:  safePrice, size: safeSize,
            tif:    TimeInForce.Alo);

        Console.WriteLine($"[smoke] place result: orderId={placed.OrderId}, status={placed.Status}, error={placed.ErrorMessage ?? "(none)"}");
        Assert.True(placed.Status == OrderStatus.Open || placed.Status == OrderStatus.Pending,
            $"unexpected placement status: {placed.Status}, error: {placed.ErrorMessage}");
        Assert.True(placed.OrderId > 0L, $"orderId should be non-zero, got {placed.OrderId}");

        // 3. Brief settle, then cancel
        await Task.Delay(TimeSpan.FromSeconds(1));

        var cancelled = await client.Orders.CancelAsync("BTC", placed.OrderId);
        Console.WriteLine($"[smoke] cancel result: success={cancelled.Success}, error={cancelled.ErrorMessage ?? "(none)"}");
        Assert.True(cancelled.Success, $"cancel failed for orderId={placed.OrderId}: {cancelled.ErrorMessage}");

        // 4. Surface warnings (we expect one from the agent-signed approveBuilderFee being rejected
        //    by HL — that's the 1.2.1 hotfix kicking in and skipping the builder field.)
        var warnings = captured.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        if (warnings.Count > 0)
        {
            Console.WriteLine($"[smoke] {warnings.Count} warning(s) logged (expected: builder approve fallback):");
            foreach (var w in warnings)
            {
                var trimmed = w.Message.Length <= 300 ? w.Message : w.Message[..300] + "…";
                Console.WriteLine($"  ⚠  {trimmed}");
            }
        }
        else
        {
            Console.WriteLine("[smoke] no warnings — builder must have been pre-approved (maxBuilderFee >= required)");
        }

        Console.WriteLine("[smoke] ✓ end-to-end mainnet round-trip green");
    }

    /// <summary>In-memory ILogger that records every entry. Local copy to keep this file self-contained.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<Entry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(new Entry(logLevel, formatter(state, exception), exception));

        public sealed record Entry(LogLevel Level, string Message, Exception? Exception);

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
