# GitHub Copilot Instructions for EasyTrading

This repository builds **EasyTrading**, a multi-DEX trading client for .NET. See [AGENTS.md](../AGENTS.md) for the full guidance — this file is the short version for Copilot.

## Hard rules (apply always)

- All money values use `decimal`; never `double` or `float`.
- Async methods carry the `Async` suffix and accept `CancellationToken ct = default` as the last parameter.
- Methods are grouped by **entity**, not by intent: orders → `client.Orders.*`, positions → `client.Positions.*`, markets → `client.Markets.*`, and so on.
- WebSocket subscriptions return `IAsyncEnumerable<T>` — iterate with `await foreach`.
- Check `client.Capabilities.HasFlag(...)` before calling optional features (TWAP, vaults, builder fees, etc.).
- Catch typed exceptions: `RateLimitException`, `InsufficientFundsException`, `InvalidOrderException`, `AuthenticationException`, `SigningException`. Don't catch raw `Exception`.
- For HyperLiquid, prefer **agent wallets** over the master private key — call `IAccount.ApproveAgentAsync(...)` and use the agent's key for trading.
- DI registration: `services.AddEasyTrading().AddHyperLiquid(opts => ...)`.
- DTOs are `record` types with positional parameters and full XML doc-comments.

## When suggesting code that uses EasyTrading

Prefer the convenience overloads:

```csharp
// Good — concise and clear
await ex.Orders.PlaceLimitAsync("BTC", OrderSide.Buy, price: 60_000m, size: 0.01m, tif: TimeInForce.Alo);

// Use the full OrderRequest only when you need fields the overload doesn't expose
```

Prefer `IHyperLiquidExchange` injection when HL-specific features (`Vaults`, `Staking`, `Builder`) are needed; use `IExchangeClient` for cross-DEX strategy code.

## Repo conventions

- File-scoped namespaces, implicit usings, nullable enabled.
- Central package management — `<PackageReference>` entries omit `Version` (versions live in `Directory.Packages.props`).
- `from` / `to` for time-range parameters (CA1716 is suppressed globally).
- XML doc-comment every public type and member.
