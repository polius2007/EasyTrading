using EasyTrading.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyTrading.Aster;

/// <summary>DI registration entry-point for the Aster client.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="AsterClient"/> with the configured options. Chain after
    /// <c>services.AddEasyTrading()</c>.
    /// </summary>
    /// <param name="builder">The EasyTrading builder.</param>
    /// <param name="configure">Configures <see cref="AsterClientOptions"/>.</param>
    /// <returns>The builder, for chaining further venue registrations.</returns>
    public static IEasyTradingBuilder AddAster(
        this IEasyTradingBuilder builder,
        Action<AsterClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.Configure(configure);
        builder.Services.TryAddSingleton<AsterClient>();
        builder.Services.TryAddSingleton<IAsterExchange>(sp => sp.GetRequiredService<AsterClient>());

        // Aster also satisfies IExchangeClient (the cross-DEX surface). We register a keyed
        // service instead of clobbering the unkeyed slot so a host running BOTH HyperLiquid and
        // Aster can resolve each by name.
        builder.Services.TryAddKeyedSingleton<IExchangeClient>("aster",
            (sp, _) => sp.GetRequiredService<AsterClient>());

        return builder;
    }
}
