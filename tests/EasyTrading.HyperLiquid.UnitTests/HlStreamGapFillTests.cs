using System.Threading.Channels;
using EasyTrading.HyperLiquid;
using EasyTrading.HyperLiquid.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyTrading.HyperLiquid.UnitTests;

/// <summary>
/// Tests for the gap-recovery layer that sits between <see cref="WebSocketClient"/> and the
/// user-scoped streams in <see cref="EasyTrading.HyperLiquid.Modules.Streams"/>. The WS
/// client itself is real; we simulate "live" events by feeding items into a channel and trigger
/// reconnects by raising the <c>Reconnected</c> event manually via reflection-free helpers.
/// </summary>
public sealed class HlStreamGapFillTests
{
    private sealed record Item(long Id, long TimestampMs);

    /// <summary>
    /// We need to fire <c>Reconnected</c> on the real <c>WebSocketClient</c> from tests, but
    /// the event is published only to handlers registered through <c>+=</c>. We construct the
    /// client (which never actually connects in unit tests) and use a small reflection-free
    /// indirection: the test triggers reconnect by directly calling a wrapped helper that
    /// raises the event via a captured delegate.
    /// </summary>
    private static (WebSocketClient Ws, Action TriggerReconnect) MakeWs()
    {
        var ws = new WebSocketClient(
            new HyperLiquidClientOptions { Network = HyperLiquidNetwork.Testnet },
            NullLogger.Instance);

        // Use reflection ONCE to obtain the event's backing field so tests can pulse it.
        var field = typeof(WebSocketClient).GetField("Reconnected", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return (ws, () =>
        {
            var handler = (Action?)field?.GetValue(ws);
            handler?.Invoke();
        });
    }

    [Fact]
    public async Task Live_items_pass_through_in_order()
    {
        var (ws, _) = MakeWs();
        await using (ws)
        {
            var liveChannel = Channel.CreateUnbounded<Item>();
            liveChannel.Writer.TryWrite(new Item(1, 1000));
            liveChannel.Writer.TryWrite(new Item(2, 2000));
            liveChannel.Writer.TryWrite(new Item(3, 3000));
            liveChannel.Writer.Complete();

            var fetchCalls = 0;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            var merged = StreamGapFill.WithRecoveryAsync(
                live: liveChannel.Reader.ReadAllAsync(cts.Token),
                ws: ws,
                fetchSince: (_, _) => { fetchCalls++; return Task.FromResult<IReadOnlyList<Item>>(Array.Empty<Item>()); },
                getId: i => i.Id,
                getTimestampMillis: i => i.TimestampMs,
                ct: cts.Token);

            var collected = new List<Item>();
            await foreach (var item in merged)
                collected.Add(item);

            Assert.Equal(new long[] { 1, 2, 3 }, collected.Select(i => i.Id).ToArray());
            Assert.Equal(0, fetchCalls); // no reconnects → no REST catch-up
        }
    }

    [Fact]
    public async Task Reconnect_triggers_rest_catchup_and_dedupes_against_live()
    {
        var (ws, trigger) = MakeWs();
        await using (ws)
        {
            var liveChannel = Channel.CreateUnbounded<Item>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // REST returns items 5 and 6 — both newer than anything the live stream has seen.
            var fetchCalls = 0;
            var recoveryReady = new TaskCompletionSource();
            var merged = StreamGapFill.WithRecoveryAsync(
                live: liveChannel.Reader.ReadAllAsync(cts.Token),
                ws: ws,
                fetchSince: (_, _) =>
                {
                    fetchCalls++;
                    recoveryReady.TrySetResult();
                    return Task.FromResult<IReadOnlyList<Item>>(new[]
                    {
                        new Item(5, 5000),
                        new Item(6, 6000),
                    });
                },
                getId: i => i.Id,
                getTimestampMillis: i => i.TimestampMs,
                ct: cts.Token);

            // Push one live item, then trigger a reconnect, then push more live items.
            liveChannel.Writer.TryWrite(new Item(1, 1000));

            var collected = new List<Item>();
            var consumer = Task.Run(async () =>
            {
                await foreach (var item in merged.WithCancellation(cts.Token))
                    collected.Add(item);
            }, cts.Token);

            // Wait until the live item has been pumped through, then pulse Reconnected.
            await WaitFor(() => collected.Count >= 1, cts.Token);
            trigger();

            // Wait for the recovery task to actually push items 5 and 6 through the output channel.
            // (recoveryReady fires when fetchSince is INVOKED — recovery still needs to push afterwards.)
            await recoveryReady.Task.WaitAsync(cts.Token);
            await WaitFor(() => collected.Any(i => i.Id == 6), cts.Token);

            // Now push more live items including a duplicate of one REST returned.
            liveChannel.Writer.TryWrite(new Item(5, 5000)); // duplicate — must be deduped
            liveChannel.Writer.TryWrite(new Item(7, 7000));
            liveChannel.Writer.Complete();

            await consumer.WaitAsync(cts.Token);

            // Expected: 1 (live), 5, 6 (recovery), 7 (live). Item 5 from live is deduped.
            Assert.Equal(new long[] { 1, 5, 6, 7 }, collected.Select(i => i.Id).OrderBy(x => x).ToArray());
            Assert.Equal(1, fetchCalls);
        }
    }

    [Fact]
    public async Task Recovery_failure_does_not_crash_the_live_stream()
    {
        var (ws, trigger) = MakeWs();
        await using (ws)
        {
            var liveChannel = Channel.CreateUnbounded<Item>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var attempts = 0;
            var merged = StreamGapFill.WithRecoveryAsync(
                live: liveChannel.Reader.ReadAllAsync(cts.Token),
                ws: ws,
                fetchSince: (_, _) =>
                {
                    attempts++;
                    throw new HttpRequestException("REST temporarily unavailable");
                },
                getId: i => i.Id,
                getTimestampMillis: i => i.TimestampMs,
                ct: cts.Token);

            liveChannel.Writer.TryWrite(new Item(1, 1000));

            var collected = new List<Item>();
            var consumer = Task.Run(async () =>
            {
                await foreach (var item in merged.WithCancellation(cts.Token))
                    collected.Add(item);
            }, cts.Token);

            await WaitFor(() => collected.Count >= 1, cts.Token);
            trigger(); // recovery throws

            // Give the failing recovery task a moment, then push more live items.
            await Task.Delay(50, cts.Token);
            liveChannel.Writer.TryWrite(new Item(2, 2000));
            liveChannel.Writer.Complete();

            await consumer.WaitAsync(cts.Token);

            Assert.Equal(new long[] { 1, 2 }, collected.Select(i => i.Id).ToArray());
            Assert.True(attempts >= 1, "recovery callback was never invoked");
        }
    }

    [Fact]
    public async Task BoundedIdSet_is_thread_safe_under_contention()
    {
        // The pump task (live WS stream) and the recovery task (REST catch-up) can race on
        // TryAdd after a reconnect. Without the internal lock the underlying HashSet corrupts.
        // We hammer the set from 4 threads with overlapping ID ranges and assert no exceptions
        // plus a consistent final-count invariant.
        var set = new BoundedIdSet(capacity: 256);
        const int workers = 4;
        const int perWorker = 5_000;
        var added = 0;

        var tasks = Enumerable.Range(0, workers).Select(w => Task.Run(() =>
        {
            for (var i = 0; i < perWorker; i++)
            {
                // Overlap ranges so workers compete for the same IDs.
                if (set.TryAdd((w * 100) + (i % 1000)))
                    Interlocked.Increment(ref added);
            }
        })).ToArray();

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));

        // The set is bounded — final state must reflect at most the capacity worth of items.
        // (We assert via behaviour: after 4 × 5k operations with high overlap, TryAdd a new ID
        //  should still work and return true without crashing the set.)
        Assert.True(set.TryAdd(long.MaxValue));
        Assert.False(set.TryAdd(long.MaxValue)); // immediately re-add → dup
        Assert.True(added > 0, "expected at least some adds to succeed");
    }

    [Fact]
    public void BoundedIdSet_evicts_oldest_when_full()
    {
        var set = new BoundedIdSet(capacity: 3);
        Assert.True(set.TryAdd(1));    // {1}
        Assert.True(set.TryAdd(2));    // {1, 2}
        Assert.True(set.TryAdd(3));    // {1, 2, 3}
        Assert.False(set.TryAdd(2));   // duplicate

        // Adding a 4th evicts 1 (oldest); re-adding 1 should now succeed.
        Assert.True(set.TryAdd(4));    // {2, 3, 4}
        Assert.True(set.TryAdd(1));    // {3, 4, 1} — evicts 2

        // 3 is still present; 2 has been evicted so it's re-addable.
        Assert.False(set.TryAdd(3));
        Assert.True(set.TryAdd(2));    // {4, 1, 2} — evicts 3
    }

    private static async Task WaitFor(Func<bool> predicate, CancellationToken ct)
    {
        for (var i = 0; i < 100; i++)
        {
            if (predicate()) return;
            await Task.Delay(20, ct).ConfigureAwait(false);
        }
        throw new TimeoutException("Condition not satisfied within 2 seconds.");
    }
}
