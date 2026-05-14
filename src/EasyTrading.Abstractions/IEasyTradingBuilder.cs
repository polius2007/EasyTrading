using Microsoft.Extensions.DependencyInjection;

namespace EasyTrading.Abstractions;

/// <summary>
/// Marker returned by <see cref="ServiceCollectionExtensions.AddEasyTrading"/>. Venue packages
/// hang their <c>AddXxx</c> extension methods off this type so registration chains read as
/// <c>services.AddEasyTrading().AddHyperLiquid(...).AddAster(...)</c>.
/// </summary>
public interface IEasyTradingBuilder
{
    /// <summary>The underlying service collection.</summary>
    IServiceCollection Services { get; }
}

internal sealed class EasyTradingBuilder(IServiceCollection services) : IEasyTradingBuilder
{
    public IServiceCollection Services { get; } = services;
}

/// <summary>DI entry-point used by every <c>EasyTrading</c> venue package.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the base EasyTrading services. Call this once, then chain one or more
    /// venue-specific registrations such as <c>AddHyperLiquid()</c> or <c>AddAster()</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>An <see cref="IEasyTradingBuilder"/> for chaining.</returns>
    public static IEasyTradingBuilder AddEasyTrading(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new EasyTradingBuilder(services);
    }
}
