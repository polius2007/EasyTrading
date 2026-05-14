# Security policy

## Reporting a vulnerability

If you've found a vulnerability — especially one that could result in **lost funds, leaked private keys, or signed messages being accepted that shouldn't be** — please report it privately first. Don't open a public GitHub issue, and don't push a public proof-of-concept to a fork before the fix is out.

Contact: **polius2007@gmail.com** with the subject `[easytrading-security]`.

A good report includes:

1. The affected version (e.g. `0.4.0-alpha.1`) and target framework.
2. The exact code path / commit that introduces the issue.
3. A minimal reproduction, plus the impact (key material at risk, signature reuse, fee bypass, etc.).
4. (If you have one) a proposed fix.

We'll acknowledge within 72 hours and aim to push a patched release within 7 days for high-severity issues. We'll credit you in the changelog unless you prefer anonymity.

## Scope

Things that **are** in scope:

- Cryptographic signing bugs (EIP-712 hashing, msgpack canonicalisation, ECDSA recovery byte, secp256k1 key handling).
- Authentication / agent / builder-approval logic in `EasyTrading.HyperLiquid`.
- Anything that could cause a user's order to be signed with parameters they didn't intend (price, size, direction, asset, builder address).
- WebSocket message handling that could allow a malicious server response to corrupt client state.

Things that **are not** in scope:

- HyperLiquid exchange bugs themselves — please report those to HyperLiquid.
- Builder-fee routing to the default `EasyTrading.pw` address; that is the project's documented funding model (see `README.md` § Disclaimer). Users can opt out via `HyperLiquidClientOptions.BuilderFee`.
- Vulnerabilities in upstream packages (`Nethereum.Signer`, etc.) — please report those to the respective project.

## Supported versions

We patch the latest `0.x` line. We don't backport fixes to older alpha releases.

| Version line | Patches |
|---|---|
| `0.4.x-alpha` | ✅ active |
| `0.3.x-alpha` | only critical signing / key-material issues |
| earlier      | no patches; please upgrade |

## Disclosure timeline

We follow coordinated disclosure: the reporter gets advance notice of the release, then we publish the patched version and the security advisory together. If a fix is already shipped at the moment of disclosure, we add the advisory to the changelog of the release that contains it.

## Disclaimer

Crypto trading carries significant financial risk independent of any code defects. This library is provided as-is under MIT, with no warranty. See `LICENSE` for details.
