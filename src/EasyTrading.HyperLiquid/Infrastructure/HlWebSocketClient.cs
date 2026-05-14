using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace EasyTrading.HyperLiquid.Infrastructure;

/// <summary>
/// HyperLiquid WebSocket client. Manages a single shared connection per client instance and
/// multiplexes many subscriptions over it. Each subscriber gets its own
/// <see cref="System.Threading.Channels.Channel{T}"/> so back-pressure is per-subscriber.
/// </summary>
/// <remarks>
/// <para>Wire protocol: <c>{"method":"subscribe","subscription":{...}}</c> for subscribe;
/// <c>{"channel":"...","data":...}</c> for push messages. Each subscription registers with a
/// composite "match key" (channel name plus optional coin/interval/etc.) that the reader uses
/// to dispatch incoming messages.</para>
/// <para>On disconnect: exponential-ish backoff via <see cref="HyperLiquidClientOptions.WebSocketReconnectDelay"/>,
/// then re-subscribe to every active key on the freshly opened socket.</para>
/// </remarks>
internal sealed class HlWebSocketClient : IAsyncDisposable
{
    private readonly Uri _url;
    private readonly TimeSpan _initialReconnectDelay;
    private readonly ILogger _logger;

    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private readonly ConcurrentDictionary<string, IActiveSubscription> _subscriptions = new();

    private ClientWebSocket? _socket;
    private Task? _readerTask;
    private int _disposed;

    /// <summary>
    /// Fires after a reconnect when every active subscription has been re-subscribed on the
    /// fresh socket. Stream-level gap-recovery hooks (e.g. user-fill REST catch-up in
    /// <c>HlStreams</c>) attach here. Handlers run synchronously on the reconnect task — keep
    /// the work tiny and offload anything I/O-bound to <c>Task.Run</c>.
    /// </summary>
    public event Action? Reconnected;

    public HlWebSocketClient(HyperLiquidClientOptions options, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _url = options.GetEffectiveWebSocketUrl();
        _initialReconnectDelay = options.WebSocketReconnectDelay;
        _logger = logger;
    }

    /// <summary>
    /// Subscribe to a channel and stream typed updates until the supplied cancellation token fires.
    /// </summary>
    /// <typeparam name="T">Update type the parser yields per push message.</typeparam>
    /// <param name="subscribePayload">The <c>subscription</c> object to send to HL (e.g. <c>new { type = "trades", coin = "BTC" }</c>).</param>
    /// <param name="subscriptionKey">A unique composite key (e.g. <c>"trades:BTC"</c>) used to route incoming messages.</param>
    /// <param name="parser">Converts the message's <c>data</c> element into zero-or-more typed updates.</param>
    /// <param name="ct">Cancellation token. Unsubscribes when cancelled.</param>
    public async IAsyncEnumerable<T> SubscribeAsync<T>(
        object subscribePayload,
        string subscriptionKey,
        Func<JsonElement, IEnumerable<T>> parser,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subscribePayload);
        ArgumentNullException.ThrowIfNull(subscriptionKey);
        ArgumentNullException.ThrowIfNull(parser);

        var channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        // Register (or fan-in to existing) subscription for this key.
        ActiveSubscription<T> typed;
        bool isFirstWriter;
        var entry = _subscriptions.GetOrAdd(subscriptionKey,
            _ => new ActiveSubscription<T>(subscribePayload, parser));
        if (entry is not ActiveSubscription<T> matchingSub)
            throw new InvalidOperationException(
                $"Subscription key '{subscriptionKey}' is already registered with a different update type "
                + $"({entry.GetType().Name} vs ActiveSubscription<{typeof(T).Name}>).");
        typed = matchingSub;
        isFirstWriter = typed.AddWriter(channel.Writer);

        try
        {
            await EnsureConnectedAsync(ct).ConfigureAwait(false);
            if (isFirstWriter)
                await SendSubscribeAsync(subscribePayload, subscribe: true, ct).ConfigureAwait(false);

            await foreach (var item in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                yield return item;
        }
        finally
        {
            var wasLast = typed.RemoveWriter(channel.Writer);
            if (wasLast)
            {
                _subscriptions.TryRemove(subscriptionKey, out _);
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await SendSubscribeAsync(subscribePayload, subscribe: false, timeout.Token).ConfigureAwait(false);
                }
                catch
                {
                    // shutdown in progress or socket already closed — nothing to undo on the server.
                }
            }
            channel.Writer.TryComplete();
        }
    }

    // ─── connection management ───────────────────────────────────────────────

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_socket is { State: WebSocketState.Open })
            return;

        await _connectLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_socket is { State: WebSocketState.Open })
                return;

            await ConnectAndStartReaderAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task ConnectAndStartReaderAsync(CancellationToken ct)
    {
        // Tear down any previous socket cleanly.
        if (_socket is not null)
        {
            try { _socket.Abort(); } catch { /* best-effort */ }
            _socket.Dispose();
            _socket = null;
        }

        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        await socket.ConnectAsync(_url, ct).ConfigureAwait(false);
        _socket = socket;

        _readerTask = Task.Run(() => ReaderLoopAsync(_shutdown.Token), _shutdown.Token);
    }

    private async Task ReconnectAsync(CancellationToken ct)
    {
        var delay = _initialReconnectDelay;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
                _logger.LogInformation("HyperLiquid WebSocket reconnecting…");

                await _connectLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    await ConnectAndStartReaderAsync(ct).ConfigureAwait(false);
                }
                finally
                {
                    _connectLock.Release();
                }

                // Re-subscribe to every active key on the freshly opened socket.
                foreach (var (_, sub) in _subscriptions)
                {
                    try
                    {
                        await SendSubscribeAsync(sub.SubscribePayload, subscribe: true, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to resubscribe key after reconnect.");
                    }
                }

                _logger.LogInformation("HyperLiquid WebSocket reconnected; {Count} subscriptions resubscribed.", _subscriptions.Count);

                // Notify subscribers — used by user-stream gap-recovery to fetch REST catch-up.
                try
                {
                    Reconnected?.Invoke();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Reconnected event handler threw.");
                }
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "HyperLiquid WebSocket reconnect attempt failed; retrying in {Delay}.", delay);
                // exponential-ish backoff, capped at 30 s
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, TimeSpan.FromSeconds(30).Ticks));
            }
        }
    }

    // ─── outgoing send (subscribe / unsubscribe) ─────────────────────────────

    private async Task SendSubscribeAsync(object subscribePayload, bool subscribe, CancellationToken ct)
    {
        var envelope = new HlMap()
            .Add("method", subscribe ? "subscribe" : "unsubscribe");
        envelope.Add("subscription", subscribePayload);

        var json = JsonSerializer.Serialize(envelope, HlJsonOptions.Default);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var socket = _socket
                ?? throw new InvalidOperationException("WebSocket is not connected.");
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    // ─── reader loop ─────────────────────────────────────────────────────────

    private async Task ReaderLoopAsync(CancellationToken ct)
    {
        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent(64 * 1024);

        try
        {
            while (!ct.IsCancellationRequested && _socket is { State: WebSocketState.Open })
            {
                using var ms = new MemoryStream();
                try
                {
                    while (true)
                    {
                        var result = await _socket.ReceiveAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            _logger.LogInformation("HyperLiquid WebSocket received Close frame.");
                            await TriggerReconnectAsync(ct).ConfigureAwait(false);
                            return;
                        }
                        ms.Write(buffer, 0, result.Count);
                        if (result.EndOfMessage) break;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return; // shutdown
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "HyperLiquid WebSocket receive failed; reconnecting.");
                    await TriggerReconnectAsync(ct).ConfigureAwait(false);
                    return;
                }

                if (ms.Length == 0) continue;

                ms.Position = 0;
                try
                {
                    using var doc = await JsonDocument.ParseAsync(ms, cancellationToken: ct).ConfigureAwait(false);
                    DispatchMessage(doc.RootElement);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse / dispatch HyperLiquid WebSocket message.");
                }
            }
        }
        finally
        {
            pool.Return(buffer);
        }
    }

    private async Task TriggerReconnectAsync(CancellationToken ct)
    {
        // Run reconnect on its own task so the reader can exit cleanly.
        _ = Task.Run(() => ReconnectAsync(ct), ct);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private void DispatchMessage(JsonElement msg)
    {
        if (!msg.TryGetProperty("channel", out var channelEl) || channelEl.ValueKind != JsonValueKind.String)
            return;

        var channel = channelEl.GetString();
        if (channel is null) return;

        // Subscription confirmations / pongs — log at debug, no routing.
        if (channel is "subscriptionResponse" or "pong" or "post")
            return;

        if (!msg.TryGetProperty("data", out var data))
            return;

        var key = ComputeRoutingKey(channel, data);
        if (key is null) return;

        if (_subscriptions.TryGetValue(key, out var sub))
        {
            try
            {
                sub.Dispatch(data);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Subscriber dispatch failed for key '{Key}'.", key);
            }
        }
    }

    /// <summary>
    /// Computes the routing key for an incoming message. Public per-coin channels embed the coin
    /// in their data; user-level channels are keyed by channel name only.
    /// </summary>
    private static string? ComputeRoutingKey(string channel, JsonElement data)
    {
        switch (channel)
        {
            case "trades":
                // data is an array; first element carries the coin.
                if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0) return null;
                var firstTradeCoin = data[0].TryGetProperty("coin", out var c1) ? c1.GetString() : null;
                return firstTradeCoin is null ? null : $"trades:{firstTradeCoin}";

            case "l2Book":
                return data.TryGetProperty("coin", out var c2) ? $"l2Book:{c2.GetString()}" : null;

            case "bbo":
                return data.TryGetProperty("coin", out var c3) ? $"bbo:{c3.GetString()}" : null;

            case "candle":
                // candle data has "s" (symbol) and "i" (interval).
                var sym = data.TryGetProperty("s", out var s) ? s.GetString() : null;
                var iv = data.TryGetProperty("i", out var i) ? i.GetString() : null;
                return (sym is null || iv is null) ? null : $"candle:{sym}:{iv}";

            // User-scoped channels (one user per client → channel name suffices).
            case "allMids":
            case "orderUpdates":
            case "userFills":
            case "userFundings":
            case "notification":
            case "webData2":
                return channel;

            default:
                return channel;
        }
    }

    // ─── dispose ─────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        // DI graphs can resolve us through multiple interfaces (HyperLiquidClient, IExchangeClient,
        // IHyperLiquidExchange) and end up invoking dispose twice. Make it idempotent.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _shutdown.Cancel();

        if (_socket is not null && _socket.State == WebSocketState.Open)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", cts.Token).ConfigureAwait(false);
            }
            catch { /* best-effort */ }
        }

        _socket?.Dispose();

        if (_readerTask is not null)
        {
            try { await _readerTask.ConfigureAwait(false); }
            catch { /* shutdown */ }
        }

        _connectLock.Dispose();
        _sendLock.Dispose();
        _shutdown.Dispose();
    }

    // ─── active subscription bookkeeping ─────────────────────────────────────

    /// <summary>Non-generic facade so the dictionary can hold mixed-T subscriptions.</summary>
    private interface IActiveSubscription
    {
        object SubscribePayload { get; }
        void Dispatch(JsonElement data);
    }

    private sealed class ActiveSubscription<T> : IActiveSubscription
    {
        private readonly Func<JsonElement, IEnumerable<T>> _parser;
        private readonly List<ChannelWriter<T>> _writers = new();
        private readonly object _gate = new();

        public ActiveSubscription(object payload, Func<JsonElement, IEnumerable<T>> parser)
        {
            SubscribePayload = payload;
            _parser = parser;
        }

        public object SubscribePayload { get; }

        public bool AddWriter(ChannelWriter<T> writer)
        {
            lock (_gate)
            {
                _writers.Add(writer);
                return _writers.Count == 1;
            }
        }

        public bool RemoveWriter(ChannelWriter<T> writer)
        {
            lock (_gate)
            {
                _writers.Remove(writer);
                return _writers.Count == 0;
            }
        }

        public void Dispatch(JsonElement data)
        {
            IReadOnlyList<ChannelWriter<T>> snapshot;
            lock (_gate)
            {
                snapshot = _writers.ToArray();
            }

            foreach (var item in _parser(data))
            {
                foreach (var w in snapshot)
                    w.TryWrite(item);
            }
        }
    }
}
