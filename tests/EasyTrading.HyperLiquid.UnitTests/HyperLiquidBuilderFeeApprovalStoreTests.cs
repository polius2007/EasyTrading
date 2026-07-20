using EasyTrading.HyperLiquid;

namespace EasyTrading.HyperLiquid.UnitTests;

public sealed class HyperLiquidBuilderFeeApprovalStoreTests
{
    [Fact]
    public async Task Concurrent_refresh_for_one_key_executes_once()
    {
        using var store = new HyperLiquidBuilderFeeApprovalStore();
        var key = Key();
        var refreshCount = 0;

        var states = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
            store.GetOrRefreshAsync(
                key,
                requiredWireFee: 5,
                async ct =>
                {
                    Interlocked.Increment(ref refreshCount);
                    await Task.Delay(25, ct);
                    return new HyperLiquidBuilderFeeApprovalState(
                        ApprovedWireFee: 5,
                        CheckedAt: DateTimeOffset.UtcNow);
                })));

        Assert.Equal(1, refreshCount);
        Assert.All(states, state => Assert.Equal(5, state.ApprovedWireFee));
    }

    [Fact]
    public async Task Higher_required_fee_refreshes_an_insufficient_cached_state()
    {
        using var store = new HyperLiquidBuilderFeeApprovalStore();
        var key = Key();
        var refreshCount = 0;

        async Task<HyperLiquidBuilderFeeApprovalState> Refresh(
            int approvedWireFee,
            CancellationToken ct)
        {
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref refreshCount);
            return new HyperLiquidBuilderFeeApprovalState(
                approvedWireFee,
                DateTimeOffset.UtcNow);
        }

        await store.GetOrRefreshAsync(
            key,
            requiredWireFee: 5,
            ct => Refresh(5, ct));
        await store.GetOrRefreshAsync(
            key,
            requiredWireFee: 4,
            ct => Refresh(4, ct));
        var increased = await store.GetOrRefreshAsync(
            key,
            requiredWireFee: 6,
            ct => Refresh(6, ct));

        Assert.Equal(2, refreshCount);
        Assert.Equal(6, increased.ApprovedWireFee);
    }

    [Fact]
    public async Task Retry_cooldown_suppresses_repeated_refresh()
    {
        using var store = new HyperLiquidBuilderFeeApprovalStore();
        var key = Key();
        var refreshCount = 0;

        Task<HyperLiquidBuilderFeeApprovalState> Refresh(CancellationToken _)
        {
            Interlocked.Increment(ref refreshCount);
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new HyperLiquidBuilderFeeApprovalState(
                ApprovedWireFee: 0,
                CheckedAt: now,
                RetryAfter: now.AddMinutes(30)));
        }

        await store.GetOrRefreshAsync(key, requiredWireFee: 5, Refresh);
        var cached = await store.GetOrRefreshAsync(
            key,
            requiredWireFee: 5,
            _ => throw new InvalidOperationException("Refresh must be suppressed by cooldown."));

        Assert.Equal(1, refreshCount);
        Assert.Equal(0, cached.ApprovedWireFee);
        Assert.NotNull(cached.RetryAfter);
    }

    [Fact]
    public async Task Network_user_and_builder_are_isolated()
    {
        using var store = new HyperLiquidBuilderFeeApprovalStore();
        var refreshCount = 0;
        var keys = new[]
        {
            Key(),
            Key() with { Network = HyperLiquidNetwork.Testnet },
            Key() with { MasterAddress = "0x2222222222222222222222222222222222222222" },
            Key() with { BuilderAddress = "0x0000000000000000000000000000000000000002" },
        };

        foreach (var key in keys)
        {
            await store.GetOrRefreshAsync(
                key,
                requiredWireFee: 5,
                _ =>
                {
                    Interlocked.Increment(ref refreshCount);
                    return Task.FromResult(new HyperLiquidBuilderFeeApprovalState(
                        ApprovedWireFee: 5,
                        CheckedAt: DateTimeOffset.UtcNow));
                });
        }

        Assert.Equal(keys.Length, refreshCount);
    }

    private static HyperLiquidBuilderFeeApprovalKey Key()
        => new(
            HyperLiquidNetwork.Mainnet,
            new Uri("https://api.hyperliquid.xyz"),
            "0x1111111111111111111111111111111111111111",
            "0x0000000000000000000000000000000000000001");
}
