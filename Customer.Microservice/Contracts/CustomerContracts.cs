using System.ComponentModel.DataAnnotations;

namespace Customer.Microservice.Contracts;

public sealed record CustomerResponse(int Id, string Name, string Contact, string City, string Email);

public sealed class CustomerWriteRequest
{
    [Required, StringLength(100)]
    public string Name { get; init; }
    [StringLength(50)]
    public string Contact { get; init; }
    [StringLength(100)]
    public string City { get; init; }
    [EmailAddress, StringLength(254)]
    public string Email { get; init; }
}
