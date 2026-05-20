using System.Reflection;

namespace Morphy;

public sealed class MorphyConfiguration
{
    private readonly Dictionary<(Type Source, Type Destination), TypeMap> _maps = new();

    public MorphyConfiguration() { }

    public MorphyConfiguration(Action<MorphyConfiguration> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(this);
    }

    public IMappingExpression<TSource, TDestination> CreateMap<TSource, TDestination>()
    {
        var inline = new InlineProfile();
        var map = new TypeMap(typeof(TSource), typeof(TDestination));
        inline.TypeMaps.Add(map);
        _maps[(typeof(TSource), typeof(TDestination))] = map;
        return new MappingExpression<TSource, TDestination>(map, inline);
    }

    public void AddProfile<TProfile>() where TProfile : Profile, new() => AddProfile(new TProfile());

    public void AddProfile(Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        foreach (var map in profile.TypeMaps)
            _maps[(map.SourceType, map.DestinationType)] = map;
    }

    public void AddProfiles(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        foreach (var assembly in assemblies)
            foreach (var type in assembly.GetTypes())
                if (!type.IsAbstract && typeof(Profile).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) is not null)
                    AddProfile((Profile)Activator.CreateInstance(type)!);
    }

    public void AddProfiles(params Assembly[] assemblies) => AddProfiles((IEnumerable<Assembly>)assemblies);

    internal TypeMap? GetMap(Type source, Type destination) =>
        _maps.TryGetValue((source, destination), out var map) ? map : null;

    private sealed class InlineProfile : Profile { }
}
