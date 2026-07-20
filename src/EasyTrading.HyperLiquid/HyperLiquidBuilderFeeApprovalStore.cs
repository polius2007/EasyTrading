using System.Collections.Concurrent;

namespace EasyTrading.HyperLiquid;

/// <summary>
/// In-memory, thread-safe builder-fee approval store with per-key single-flight refresh.
/// </summary>
public sealed class HyperLiquidBuilderFeeApprovalStore :
    IHyperLiquidBuilderFeeApprovalStore,
    IDisposable
{
    private readonly ConcurrentDictionary<NormalizedKey, Entry> _entries = new();

    /// <inheritdoc />
    public async Task<HyperLiquidBuilderFeeApprovalState> GetOrRefreshAsync(
        HyperLiquidBuilderFeeApprovalKey key,
        int requiredWireFee,
        Func<CancellationToken, Task<HyperLiquidBuilderFeeApprovalState>> refresh,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentOutOfRangeException.ThrowIfNegative(requiredWireFee);

        var entry = _entries.GetOrAdd(Normalize(key), static _ => new Entry());
        var now = DateTimeOffset.UtcNow;
        if (IsUsable(entry.State, requiredWireFee, now))
            return entry.State!;

        await entry.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            now = DateTimeOffset.UtcNow;
            if (IsUsable(entry.State, requiredWireFee, now))
                return entry.State!;

            var refreshed = await refresh(ct).ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(refreshed);
            ArgumentOutOfRangeException.ThrowIfNegative(refreshed.ApprovedWireFee);
            entry.Publish(refreshed);
            return refreshed;
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    /// <inheritdoc />
    public bool TryGet(
        HyperLiquidBuilderFeeApprovalKey key,
        out HyperLiquidBuilderFeeApprovalState state)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_entries.TryGetValue(Normalize(key), out var entry)
            && entry.State is { } found)
        {
            state = found;
            return true;
        }

        state = null!;
        return false;
    }

    /// <inheritdoc />
    public void Invalidate(HyperLiquidBuilderFeeApprovalKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_entries.TryGetValue(Normalize(key), out var entry))
            entry.Publish(null);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var entry in _entries.Values)
            entry.Dispose();
    }

    private static bool IsUsable(
        HyperLiquidBuilderFeeApprovalState? state,
        int requiredWireFee,
        DateTimeOffset now)
    {
        if (state is null)
            return false;

        if (state.ApprovedWireFee >= requiredWireFee)
            return true;

        return state.RetryAfter is { } retryAfter && retryAfter > now;
    }

    private static NormalizedKey Normalize(HyperLiquidBuilderFeeApprovalKey key)
    {
        ArgumentNullException.ThrowIfNull(key.RestBaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.MasterAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.BuilderAddress);

        return new NormalizedKey(
            key.Network,
            key.RestBaseUrl.AbsoluteUri.TrimEnd('/').ToLowerInvariant(),
            key.MasterAddress.Trim().ToLowerInvariant(),
            key.BuilderAddress.Trim().ToLowerInvariant());
    }

    private readonly record struct NormalizedKey(
        HyperLiquidNetwork Network,
        string RestBaseUrl,
        string MasterAddress,
        string BuilderAddress);

    private sealed class Entry : IDisposable
    {
        private HyperLiquidBuilderFeeApprovalState? _state;

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public HyperLiquidBuilderFeeApprovalState? State => Volatile.Read(ref _state);

        public void Publish(HyperLiquidBuilderFeeApprovalState? state)
            => Volatile.Write(ref _state, state);

        public void Dispose() => Gate.Dispose();
    }
}
