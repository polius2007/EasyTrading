namespace EasyTrading.HyperLiquid;

/// <summary>Credentials used to sign HyperLiquid Exchange-endpoint requests.</summary>
/// <param name="MasterAddress">EVM-style address of the master account that owns funds and approves agents.</param>
/// <param name="PrivateKey">Hex-encoded private key used for signing. This may be the master account's key, or — recommended — an approved agent wallet's key.</param>
/// <param name="AgentName">Name of the agent wallet, if <see cref="PrivateKey"/> belongs to an agent rather than the master account.</param>
/// <param name="VaultAddress">If set, requests are signed by the master account but applied to this vault / sub-account address.</param>
/// <remarks>
/// For production use, prefer an agent wallet over the master account's private key — agents can be
/// revoked without rotating the master key. Use <c>IAccount.ApproveAgentAsync</c> to create one.
/// </remarks>
public sealed record HyperLiquidCredentials(
    string MasterAddress,
    string PrivateKey,
    string? AgentName = null,
    string? VaultAddress = null);
