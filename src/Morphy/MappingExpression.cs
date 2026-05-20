using System.Linq.Expressions;

namespace Morphy;

public interface IMappingExpression<TSource, TDestination>
{
    IMappingExpression<TSource, TDestination> ForMember<TMember>(
        Expression<Func<TDestination, TMember>> destinationMember,
        Action<IMemberOptions<TSource, TMember>> memberOptions);

    IMappingExpression<TSource, TDestination> BeforeMap(Action<TSource, TDestination> action);
    IMappingExpression<TSource, TDestination> AfterMap(Action<TSource, TDestination> action);
    IMappingExpression<TDestination, TSource> ReverseMap();
}

public interface IMemberOptions<TSource, TMember>
{
    void MapFrom<TSourceMember>(Expression<Func<TSource, TSourceMember>> sourceExpression);
    void ResolveUsing(Func<TSource, TMember> resolver);
    void Ignore();
    void UseValue(TMember value);
}

internal sealed class MappingExpression<TSource, TDestination> : IMappingExpression<TSource, TDestination>
{
    private readonly TypeMap _typeMap;
    private readonly Profile _profile;

    public MappingExpression(TypeMap typeMap, Profile profile)
    {
        _typeMap = typeMap;
        _profile = profile;
    }

    public IMappingExpression<TSource, TDestination> ForMember<TMember>(
        Expression<Func<TDestination, TMember>> destinationMember,
        Action<IMemberOptions<TSource, TMember>> memberOptions)
    {
        var memberName = GetMemberName(destinationMember)
            ?? throw new MorphyException($"ForMember must point to a property or field of {typeof(TDestination).Name}.");

        var cfg = _typeMap.GetOrAddMember(memberName);
        memberOptions(new MemberOptions<TMember>(cfg));
        return this;
    }

    public IMappingExpression<TSource, TDestination> BeforeMap(Action<TSource, TDestination> action)
    {
        _typeMap.BeforeMap = (s, d) => action((TSource)s, (TDestination)d);
        return this;
    }

    public IMappingExpression<TSource, TDestination> AfterMap(Action<TSource, TDestination> action)
    {
        _typeMap.AfterMap = (s, d) => action((TSource)s, (TDestination)d);
        return this;
    }

    public IMappingExpression<TDestination, TSource> ReverseMap()
    {
        var reverse = _profile.CreateReverseMap(typeof(TDestination), typeof(TSource));
        return new MappingExpression<TDestination, TSource>(reverse, _profile);
    }

    private static string? GetMemberName<TMember>(Expression<Func<TDestination, TMember>> expression)
    {
        var body = expression.Body;
        if (body is UnaryExpression unary) body = unary.Operand;
        return body is MemberExpression member ? member.Member.Name : null;
    }

    private sealed class MemberOptions<TMember> : IMemberOptions<TSource, TMember>
    {
        private readonly MemberConfig _config;
        public MemberOptions(MemberConfig config) { _config = config; }

        public void MapFrom<TSourceMember>(Expression<Func<TSource, TSourceMember>> sourceExpression)
        {
            var compiled = sourceExpression.Compile();
            _config.Resolver = source => compiled((TSource)source);
        }

        public void ResolveUsing(Func<TSource, TMember> resolver)
        {
            _config.Resolver = source => resolver((TSource)source);
        }

        public void Ignore() => _config.Ignored = true;

        public void UseValue(TMember value)
        {
            _config.Resolver = _ => value;
        }
    }
}
