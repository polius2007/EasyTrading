using EasyTrading.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyTrading.Dydx;

/// <summary>DI registration entry-point for the dYdX v4 client.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="DydxClient"/> with the configured options. Chain after
    /// <c>services.AddEasyTrading()</c>.
    /// </summary>
    /// <param name="builder">The EasyTrading builder.</param>
    /// <param name="configure">Configures <see cref="DydxClientOptions"/>.</param>
    /// <returns>The builder, for chaining further venue registrations.</returns>
    public static IEasyTradingBuilder AddDydx(
        this IEasyTradingBuilder builder,
        Action<DydxClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.Configure(configure);
        builder.Services.TryAddSingleton<DydxClient>();
        builder.Services.TryAddSingleton<IDydxExchange>(sp => sp.GetRequiredService<DydxClient>());

        // dYdX also satisfies IExchangeClient. Use a keyed singleton so hosts running multiple
        // venues can resolve each by name without collision.
        builder.Services.TryAddKeyedSingleton<IExchangeClient>("dydx",
            (sp, _) => sp.GetRequiredService<DydxClient>());

        return builder;
    }
}
