namespace EasyTrading.Abstractions.Models;

/// <summary>Result of a transfer / withdrawal operation.</summary>
/// <param name="TransferId">Exchange-assigned id of the transfer, if any.</param>
/// <param name="Success">Whether the transfer was accepted.</param>
/// <param name="ErrorMessage">Error description if rejected.</param>
public sealed record TransferResult(string? TransferId, bool Success, string? ErrorMessage);
