namespace EasyTrading.Aster;

/// <summary>
/// Credentials for signing Aster Exchange requests.
/// </summary>
/// <param name="MasterAddress">The user's main wallet address (the <c>user</c> in Aster's API).
/// Aster registers an API wallet ("signer") against this master via the Aster web UI; once registered,
/// trades are signed with the signer's private key on behalf of the master.</param>
/// <param name="SignerAddress">The API wallet address ("signer" in Aster's API). Must already be
/// approved against <paramref name="MasterAddress"/> via the Aster web UI before any signed request will succeed.</param>
/// <param name="PrivateKey">The hex-encoded private key of the <paramref name="SignerAddress"/>
/// API wallet — used to sign EIP-712 messages. Never use your master wallet's private key.</param>
/// <remarks>
/// <para>Aster's auth model is conceptually identical to HyperLiquid's master + agent split: the
/// API wallet ("signer" on Aster, "agent" on HL) signs requests on behalf of the master account,
/// can be revoked at any time without rotating the master key, and cannot withdraw funds.</para>
/// <para>To register an API wallet, visit <c>https://www.asterdex.com/en/api-wallet</c>
/// and switch the page mode to <c>Pro API</c> at the top.</para>
/// </remarks>
public sealed record AsterCredentials(
    string MasterAddress,
    string SignerAddress,
    string PrivateKey);
