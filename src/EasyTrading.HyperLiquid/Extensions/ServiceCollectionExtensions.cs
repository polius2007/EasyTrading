using EasyTrading.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyTrading.HyperLiquid;

/// <summary>DI registration entry-point for the HyperLiquid client.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="HyperLiquidClient"/> with the configured options. Chain after
    /// <c>services.AddEasyTrading()</c>.
    /// </summary>
    /// <param name="builder">The EasyTrading builder.</param>
    /// <param name="configure">Configures <see cref="HyperLiquidClientOptions"/>.</param>
    /// <returns>The builder, for chaining further venue registrations.</returns>
    public static IEasyTradingBuilder AddHyperLiquid(
        this IEasyTradingBuilder builder,
        Action<HyperLiquidClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.Configure(configure);
        builder.Services.TryAddSingleton<HyperLiquidClient>();
        builder.Services.TryAddSingleton<IHyperLiquidExchange>(sp => sp.GetRequiredService<HyperLiquidClient>());
        builder.Services.TryAddSingleton<IExchangeClient>(sp => sp.GetRequiredService<HyperLiquidClient>());

        return builder;
    }
}
