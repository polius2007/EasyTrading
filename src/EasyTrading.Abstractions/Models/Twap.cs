namespace EasyTrading.Abstractions.Models;

/// <summary>Request to place a time-weighted-average-price (TWAP) order.</summary>
/// <param name="Symbol">Market symbol.</param>
/// <param name="Side">Buy or sell.</param>
/// <param name="Size">Total size to fill across the TWAP duration.</param>
/// <param name="DurationMinutes">Total duration over which to slice the execution.</param>
/// <param name="Randomize">If <c>true</c>, slice timing and size are randomised to obfuscate intent.</param>
/// <param name="ReduceOnly">If <c>true</c>, the TWAP may only reduce an existing position.</param>
public sealed record TwapRequest(
    string Symbol,
    OrderSide Side,
    decimal Size,
    int DurationMinutes,
    bool Randomize = false,
    bool ReduceOnly = false);

/// <summary>Result of placing a TWAP order.</summary>
/// <param name="TwapId">Exchange-assigned TWAP id.</param>
/// <param name="Success">Whether the TWAP was accepted.</param>
/// <param name="ErrorMessage">Error description if rejected.</param>
public sealed record TwapResult(long TwapId, bool Success, string? ErrorMessage);

/// <summary>One slice of a TWAP execution that has filled.</summary>
/// <param name="TwapId">Parent TWAP id.</param>
/// <param name="Symbol">Market symbol.</param>
/// <param name="Side">Side of the slice.</param>
/// <param name="Price">Fill price of this slice.</param>
/// <param name="Size">Filled size of this slice.</param>
/// <param name="Time">Time the slice was filled.</param>
public sealed record TwapSliceFill(
    long TwapId,
    string Symbol,
    OrderSide Side,
    decimal Price,
    decimal Size,
    DateTimeOffset Time);
