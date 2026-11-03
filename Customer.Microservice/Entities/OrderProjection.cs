using System;
using System.ComponentModel.DataAnnotations;

namespace Customer.Microservice.Entities;

public sealed class OrderProjection
{
    [Key]
    public Guid EventId { get; set; }
    public long OrderId { get; set; }
    public long CustomerId { get; set; }
    public long ProductId { get; set; }
    public int Quantity { get; set; }
    public DateTime OccurredUtc { get; set; }
}
