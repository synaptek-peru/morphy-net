using Microsoft.Extensions.DependencyInjection;
using Morphy;

var services = new ServiceCollection();
services.AddMorphy(typeof(Program).Assembly);
var sp = services.BuildServiceProvider();
var mapper = sp.GetRequiredService<IMapper>();

int failed = 0;
void Assert(bool condition, string label)
{
    if (condition) Console.WriteLine($"OK   {label}");
    else { Console.WriteLine($"FAIL {label}"); failed++; }
}

var user = new User
{
    Id = 7,
    FirstName = "Ada",
    LastName = "Lovelace",
    Password = "secret",
    Role = Role.Admin,
    Address = new Address { City = "London", Zip = "W1" },
    Tags = new List<string> { "math", "history" }
};

var dto = mapper.Map<UserDto>(user);
Assert(dto.Id == 7, "convention Id");
Assert(dto.FullName == "Ada Lovelace", "ForMember.MapFrom expression");
Assert(dto.Password is null, "Ignore");
Assert(dto.Role == "Admin", "enum -> string");
Assert(dto.Address!.City == "London", "nested object via registered map");
Assert(dto.Tags.Count == 2 && dto.Tags[0] == "math", "List<string> collection");
Assert(dto.AuditNote == "mapped", "AfterMap");

var back = mapper.Map<User>(dto);
Assert(back.FirstName == "Ada" && back.LastName == "Lovelace", "ReverseMap with custom split");
Assert(back.Role == Role.Admin, "string -> enum (reverse)");

var users = new[] { user, new User { Id = 2, FirstName = "Grace", LastName = "Hopper" } };
var dtos = mapper.Map<List<UserDto>>(users);
Assert(dtos.Count == 2 && dtos[1].FullName == "Grace Hopper", "array -> List<T>");

Console.WriteLine(failed == 0 ? "\nALL PASSED" : $"\n{failed} TEST(S) FAILED");
return failed;

public enum Role { None, Admin, Guest }

public class User
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string? Password { get; set; }
    public Role Role { get; set; }
    public Address? Address { get; set; }
    public List<string> Tags { get; set; } = new();
}

public class Address
{
    public string City { get; set; } = "";
    public string Zip { get; set; } = "";
}

public class UserDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string? Password { get; set; }
    public string Role { get; set; } = "";
    public AddressDto? Address { get; set; }
    public List<string> Tags { get; set; } = new();
    public string? AuditNote { get; set; }
}

public class AddressDto
{
    public string City { get; set; } = "";
    public string Zip { get; set; } = "";
}

public class UserProfile : Profile
{
    public UserProfile()
    {
        CreateMap<Address, AddressDto>().ReverseMap();

        CreateMap<User, UserDto>()
            .ForMember(d => d.FullName, opt => opt.MapFrom(s => $"{s.FirstName} {s.LastName}"))
            .ForMember(d => d.Password, opt => opt.Ignore())
            .AfterMap((s, d) => d.AuditNote = "mapped")
            .ReverseMap()
            .ForMember(d => d.FirstName, opt => opt.MapFrom(s => s.FullName.Split(' ')[0]))
            .ForMember(d => d.LastName, opt => opt.MapFrom(s => s.FullName.Split(' ')[1]))
            .ForMember(d => d.Password, opt => opt.Ignore());
    }
}
