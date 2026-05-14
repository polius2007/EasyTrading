using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace EasyTrading.Aster.Infrastructure;

/// <summary>
/// Aster WebSocket client. Multiplexes many subscriptions over a single connection per client
/// instance, following the Binance-style wire protocol Aster inherits: subscribe via
/// <c>{"method":"SUBSCRIBE","params":["btcusdt@aggTrade"],"id":N}</c>; messages arrive as
/// <c>{"e":"aggTrade",...}</c>.
/// </summary>
/// <remarks>
/// <para>Routing: every subscriber registers its stream name (e.g. <c>"btcusdt@aggTrade"</c>) as
/// the lookup key. Incoming messages carry an <c>"e"</c> field plus <c>"s"</c> (symbol) /
/// other keys that let us rebuild the stream name and dispatch.</para>
/// <para>On disconnect: exponential-ish backoff via
/// <see cref="AsterClientOptions.WebSocketReconnectDelay"/>, then re-subscribe every active key.</para>
/// </remarks>
internal sealed class WebSocketClient : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<Uri>> _urlProvider;
    private readonly TimeSpan _initialReconnectDelay;
    private readonly ILogger _logger;

    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private readonly ConcurrentDictionary<string, IActiveSubscription> _subscriptions = new();
    private int _requestId;

    private ClientWebSocket? _socket;
    private Task? _readerTask;
    private int _disposed;

    /// <summary>Fires after every successful reconnect + re-subscribe. Stream gap-recovery hooks attach here.</summary>
    public event Action? Reconnected;

    public WebSocketClient(Func<CancellationToken, Task<Uri>> urlProvider, TimeSpan reconnectDelay, ILogger logger)
    {
        _urlProvider = urlProvider;
        _initialReconnectDelay = reconnectDelay;
        _logger = logger;
    }

    /// <summary>
    /// Subscribe to a stream and yield typed updates. The subscriber owns its own
    /// <see cref="Channel{T}"/>; multiple subscribers to the same key share one HL subscription.
    /// </summary>
    public async IAsyncEnumerable<T> SubscribeAsync<T>(
        string streamName,
        Func<JsonElement, IEnumerable<T>> parser,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(streamName);
        ArgumentNullException.ThrowIfNull(parser);

        var channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        var entry = _subscriptions.GetOrAdd(streamName,
            _ => new ActiveSubscription<T>(streamName, parser));
        if (entry is not ActiveSubscription<T> matchingSub)
            throw new InvalidOperationException(
                $"Stream '{streamName}' is already registered with a different update type.");

        var isFirstWriter = matchingSub.AddWriter(channel.Writer);

        try
        {
            await EnsureConnectedAsync(ct).ConfigureAwait(false);
            if (isFirstWriter)
                await SendSubscribeAsync(streamName, subscribe: true, ct).ConfigureAwait(false);

            await foreach (var item in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                yield return item;
        }
        finally
        {
            var wasLast = matchingSub.RemoveWriter(channel.Writer);
            if (wasLast)
            {
                _subscriptions.TryRemove(streamName, out _);
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await SendSubscribeAsync(streamName, subscribe: false, timeout.Token).ConfigureAwait(false);
                }
                catch { /* shutdown */ }
            }
            channel.Writer.TryComplete();
        }
    }

    // ─── connection management ───────────────────────────────────────────────

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_socket is { State: WebSocketState.Open }) return;

        await _connectLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_socket is { State: WebSocketState.Open }) return;
            await ConnectAndStartReaderAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task ConnectAndStartReaderAsync(CancellationToken ct)
    {
        if (_socket is not null)
        {
            try { _socket.Abort(); } catch { /* best-effort */ }
            _socket.Dispose();
            _socket = null;
        }

        var url = await _urlProvider(ct).ConfigureAwait(false);
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        await socket.ConnectAsync(url, ct).ConfigureAwait(false);
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
                _logger.LogInformation("Aster WebSocket reconnecting…");

                await _connectLock.WaitAsync(ct).ConfigureAwait(false);
                try { await ConnectAndStartReaderAsync(ct).ConfigureAwait(false); }
                finally { _connectLock.Release(); }

                foreach (var (_, sub) in _subscriptions)
                {
                    try { await SendSubscribeAsync(sub.StreamName, subscribe: true, ct).ConfigureAwait(false); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Resubscribe failed for {Stream}.", sub.StreamName); }
                }

                _logger.LogInformation("Aster WebSocket reconnected; {Count} subscriptions resubscribed.", _subscriptions.Count);

                try { Reconnected?.Invoke(); }
                catch (Exception ex) { _logger.LogWarning(ex, "Reconnected event handler threw."); }
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconnect attempt failed; retrying in {Delay}.", delay);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, TimeSpan.FromSeconds(30).Ticks));
            }
        }
    }

    private async Task SendSubscribeAsync(string streamName, bool subscribe, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _requestId);
        var envelope = new
        {
            method = subscribe ? "SUBSCRIBE" : "UNSUBSCRIBE",
            @params = new[] { streamName },
            id,
        };

        var json = JsonSerializer.Serialize(envelope, JsonOptions.Default);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var socket = _socket ?? throw new InvalidOperationException("WebSocket is not connected.");
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct).ConfigureAwait(false);
        }
        finally { _sendLock.Release(); }
    }

    // ─── reader loop + dispatch ─────────────────────────────────────────────

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
                            await TriggerReconnectAsync(ct).ConfigureAwait(false);
                            return;
                        }
                        ms.Write(buffer, 0, result.Count);
                        if (result.EndOfMessage) break;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Aster WS receive failed; reconnecting.");
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
                    _logger.LogWarning(ex, "Failed to parse / dispatch Aster WS message.");
                }
            }
        }
        finally { pool.Return(buffer); }
    }

    private async Task TriggerReconnectAsync(CancellationToken ct)
    {
        _ = Task.Run(() => ReconnectAsync(ct), ct);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private void DispatchMessage(JsonElement msg)
    {
        // Subscribe acknowledgements look like {result: null, id: N} — skip silently.
        if (msg.TryGetProperty("result", out _) && msg.TryGetProperty("id", out _)) return;

        // Combined stream wrapping: {stream: "...", data: {...}}
        if (msg.TryGetProperty("stream", out var streamEl) && msg.TryGetProperty("data", out var dataEl))
        {
            var name = streamEl.GetString();
            if (name is not null && _subscriptions.TryGetValue(name, out var sub))
                Try(() => sub.Dispatch(dataEl), name);
            return;
        }

        // Single-stream wire: payload at root level. Reconstruct the stream name from `e` + `s`.
        var key = ComputeRoutingKey(msg);
        if (key is null) return;
        if (_subscriptions.TryGetValue(key, out var entry))
            Try(() => entry.Dispatch(msg), key);
    }

    private void Try(Action a, string key)
    {
        try { a(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Subscriber dispatch failed for '{Key}'.", key); }
    }

    /// <summary>
    /// Build the stream name our subscribers registered with from an incoming message's
    /// event type + symbol. Matches the format Aster emits when subscribing to single streams.
    /// </summary>
    private static string? ComputeRoutingKey(JsonElement msg)
    {
        if (!msg.TryGetProperty("e", out var eEl) || eEl.ValueKind != JsonValueKind.String) return null;
        var eventType = eEl.GetString();
        if (eventType is null) return null;

        // Symbol — Binance/Aster uses "s" for the trading symbol in most payloads.
        string? sym = msg.TryGetProperty("s", out var sEl) && sEl.ValueKind == JsonValueKind.String
            ? sEl.GetString()
            : null;

        return eventType.ToUpperInvariant() switch
        {
            "AGGTRADE"        => sym is null ? null : sym.ToLowerInvariant() + "@aggTrade",
            "DEPTHUPDATE"     => sym is null ? null : sym.ToLowerInvariant() + "@depth",
            "BOOKTICKER"      => sym is null ? null : sym.ToLowerInvariant() + "@bookTicker",
            "MARKPRICEUPDATE" => sym is null ? null : sym.ToLowerInvariant() + "@markPrice",
            "KLINE"           => BuildKlineKey(msg, sym),
            // User-data events come through a separate listenKey-bound socket; the user-stream
            // wrapper dispatches by the well-known event type itself.
            "ACCOUNT_UPDATE"      => "user:accountUpdate",
            "ORDER_TRADE_UPDATE"  => "user:orderTradeUpdate",
            "MARGIN_CALL"         => "user:marginCall",
            "LISTENKEYEXPIRED"    => "user:listenKeyExpired",
            _ => sym is null ? null : sym.ToLowerInvariant() + "@" + eventType,
        };
    }

    private static string? BuildKlineKey(JsonElement msg, string? sym)
    {
        if (sym is null) return null;
        if (!msg.TryGetProperty("k", out var k)) return null;
        if (!k.TryGetProperty("i", out var iEl)) return null;
        var interval = iEl.GetString();
        return interval is null ? null : sym.ToLowerInvariant() + "@kline_" + interval;
    }

    // ─── dispose ─────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

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

    // ─── active-subscription bookkeeping ────────────────────────────────────

    private interface IActiveSubscription
    {
        string StreamName { get; }
        void Dispatch(JsonElement data);
    }

    private sealed class ActiveSubscription<T> : IActiveSubscription
    {
        private readonly Func<JsonElement, IEnumerable<T>> _parser;
        private readonly List<ChannelWriter<T>> _writers = new();
        private readonly object _gate = new();

        public ActiveSubscription(string streamName, Func<JsonElement, IEnumerable<T>> parser)
        {
            StreamName = streamName;
            _parser = parser;
        }

        public string StreamName { get; }

        public bool AddWriter(ChannelWriter<T> w)
        {
            lock (_gate) { _writers.Add(w); return _writers.Count == 1; }
        }

        public bool RemoveWriter(ChannelWriter<T> w)
        {
            lock (_gate) { _writers.Remove(w); return _writers.Count == 0; }
        }

        public void Dispatch(JsonElement data)
        {
            IReadOnlyList<ChannelWriter<T>> snapshot;
            lock (_gate) { snapshot = _writers.ToArray(); }
            foreach (var item in _parser(data))
                foreach (var w in snapshot) w.TryWrite(item);
        }
    }
}
