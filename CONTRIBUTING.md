# Contributing to EasyTrading

Thanks for thinking about contributing. EasyTrading aims to be the canonical .NET client for DEX trading — clean abstractions, byte-correct signing, full test coverage, and AI-friendly docs. This file documents how to land changes here.

## Quick start (dev setup)

```powershell
git clone https://github.com/polius2007/EasyTrading.git
cd EasyTrading
dotnet build EasyTrading.slnx
dotnet test  EasyTrading.slnx
```

Optional — run integration tests against live HyperLiquid mainnet (read-only):

```powershell
$env:EASYTRADING_INTEGRATION="1"
dotnet test EasyTrading.slnx --filter "Category=Integration"
```

## Bug reports

Open a GitHub issue with:

1. **What** you did (the smallest code snippet that reproduces).
2. **What you expected**.
3. **What actually happened** (exception type + message + stack trace if any).
4. **Library version**, **.NET SDK version** (`dotnet --version`), and OS.

If the bug involves signing rejections from HyperLiquid, include the raw HL error string — it's usually in the exception message.

## Pull requests

Small, focused PRs are easiest to land.

**Before opening a PR:**
- `dotnet build EasyTrading.slnx` is clean (0 warnings, 0 errors on `net8.0` and `net9.0`).
- `dotnet test EasyTrading.slnx` is green (every existing test still passes).
- If the change is user-visible, add or update a test that demonstrates the new behaviour.
- If you touched the public API surface in `EasyTrading.Abstractions`, please open an issue first — that's a contract every DEX implementation honours.

**Style:**
- `decimal` for money; never `double` / `float`.
- `Async` suffix on every async method.
- `CancellationToken ct = default` as the last parameter.
- File-scoped namespaces, implicit usings, nullable enabled.
- Records for DTOs; classes only when you genuinely need behaviour or mutation.
- XML doc-comment every public type and member.
- Central package management — `<PackageReference>` entries must omit `Version` (versions live in `Directory.Packages.props`).

See [`AGENTS.md`](AGENTS.md) for the same conventions written for AI assistants.

## Adding a new DEX

The library is designed to make this straightforward. The pattern is:

1. Create a new `src/EasyTrading.<Venue>/` project, multi-targeting `net8.0;net9.0`.
2. Add a `*-specific` extension interface (e.g. `IAsterExchange : IExchangeClient`) with any venue-only sub-clients.
3. Implement `IExchangeClient` and every sub-interface. Use `EasyTrading.Core` for shared HTTP / WebSocket infrastructure when possible.
4. Add the venue's signing implementation alongside the existing `HlSigner`.
5. Add unit tests with embedded fixtures and at least one integration test (read-only) against the venue's live REST endpoint, gated by `EASYTRADING_INTEGRATION=1`.
6. Add a row to the **Supported DEXes** table in `README.md` and update `AGENTS.md` / `CLAUDE.md` / `llms.txt` to mention the venue.
7. Ship it as its own NuGet package via `release.yml` — packages are picked up automatically.

## Building the docs locally

```powershell
dotnet tool install -g docfx
docfx docfx.json
# served at http://localhost:8080
docfx docfx.json --serve
```

## Things to keep in mind

- **Builder-fee routing** in `HlBuilderDefaults` is the project's monetisation; please don't change it lightly. If you have a genuine reason (per-fork override, etc.) open a discussion first.
- **`Nethereum.Signer`** is pinned to `4.26.0` because `4.27.0` has a regression in `EthECKey.SignAndCalculateV` (`Invalid DER signature` round-trip). If a newer version fixes it, please verify via the signer unit tests before bumping.
- **Test count**: unit tests should grow monotonically; integration tests should be added when a new venue or live feature lands.

## License

By contributing, you agree your code will be released under the project's MIT license.
