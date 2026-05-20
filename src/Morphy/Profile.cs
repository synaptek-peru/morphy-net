namespace Morphy;

public abstract class Profile
{
    internal List<TypeMap> TypeMaps { get; } = new();

    protected IMappingExpression<TSource, TDestination> CreateMap<TSource, TDestination>()
    {
        var map = new TypeMap(typeof(TSource), typeof(TDestination));
        TypeMaps.Add(map);
        return new MappingExpression<TSource, TDestination>(map, this);
    }

    internal TypeMap CreateReverseMap(Type sourceType, Type destinationType)
    {
        var map = new TypeMap(sourceType, destinationType);
        TypeMaps.Add(map);
        return map;
    }
}
