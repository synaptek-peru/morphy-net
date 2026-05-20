using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace Morphy;

public sealed class Mapper : IMapper
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> ReadableCache = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> WritableCache = new();

    private readonly MorphyConfiguration _configuration;

    public Mapper(MorphyConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public TDestination Map<TDestination>(object source)
    {
        if (source is null) return default!;
        return (TDestination)Map(source, source.GetType(), typeof(TDestination))!;
    }

    public TDestination Map<TSource, TDestination>(TSource source)
    {
        if (source is null) return default!;
        return (TDestination)Map(source, typeof(TSource), typeof(TDestination))!;
    }

    public TDestination Map<TSource, TDestination>(TSource source, TDestination destination)
    {
        if (source is null) return destination;
        if (destination is null) return Map<TSource, TDestination>(source);

        var map = _configuration.GetMap(typeof(TSource), typeof(TDestination))
            ?? throw NoMap(typeof(TSource), typeof(TDestination));

        ApplyMap(source, destination, map);
        return destination;
    }

    public object? Map(object? source, Type sourceType, Type destinationType)
    {
        if (source is null) return DefaultValue(destinationType);

        var actualSourceType = source.GetType();
        var underlyingDest = Nullable.GetUnderlyingType(destinationType) ?? destinationType;

        if (underlyingDest.IsAssignableFrom(actualSourceType) && !HasMapBetween(actualSourceType, underlyingDest))
            return source;

        if (IsEnumerable(actualSourceType) && IsEnumerable(underlyingDest))
            return MapCollection(source, actualSourceType, underlyingDest);

        if (TryConvertPrimitive(source, actualSourceType, underlyingDest, out var converted))
            return converted;

        var map = _configuration.GetMap(actualSourceType, underlyingDest)
            ?? _configuration.GetMap(sourceType, underlyingDest)
            ?? throw NoMap(actualSourceType, underlyingDest);

        var dest = Activator.CreateInstance(map.DestinationType)
            ?? throw new MorphyException($"Cannot create instance of {map.DestinationType.Name}. A parameterless constructor is required.");

        ApplyMap(source, dest, map);
        return dest;
    }

    private void ApplyMap(object source, object destination, TypeMap map)
    {
        map.BeforeMap?.Invoke(source, destination);

        var destProps = WritableCache.GetOrAdd(map.DestinationType, GetWritableProperties);
        var srcProps = ReadableCache.GetOrAdd(map.SourceType, GetReadableProperties);

        foreach (var destProp in destProps)
        {
            if (map.Members.TryGetValue(destProp.Name, out var cfg))
            {
                if (cfg.Ignored) continue;
                if (cfg.Resolver is not null)
                {
                    var raw = cfg.Resolver(source);
                    AssignValue(destination, destProp, raw, raw?.GetType());
                    continue;
                }
            }

            var srcProp = FindByName(srcProps, destProp.Name);
            if (srcProp is null) continue;

            var srcValue = srcProp.GetValue(source);
            AssignValue(destination, destProp, srcValue, srcProp.PropertyType);
        }

        map.AfterMap?.Invoke(source, destination);
    }

    private void AssignValue(object destination, PropertyInfo destProp, object? value, Type? declaredSourceType)
    {
        if (value is null)
        {
            if (!destProp.PropertyType.IsValueType || Nullable.GetUnderlyingType(destProp.PropertyType) is not null)
                destProp.SetValue(destination, null);
            return;
        }

        var srcType = declaredSourceType ?? value.GetType();
        var mapped = Map(value, srcType, destProp.PropertyType);
        destProp.SetValue(destination, mapped);
    }

    private object MapCollection(object source, Type sourceType, Type destinationType)
    {
        var sourceElement = GetElementType(sourceType)
            ?? throw new MorphyException($"Cannot determine element type of {sourceType.Name}.");
        var destElement = GetElementType(destinationType)
            ?? throw new MorphyException($"Cannot determine element type of {destinationType.Name}.");

        var listType = typeof(List<>).MakeGenericType(destElement);
        var list = (IList)Activator.CreateInstance(listType)!;

        foreach (var item in (IEnumerable)source)
            list.Add(Map(item, item?.GetType() ?? sourceElement, destElement));

        if (destinationType.IsArray)
        {
            var array = Array.CreateInstance(destElement, list.Count);
            list.CopyTo(array, 0);
            return array;
        }

        if (destinationType.IsAssignableFrom(listType))
            return list;

        var ctor = destinationType.GetConstructor([typeof(IEnumerable<>).MakeGenericType(destElement)]);
        if (ctor is not null)
            return ctor.Invoke([list]);

        throw new MorphyException($"Cannot construct destination collection of type {destinationType.Name}.");
    }

    private bool HasMapBetween(Type source, Type destination) =>
        _configuration.GetMap(source, destination) is not null;

    private static bool IsEnumerable(Type type) =>
        type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);

    private static Type? GetElementType(Type type)
    {
        if (type.IsArray) return type.GetElementType();

        foreach (var i in type.GetInterfaces())
        {
            if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return i.GetGenericArguments()[0];
        }
        return null;
    }

    private static bool TryConvertPrimitive(object source, Type sourceType, Type destinationType, out object? result)
    {
        result = null;
        if (sourceType == destinationType) { result = source; return true; }

        if (destinationType.IsEnum)
        {
            if (sourceType == typeof(string))
            {
                result = Enum.Parse(destinationType, (string)source, ignoreCase: true);
                return true;
            }
            if (sourceType.IsPrimitive || sourceType.IsEnum)
            {
                result = Enum.ToObject(destinationType, source);
                return true;
            }
        }

        if (sourceType.IsEnum && destinationType == typeof(string))
        {
            result = source.ToString();
            return true;
        }

        if (source is IConvertible && typeof(IConvertible).IsAssignableFrom(destinationType))
        {
            try
            {
                result = Convert.ChangeType(source, destinationType);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private static object? DefaultValue(Type type) =>
        type.IsValueType && Nullable.GetUnderlyingType(type) is null ? Activator.CreateInstance(type) : null;

    private static PropertyInfo? FindByName(PropertyInfo[] props, string name)
    {
        foreach (var p in props)
            if (p.Name == name) return p;
        return null;
    }

    private static PropertyInfo[] GetReadableProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToArray();

    private static PropertyInfo[] GetWritableProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.GetIndexParameters().Length == 0)
            .ToArray();

    private static MorphyException NoMap(Type source, Type destination) =>
        new($"No mapping registered for {source.FullName} -> {destination.FullName}. Did you forget to add a Profile or call CreateMap?");
}
