# Morphy

Minimal convention-based object-to-object mapper for .NET. Built from scratch — MIT licensed, no AutoMapper code, no license keys.

## Features

- Mapeo por convención (mismos nombres de propiedad)
- **Records posicionales y clases con constructor** (construye vía el ctor público de más parámetros, matching por nombre case-insensitive)
- `Profile` con `CreateMap<TSource, TDestination>()`
- `ForMember` + `MapFrom` / `Ignore` / `UseValue` / `ResolveUsing`
- `ReverseMap()` para mapeo en ambas direcciones
- `BeforeMap` / `AfterMap`
- Colecciones (`List<T>`, `IList<T>`, `IEnumerable<T>`, arrays)
- Tipos anidados (resuelve recursivo si hay un mapa registrado)
- Conversiones primitivas (`enum` ↔ `string`, `IConvertible`)
- Integración con `Microsoft.Extensions.DependencyInjection` (`AddMorphy`)

## Instalación

Por ahora referencia directa al `.csproj`:

```xml
<ProjectReference Include="..\Morphy\src\Morphy\Morphy.csproj" />
```

## Uso

### 1. Define un Profile

```csharp
using Morphy;

public class UserProfile : Profile
{
    public UserProfile()
    {
        CreateMap<User, UserDto>()
            .ForMember(d => d.FullName, opt => opt.MapFrom(s => $"{s.FirstName} {s.LastName}"))
            .ForMember(d => d.Password, opt => opt.Ignore())
            .AfterMap((s, d) => d.AuditNote = "mapped");

        CreateMap<Address, AddressDto>().ReverseMap();
    }
}
```

### 2. Regístralo en `Program.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;

// Escanea el assembly por todos los Profile
builder.Services.AddMorphy(typeof(Program).Assembly);

// O configuración inline
builder.Services.AddMorphy(cfg =>
{
    cfg.AddProfile<UserProfile>();
    cfg.CreateMap<Foo, FooDto>();
});

// O con marker types
builder.Services.AddMorphy(typeof(UserProfile), typeof(SomeOtherProfile));
```

### 3. Inyecta `IMapper` y úsalo

```csharp
public class UsersController(IMapper mapper)
{
    public UserDto Get(User user) => mapper.Map<UserDto>(user);
}
```

## API de `IMemberOptions`

| Método | Qué hace |
|---|---|
| `MapFrom(Expression<Func<TSource, T>>)` | Mapea desde una expresión (preferido) |
| `ResolveUsing(Func<TSource, T>)` | Resuelve con un delegate normal (lógica más libre) |
| `Ignore()` | Salta esta propiedad |
| `UseValue(T)` | Asigna un valor fijo |

## Limitaciones (a propósito)

- No hay `ProjectTo` (IQueryable). Si lo necesitas, usa EF projection manual.
- No valida configuración (`AssertConfigurationIsValid`). Si falta un map, lanza `MorphyException` en runtime.
- No hay value converters, value resolvers como clases, ni dependency injection dentro de resolvers.
- Sin soporte para flattening (`Order.Customer.Name` → `CustomerName` automático).

Si necesitas algo de esto, abre un issue o lo agregamos a mano.

## Licencia

MIT. Hecho desde cero, sin código derivado de AutoMapper.
