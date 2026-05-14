namespace EasyTrading.Dydx;

/// <summary>
/// Credentials for signed dYdX v4 actions.
/// </summary>
/// <param name="Address">The user's <c>dydx1…</c>-prefixed bech32 address (derived from the
/// secp256k1 public key). Used as the <c>signerAddress</c> field on every Cosmos SDK transaction.</param>
/// <param name="Mnemonic">BIP-39 mnemonic for the trading wallet. The library derives the
/// secp256k1 key from <c>m/44'/118'/0'/0/0</c> (Cosmos default path) and uses it to sign
/// validator transactions. NEVER use your master / cold-wallet mnemonic in production —
/// generate a hot-wallet mnemonic dedicated to the bot.</param>
/// <param name="SubaccountNumber">dYdX v4 routes orders + positions per subaccount; <c>0</c> is
/// the default subaccount, additional ones are user-created. Defaults to <c>0</c>.</param>
/// <remarks>
/// <para>dYdX v4 is built on a Cosmos SDK app-chain (CometBFT consensus). Authentication is
/// secp256k1 signatures over Cosmos SDK transaction protobufs, broadcast to a validator's
/// gRPC endpoint. There is <b>no EVM-style EIP-712</b> here — that was v3 (StarkEx), which
/// is being deprecated.</para>
/// <para>The signed-action path is implemented in Phase 7.2; until then constructing this
/// record is a no-op for the scaffold and the writes throw <see cref="NotImplementedException"/>.</para>
/// </remarks>
public sealed record DydxCredentials(
    string Address,
    string Mnemonic,
    int SubaccountNumber = 0);
