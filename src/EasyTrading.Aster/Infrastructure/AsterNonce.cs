namespace EasyTrading.Aster.Infrastructure;

/// <summary>
/// Strictly-monotonic microsecond nonce. Aster requires the nonce to be the current timestamp in
/// microseconds within ±10 seconds of server time and rejects duplicates within its 100-deep
/// per-user window — incrementing locally when two requests land in the same microsecond
/// guarantees uniqueness without risking server rejection.
/// </summary>
internal sealed class AsterNonce
{
    private long _last;

    /// <summary>Return the next monotonic nonce in microseconds since the Unix epoch.</summary>
    public long Next()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L;
        long next;
        long prev;
        do
        {
            prev = Interlocked.Read(ref _last);
            next = Math.Max(now, prev + 1);
        }
        while (Interlocked.CompareExchange(ref _last, next, prev) != prev);
        return next;
    }
}
