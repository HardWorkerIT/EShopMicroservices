using System.ComponentModel.DataAnnotations;

namespace Customer.Microservice.Entities;

public class Customer : BaseEntity
{
    [Required, StringLength(100)]
    public string Name { get; set; }
    [StringLength(50)]
    public string Contact { get; set; }
    [StringLength(100)]
    public string City { get; set; }
    [EmailAddress, StringLength(254)]
    public string Email { get; set; }
}
