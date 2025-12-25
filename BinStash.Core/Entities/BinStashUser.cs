using Microsoft.AspNetCore.Identity;

namespace BinStash.Core.Entities;

public class BinStashUser : IdentityUser<Guid>
{
    public string FirstName { get; set; } = null!;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = null!;
}