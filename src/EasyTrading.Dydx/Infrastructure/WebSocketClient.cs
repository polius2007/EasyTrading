using System.Buffers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace EasyTrading.Dydx.Infrastructure;

/// <summary>
/// dYdX v4 Indexer WebSocket client. Multiplexes many subscriptions over a single connection.
/// Subscribe payload: <c>{"type":"subscribe","channel":"v4_trades","id":"BTC-USD"}</c>; messages
/// arrive as <c>{"type":"channel_data" | "subscribed" | "unsubscribed", "channel":"...", "id":"...", "contents":{...}}</c>.
/// </summary>
internal sealed class WebSocketClient : IAsyncDisposable
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

    /// <summary>Fires after each successful reconnect + re-subscribe.</summary>
    public event Action? Reconnected;

    public WebSocketClient(Uri url, TimeSpan reconnectDelay, ILogger logger)
    {
        _url = url;
        _initialReconnectDelay = reconnectDelay;
        _logger = logger;
    }

    /// <summary>
    /// Subscribe to a channel + id and yield typed updates. <paramref name="subscribePayload"/> is
    /// merged with <c>{"type":"subscribe"}</c> and sent on (re)connect; <paramref name="routingKey"/>
    /// must match the value the dispatcher rebuilds from incoming <c>channel</c> + <c>id</c> fields.
    /// </summary>
    public async IAsyncEnumerable<T> SubscribeAsync<T>(
        object subscribePayload,
        string routingKey,
        Func<JsonElement, IEnumerable<T>> parser,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subscribePayload);
        ArgumentNullException.ThrowIfNull(routingKey);
        ArgumentNullException.ThrowIfNull(parser);

        var channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        var entry = _subscriptions.GetOrAdd(routingKey,
            _ => new ActiveSubscription<T>(subscribePayload, parser));
        if (entry is not ActiveSubscription<T> matchingSub)
            throw new InvalidOperationException(
                $"Subscription '{routingKey}' is already registered with a different update type.");

        var isFirstWriter = matchingSub.AddWriter(channel.Writer);

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
            var wasLast = matchingSub.RemoveWriter(channel.Writer);
            if (wasLast)
            {
                _subscriptions.TryRemove(routingKey, out _);
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await SendSubscribeAsync(subscribePayload, subscribe: false, timeout.Token).ConfigureAwait(false);
                }
                catch { /* shutdown */ }
            }
            channel.Writer.TryComplete();
        }
    }

    // ─── connection / reconnect ─────────────────────────────────────────────

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_socket is { State: WebSocketState.Open }) return;

        await _connectLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_socket is { State: WebSocketState.Open }) return;
            await ConnectAndStartReaderAsync(ct).ConfigureAwait(false);
        }
        finally { _connectLock.Release(); }
    }

    private async Task ConnectAndStartReaderAsync(CancellationToken ct)
    {
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
                _logger.LogInformation("dYdX WebSocket reconnecting…");

                await _connectLock.WaitAsync(ct).ConfigureAwait(false);
                try { await ConnectAndStartReaderAsync(ct).ConfigureAwait(false); }
                finally { _connectLock.Release(); }

                foreach (var (_, sub) in _subscriptions)
                {
                    try { await SendSubscribeAsync(sub.SubscribePayload, subscribe: true, ct).ConfigureAwait(false); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Resubscribe failed."); }
                }

                _logger.LogInformation("dYdX WebSocket reconnected; {Count} subscriptions resubscribed.", _subscriptions.Count);

                try { Reconnected?.Invoke(); }
                catch (Exception ex) { _logger.LogWarning(ex, "Reconnected handler threw."); }
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

    private async Task SendSubscribeAsync(object subscribePayload, bool subscribe, CancellationToken ct)
    {
        // dYdX subscribe envelope: merge user payload with type=subscribe/unsubscribe.
        // We rely on the user's payload already including {channel, id} and just splice in the type.
        var payloadJson = JsonSerializer.Serialize(subscribePayload, JsonOptions.Default);
        using var payloadDoc = JsonDocument.Parse(payloadJson);

        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WriteString("type", subscribe ? "subscribe" : "unsubscribe");
            foreach (var prop in payloadDoc.RootElement.EnumerateObject())
                prop.WriteTo(writer);
            writer.WriteEndObject();
        }
        var bytes = ms.ToArray();

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var socket = _socket ?? throw new InvalidOperationException("WebSocket is not connected.");
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct).ConfigureAwait(false);
        }
        finally { _sendLock.Release(); }
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
                    _logger.LogWarning(ex, "dYdX WS receive failed; reconnecting.");
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
                    _logger.LogWarning(ex, "Failed to parse / dispatch dYdX WS message.");
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
        if (!msg.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String) return;
        var type = typeEl.GetString();

        // Subscribe / unsubscribe / connected acks — nothing to dispatch.
        if (type is "connected" or "subscribed" or "unsubscribed") return;

        // Channel data messages carry channel + id + contents.
        if (!msg.TryGetProperty("channel", out var channelEl) || channelEl.ValueKind != JsonValueKind.String) return;
        var channel = channelEl.GetString();
        if (channel is null) return;

        var id = msg.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
            ? idEl.GetString()
            : null;

        var routingKey = id is null ? channel : $"{channel}:{id}";

        if (!msg.TryGetProperty("contents", out var contents)) return;
        if (_subscriptions.TryGetValue(routingKey, out var sub))
        {
            try { sub.Dispatch(contents); }
            catch (Exception ex) { _logger.LogWarning(ex, "Subscriber dispatch failed for '{Key}'.", routingKey); }
        }
    }

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

    // ─── subscription bookkeeping ───────────────────────────────────────────

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
