using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Product.Microservice.Entities;

[Index(nameof(Name))]
public class Product : BaseEntity
{
    [Required, StringLength(150)]
    public string Name { get; set; }
    [Range(0, int.MaxValue)]
    public int Rate { get; set; }

    [Range(0, int.MaxValue)]
    public int InventoryCount { get; set; }
}
