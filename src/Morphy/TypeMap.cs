using System.Reflection;

namespace Morphy;

internal sealed class TypeMap
{
    public Type SourceType { get; }
    public Type DestinationType { get; }
    public Dictionary<string, MemberConfig> Members { get; } = new(StringComparer.Ordinal);
    public Action<object, object>? BeforeMap { get; set; }
    public Action<object, object>? AfterMap { get; set; }

    public TypeMap(Type sourceType, Type destinationType)
    {
        SourceType = sourceType;
        DestinationType = destinationType;
    }

    public MemberConfig GetOrAddMember(string name)
    {
        if (!Members.TryGetValue(name, out var cfg))
        {
            cfg = new MemberConfig();
            Members[name] = cfg;
        }
        return cfg;
    }
}

internal sealed class MemberConfig
{
    public bool Ignored { get; set; }
    public Func<object, object?>? Resolver { get; set; }
}
