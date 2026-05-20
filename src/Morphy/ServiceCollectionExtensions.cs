using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Morphy;

namespace Microsoft.Extensions.DependencyInjection;

public static class MorphyServiceCollectionExtensions
{
    public static IServiceCollection AddMorphy(this IServiceCollection services, Action<MorphyConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var configuration = new MorphyConfiguration(configure);
        services.AddSingleton(configuration);
        services.AddSingleton<IMapper>(sp => new Mapper(sp.GetRequiredService<MorphyConfiguration>()));
        return services;
    }

    public static IServiceCollection AddMorphy(this IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        return services.AddMorphy(cfg => cfg.AddProfiles(assemblies));
    }

    public static IServiceCollection AddMorphy(this IServiceCollection services, params Type[] markerTypes)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(markerTypes);

        var assemblies = markerTypes.Select(t => t.Assembly).Distinct().ToArray();
        return services.AddMorphy(assemblies);
    }
}
