using EasyTrading.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyTrading.HyperLiquid;

/// <summary>DI registration entry-points for EasyTrading and the HyperLiquid client.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the base EasyTrading services. Call this once, then chain one or more
    /// exchange-specific registrations such as <see cref="AddHyperLiquid"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>An <see cref="IEasyTradingBuilder"/> for chaining.</returns>
    public static IEasyTradingBuilder AddEasyTrading(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new EasyTradingBuilder(services);
    }

    /// <summary>
    /// Registers a <see cref="HyperLiquidClient"/> with the configured options.
    /// </summary>
    /// <param name="builder">The EasyTrading builder.</param>
    /// <param name="configure">Configures <see cref="HyperLiquidClientOptions"/>.</param>
    /// <returns>The builder, for chaining.</returns>
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

/// <summary>Builder type returned by <see cref="ServiceCollectionExtensions.AddEasyTrading"/>.</summary>
public interface IEasyTradingBuilder
{
    /// <summary>The underlying service collection.</summary>
    IServiceCollection Services { get; }
}

internal sealed class EasyTradingBuilder(IServiceCollection services) : IEasyTradingBuilder
{
    public IServiceCollection Services { get; } = services;
}
