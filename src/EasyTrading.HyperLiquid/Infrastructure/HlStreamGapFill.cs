using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace EasyTrading.HyperLiquid.Infrastructure;

/// <summary>
/// Wraps a live WebSocket stream with a REST-based gap-recovery layer. When the underlying
/// <see cref="HlWebSocketClient"/> reconnects, we may have missed events that fired between
/// the disconnect and the resubscribe — for user-scoped streams (fills, orders, fundings)
/// those gaps matter because the events are state-changing and not idempotent.
/// </summary>
/// <remarks>
/// <para>The recovery loop:</para>
/// <list type="number">
///   <item><description>While reading the live stream, track the maximum timestamp seen
///       and keep a sliding window of recent unique IDs.</description></item>
///   <item><description>On every <see cref="HlWebSocketClient.Reconnected"/> event, call
///       <c>fetchSince(lastSeenTimestamp - GraceMillis)</c> via REST, dedupe with the recent-ID
///       window, and push the missing items through the same output channel the live stream
///       writes to.</description></item>
///   <item><description>The consumer reads the merged stream as a single <see cref="IAsyncEnumerable{T}"/>
///       — gap recovery is invisible at the API surface.</description></item>
/// </list>
/// <para>The 5-second grace window prevents missing items whose timestamps tie the last seen
/// one (HL timestamps are millisecond-resolution but server-side ordering isn't strict).</para>
/// </remarks>
internal static class HlStreamGapFill
{
    /// <summary>Millisecond window subtracted from <c>lastSeenTimestamp</c> when re-querying REST,
    /// to tolerate clock skew and timestamp ties.</summary>
    public const long GraceMillis = 5_000;

    /// <summary>Default size of the recent-ID dedup window. Plenty for the bursts we observe.</summary>
    public const int DefaultIdHistory = 1024;

    /// <summary>
    /// Merge a live WS stream with REST catch-up on every reconnect, deduplicated by the
    /// supplied ID selector.
    /// </summary>
    /// <typeparam name="T">Update type.</typeparam>
    /// <param name="live">The live WS stream to forward verbatim.</param>
    /// <param name="ws">WS client whose <c>Reconnected</c> event triggers catch-up.</param>
    /// <param name="fetchSince">REST callback: given a start timestamp (ms since epoch), return
    /// every event since then. Called on every reconnect.</param>
    /// <param name="getId">Extracts a stable unique ID per update (tid, oid, etc.).</param>
    /// <param name="getTimestampMillis">Extracts the event timestamp in ms since epoch.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async IAsyncEnumerable<T> WithRecoveryAsync<T>(
        IAsyncEnumerable<T> live,
        HlWebSocketClient ws,
        Func<long, CancellationToken, Task<IReadOnlyList<T>>> fetchSince,
        Func<T, long> getId,
        Func<T, long> getTimestampMillis,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(ws);
        ArgumentNullException.ThrowIfNull(fetchSince);
        ArgumentNullException.ThrowIfNull(getId);
        ArgumentNullException.ThrowIfNull(getTimestampMillis);

        var seenIds = new BoundedIdSet(DefaultIdHistory);

        // Seed lastSeenMillis to "now" — anything that happened before subscribe is the WS
        // snapshot's responsibility, not ours.
        long lastSeenMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var output = Channel.CreateUnbounded<T>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        async Task RecoveryAsync()
        {
            try
            {
                var since = Math.Max(0, lastSeenMillis - GraceMillis);
                var missed = await fetchSince(since, ct).ConfigureAwait(false);
                foreach (var item in missed)
                {
                    if (!seenIds.TryAdd(getId(item))) continue;
                    output.Writer.TryWrite(item);
                }
            }
            catch (OperationCanceledException) { /* shutdown */ }
            catch
            {
                // Best-effort: a failed recovery attempt is preferable to crashing the live stream.
                // The next reconnect will try again.
            }
        }

        void OnReconnected() => _ = Task.Run(RecoveryAsync, ct);
        ws.Reconnected += OnReconnected;

        // Pump the live stream into the output channel on a background task; the consumer reads
        // the merged channel below. This lets recovery events interleave with live events.
        var pumpTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var item in live.WithCancellation(ct).ConfigureAwait(false))
                {
                    if (!seenIds.TryAdd(getId(item))) continue;
                    var ts = getTimestampMillis(item);
                    if (ts > lastSeenMillis) lastSeenMillis = ts;
                    output.Writer.TryWrite(item);
                }
            }
            catch (OperationCanceledException) { /* shutdown */ }
            finally
            {
                output.Writer.TryComplete();
            }
        }, ct);

        try
        {
            await foreach (var item in output.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                yield return item;
        }
        finally
        {
            ws.Reconnected -= OnReconnected;
            output.Writer.TryComplete();
            try { await pumpTask.ConfigureAwait(false); } catch { /* shutdown */ }
        }
    }
}

/// <summary>
/// Fixed-capacity hash-set for ID dedup. Once full, the oldest entries are evicted in insertion
/// order. Not thread-safe — gap-fill callbacks serialise via the output channel.
/// </summary>
internal sealed class BoundedIdSet(int capacity)
{
    private readonly HashSet<long> _set = new(capacity);
    private readonly Queue<long> _order = new(capacity);
    private readonly int _capacity = capacity;

    /// <summary>Add an ID. Returns false if it was already present.</summary>
    public bool TryAdd(long id)
    {
        if (!_set.Add(id))
            return false;

        _order.Enqueue(id);
        if (_set.Count > _capacity)
        {
            var evicted = _order.Dequeue();
            _set.Remove(evicted);
        }
        return true;
    }
}
