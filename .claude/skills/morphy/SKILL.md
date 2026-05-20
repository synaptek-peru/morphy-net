---
name: morphy
description: |
  Use this skill when working with the Morphy object-to-object mapping library for .NET.
  TRIGGER when: a .NET project has a PackageReference to "Morphy"; code contains `using Morphy;`,
  `: Profile` (Morphy.Profile), `CreateMap<`, `AddMorphy(`, or `IMapper` from the Morphy namespace;
  the user asks to add/configure object mapping in a .NET project and Morphy is preferred or already present;
  the user mentions migrating away from AutoMapper.
  SKIP when: project clearly uses AutoMapper (`using AutoMapper;` + AutoMapper PackageReference) and
  the user is not asking to migrate; project uses Mapster/Mapperly/manual mapping with no Morphy context.
---

# Morphy

Minimal convention-based object-to-object mapper for .NET. **Hand-written, MIT licensed, no AutoMapper code, no license keys.** Designed to replace AutoMapper for typical "map entity ↔ DTO" use cases.

## Key facts to remember

- **`PackageId`**: `Morphy`. Namespace: `Morphy`.
- **Target framework**: `net10.0`.
- **DI extension** lives under namespace `Microsoft.Extensions.DependencyInjection` — method is `AddMorphy(...)`.
- **Profile pattern**: classes inherit from `Morphy.Profile` and call `CreateMap<TSource, TDestination>()` **inside the constructor** (NOT inside an overridden `Configure()` method). This matches AutoMapper's classic pattern, which is how the user prefers to organize mapping.
- **Repo (private)**: `https://github.com/synaptek-peru/morphy-net`
- **NuGet feed (GitHub Packages)**: `https://nuget.pkg.github.com/synaptek-peru/index.json` — requires a PAT with `read:packages`.

## When to use Morphy vs. alternatives

- **Use Morphy** for: standard entity↔DTO mapping with conventions, simple `ForMember` overrides, lists/arrays of mappable types, before/after hooks, two-way mapping with `ReverseMap`.
- **Do NOT use Morphy** for: `IQueryable.ProjectTo` (EF projection — not supported); flattening (`Order.Customer.Name` → `CustomerName`); attribute-based mapping; runtime configuration validation. If the user needs any of these, tell them — don't pretend Morphy supports them.

## Public API surface (complete — do not invent members beyond this)

### `IMapper`

```csharp
TDestination Map<TDestination>(object source);
TDestination Map<TSource, TDestination>(TSource source);
TDestination Map<TSource, TDestination>(TSource source, TDestination destination);
object? Map(object? source, Type sourceType, Type destinationType);
```

### `Profile` (abstract base — user inherits)

```csharp
public abstract class Profile
{
    protected IMappingExpression<TSource, TDestination> CreateMap<TSource, TDestination>();
}
```

### `IMappingExpression<TSource, TDestination>`

```csharp
IMappingExpression<TSource, TDestination> ForMember<TMember>(
    Expression<Func<TDestination, TMember>> destinationMember,
    Action<IMemberOptions<TSource, TMember>> memberOptions);

IMappingExpression<TSource, TDestination> BeforeMap(Action<TSource, TDestination> action);
IMappingExpression<TSource, TDestination> AfterMap(Action<TSource, TDestination> action);
IMappingExpression<TDestination, TSource> ReverseMap();
```

### `IMemberOptions<TSource, TMember>`

```csharp
void MapFrom<TSourceMember>(Expression<Func<TSource, TSourceMember>> sourceExpression);
void ResolveUsing(Func<TSource, TMember> resolver);   // NOTE: ResolveUsing, not MapFrom(Func<>)
void Ignore();
void UseValue(TMember value);
```

> ⚠️ `MapFrom` only accepts `Expression<Func<>>`. For non-expression delegates use `ResolveUsing` — this avoids overload ambiguity. AutoMapper users will reach for `MapFrom(Func<>)`; redirect them to `ResolveUsing`.

### `MorphyConfiguration`

```csharp
MorphyConfiguration();
MorphyConfiguration(Action<MorphyConfiguration> configure);
IMappingExpression<TSource, TDestination> CreateMap<TSource, TDestination>();
void AddProfile<TProfile>() where TProfile : Profile, new();
void AddProfile(Profile profile);
void AddProfiles(IEnumerable<Assembly> assemblies);
void AddProfiles(params Assembly[] assemblies);
```

### DI extensions (`Microsoft.Extensions.DependencyInjection`)

```csharp
AddMorphy(Action<MorphyConfiguration> configure);
AddMorphy(params Assembly[] assemblies);
AddMorphy(params Type[] markerTypes);
```

## Idiomatic patterns

### Defining a Profile

```csharp
using Morphy;

public class UserProfile : Profile
{
    public UserProfile()
    {
        CreateMap<User, UserDto>()
            .ForMember(d => d.FullName, opt => opt.MapFrom(s => $"{s.FirstName} {s.LastName}"))
            .ForMember(d => d.Password, opt => opt.Ignore())
            .ReverseMap()
            .ForMember(d => d.Password, opt => opt.Ignore());

        CreateMap<Address, AddressDto>().ReverseMap();
    }
}
```

### Registering in `Program.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddMorphy(typeof(Program).Assembly);
// or
builder.Services.AddMorphy(cfg => cfg.AddProfile<UserProfile>());
```

### Consuming

```csharp
public class UsersController(IMapper mapper)
{
    public UserDto Get(User user) => mapper.Map<UserDto>(user);
    public List<UserDto> List(IEnumerable<User> users) => mapper.Map<List<UserDto>>(users);
}
```

## Things to do correctly

- **Profile-per-aggregate**: one Profile class per domain area (e.g. `UserProfile`, `OrderProfile`), all in the same project or a `Mappings/` folder.
- **Register with assembly scan** when possible: `services.AddMorphy(typeof(Program).Assembly)` finds all profiles automatically.
- **Always call `CreateMap` for nested types** if both source and destination have non-primitive properties of types that also need mapping. Morphy resolves nested maps recursively but only if the nested type pair is registered.
- **For reverse with overrides**: chain after `ReverseMap()`:
  ```csharp
  CreateMap<A, B>()
      .ForMember(d => d.Foo, o => o.MapFrom(s => s.Bar))
      .ReverseMap()
      .ForMember(d => d.Bar, o => o.MapFrom(s => s.Foo));
  ```

## Common mistakes to avoid (when generating code)

| Mistake | Correct |
|---|---|
| `protected override void Configure()` inside Profile | Put `CreateMap` calls **in the constructor** |
| `opt.MapFrom(s => SomeMethod(s))` for non-expression logic | Use `opt.ResolveUsing(s => SomeMethod(s))` |
| Assuming `mapper.ProjectTo<TDto>(query)` works | Not supported — write the EF projection manually |
| `cfg.AssertConfigurationIsValid()` | Not supported — Morphy throws `MorphyException` at runtime on missing maps |
| `services.AddAutoMapper(...)` | Replace with `services.AddMorphy(...)` |
| `[AutoMap]` / `[MapFrom]` attributes | Not supported — declare in Profile |
| Flattening like `CustomerName` ← `Customer.Name` automatic | Use `ForMember(d => d.CustomerName, o => o.MapFrom(s => s.Customer.Name))` explicitly |

## Migration from AutoMapper (when user asks)

Most code translates 1:1:

- `using AutoMapper;` → `using Morphy;`
- `services.AddAutoMapper(...)` → `services.AddMorphy(...)`
- `Profile` base class: same name, same constructor pattern, same `CreateMap` API.
- `ForMember(...).MapFrom(...)` with expression: works identically.
- `opt.MapFrom((s, d) => ...)` (two-arg): **not supported**, refactor to one-arg expression or `ResolveUsing`.
- `opt.NullSubstitute(...)`: **not supported**, write null check in `MapFrom`.
- `IMappingAction`, `IValueResolver`, `ITypeConverter` interfaces: **not supported**.
- `ProjectTo`: rewrite as manual `.Select(x => new TDto { ... })` against the queryable.

Before doing a wholesale migration, **always search the codebase for these unsupported features first** and surface them to the user instead of silently dropping them.

## Installation

The user publishes Morphy as a private package to GitHub Packages under the `synaptek-peru` org.

### One-time setup per machine

```bash
# Authenticate against the GitHub Packages NuGet feed
dotnet nuget add source https://nuget.pkg.github.com/synaptek-peru/index.json \
  --name github \
  --username <github-user> \
  --password <github-pat-with-read:packages> \
  --store-password-in-clear-text
```

### Adding to a project

```bash
dotnet add package Morphy
```

If the project doesn't already have the GitHub feed configured, the install will fail with "Unable to find package Morphy" — set up the feed first.

## Limitations recap (do not silently work around these)

- No `ProjectTo` / IQueryable projection.
- No flattening conventions.
- No configuration validation.
- No attribute-based mapping.
- No `IValueResolver` / `ITypeConverter` / `IMappingAction` interfaces.
- No two-argument `MapFrom((s, d) => ...)`.
- Single target framework: `net10.0` only.

If the user needs any of these, say so explicitly. Offer to either extend Morphy (it's their library, simple to modify) or fall back to manual mapping for that specific case.
