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

        var (dest, ctorConsumed) = CreateInstance(source, map);
        ApplyMap(source, dest, map, ctorConsumed);
        return dest;
    }

    private (object Instance, HashSet<string> Consumed) CreateInstance(object source, TypeMap map)
    {
        var destType = map.DestinationType;
        var emptyCtor = destType.GetConstructor(Type.EmptyTypes);
        if (emptyCtor is not null)
            return (emptyCtor.Invoke(null), new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var ctors = destType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (ctors.Length == 0)
            throw new MorphyException($"Cannot create instance of {destType.Name}: no public constructor available.");

        var ctor = ctors.OrderByDescending(c => c.GetParameters().Length).First();
        var parameters = ctor.GetParameters();
        var srcProps = ReadableCache.GetOrAdd(map.SourceType, GetReadableProperties);
        var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var args = new object?[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            var paramName = param.Name ?? throw new MorphyException(
                $"Constructor of {destType.Name} has an unnamed parameter at position {i}.");

            var cfg = FindMemberConfigCaseInsensitive(map.Members, paramName, out var matchedKey);
            if (cfg is not null)
            {
                consumed.Add(matchedKey!);
                if (cfg.Ignored)
                {
                    args[i] = ParameterDefault(param);
                    continue;
                }
                if (cfg.Resolver is not null)
                {
                    var raw = cfg.Resolver(source);
                    args[i] = ConvertForParameter(raw, raw?.GetType(), param.ParameterType);
                    continue;
                }
            }

            var srcProp = FindByNameCaseInsensitive(srcProps, paramName);
            if (srcProp is not null)
            {
                consumed.Add(srcProp.Name);
                var srcValue = srcProp.GetValue(source);
                args[i] = ConvertForParameter(srcValue, srcProp.PropertyType, param.ParameterType);
                continue;
            }

            if (param.HasDefaultValue)
            {
                args[i] = param.DefaultValue;
                continue;
            }

            if (!param.ParameterType.IsValueType || Nullable.GetUnderlyingType(param.ParameterType) is not null)
            {
                args[i] = null;
                continue;
            }

            throw new MorphyException(
                $"Cannot construct {destType.Name}: parameter '{paramName}' ({param.ParameterType.Name}) " +
                "has no matching source property, ForMember resolver, default value, or nullable type. " +
                "Use ForMember(d => d." + paramName + ", o => o.MapFrom(...)) to provide a value.");
        }

        return (ctor.Invoke(args), consumed);
    }

    private object? ConvertForParameter(object? value, Type? declaredSourceType, Type parameterType)
    {
        if (value is null)
            return parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) is null
                ? Activator.CreateInstance(parameterType)
                : null;

        return Map(value, declaredSourceType ?? value.GetType(), parameterType);
    }

    private static object? ParameterDefault(ParameterInfo param)
    {
        if (param.HasDefaultValue) return param.DefaultValue;
        if (!param.ParameterType.IsValueType || Nullable.GetUnderlyingType(param.ParameterType) is not null) return null;
        return Activator.CreateInstance(param.ParameterType);
    }

    private static MemberConfig? FindMemberConfigCaseInsensitive(
        Dictionary<string, MemberConfig> members, string name, out string? matchedKey)
    {
        if (members.TryGetValue(name, out var exact))
        {
            matchedKey = name;
            return exact;
        }
        foreach (var kvp in members)
        {
            if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                matchedKey = kvp.Key;
                return kvp.Value;
            }
        }
        matchedKey = null;
        return null;
    }

    private static PropertyInfo? FindByNameCaseInsensitive(PropertyInfo[] props, string name)
    {
        foreach (var p in props)
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }

    private void ApplyMap(object source, object destination, TypeMap map, HashSet<string>? skipMembers = null)
    {
        map.BeforeMap?.Invoke(source, destination);

        var destProps = WritableCache.GetOrAdd(map.DestinationType, GetWritableProperties);
        var srcProps = ReadableCache.GetOrAdd(map.SourceType, GetReadableProperties);

        foreach (var destProp in destProps)
        {
            if (skipMembers is not null && skipMembers.Contains(destProp.Name)) continue;

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
