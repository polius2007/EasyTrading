namespace EasyTrading.HyperLiquid.Infrastructure;

/// <summary>
/// Provides strictly monotonic, millisecond-precision nonces. HyperLiquid rejects any nonce that
/// is not strictly greater than the previous one for the same wallet, so we serialise the
/// counter and bump it forward if the wall clock ever returns the same (or an earlier) value.
/// </summary>
internal sealed class HlNonce
{
    private long _last;

    /// <summary>Allocates the next nonce. Thread-safe; values are strictly increasing.</summary>
    /// <returns>A unix-time-millis nonce greater than any previously returned.</returns>
    public long Next()
    {
        while (true)
        {
            var prev = Interlocked.Read(ref _last);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var candidate = now > prev ? now : prev + 1;
            if (Interlocked.CompareExchange(ref _last, candidate, prev) == prev)
                return candidate;
        }
    }
}
