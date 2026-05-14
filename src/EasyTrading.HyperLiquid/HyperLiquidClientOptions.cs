using EasyTrading.Abstractions.Models;

namespace EasyTrading.HyperLiquid;

/// <summary>Options for <see cref="HyperLiquidClient"/>.</summary>
public sealed class HyperLiquidClientOptions
{
    /// <summary>Which network to talk to. Defaults to <see cref="HyperLiquidNetwork.Mainnet"/>.</summary>
    public HyperLiquidNetwork Network { get; set; } = HyperLiquidNetwork.Mainnet;

    /// <summary>Credentials for signing Exchange-endpoint requests. Read-only Info calls don't require this.</summary>
    public HyperLiquidCredentials? Credentials { get; set; }

    /// <summary>Override the REST base URL. If <c>null</c>, the URL is derived from <see cref="Network"/>.</summary>
    public Uri? RestBaseUrl { get; set; }

    /// <summary>Override the WebSocket URL. If <c>null</c>, the URL is derived from <see cref="Network"/>.</summary>
    public Uri? WebSocketUrl { get; set; }

    /// <summary>Per-request timeout. Defaults to 30 seconds.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Initial delay between WebSocket reconnect attempts (exponential backoff applies). Defaults to 2 seconds.</summary>
    public TimeSpan WebSocketReconnectDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Optional override for the builder fee attached to each order. When <c>null</c> (default), the
    /// library's built-in builder routing is used to fund continued development. Set to a custom
    /// <see cref="BuilderFee"/> to route fees elsewhere, or use a zero <see cref="BuilderFee.FeeRate"/>
    /// to effectively opt out.
    /// </summary>
    public BuilderFee? BuilderFee { get; set; }

    /// <summary>Resolves to the effective REST base URL based on <see cref="Network"/> and <see cref="RestBaseUrl"/>.</summary>
    /// <returns>The REST base URL.</returns>
    public Uri GetEffectiveRestBaseUrl() => RestBaseUrl ?? Network switch
    {
        HyperLiquidNetwork.Mainnet => new Uri("https://api.hyperliquid.xyz"),
        HyperLiquidNetwork.Testnet => new Uri("https://api.hyperliquid-testnet.xyz"),
        _ => throw new ArgumentOutOfRangeException(nameof(Network), Network, "Unknown HyperLiquid network."),
    };

    /// <summary>Resolves to the effective WebSocket URL based on <see cref="Network"/> and <see cref="WebSocketUrl"/>.</summary>
    /// <returns>The WebSocket URL.</returns>
    public Uri GetEffectiveWebSocketUrl() => WebSocketUrl ?? Network switch
    {
        HyperLiquidNetwork.Mainnet => new Uri("wss://api.hyperliquid.xyz/ws"),
        HyperLiquidNetwork.Testnet => new Uri("wss://api.hyperliquid-testnet.xyz/ws"),
        _ => throw new ArgumentOutOfRangeException(nameof(Network), Network, "Unknown HyperLiquid network."),
    };
}
