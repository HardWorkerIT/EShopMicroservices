using Customer.Microservice.Entities;
using Order.Microservice.Enums;
using System;
using System.ComponentModel.DataAnnotations;

namespace Order.Microservice.Entities;

public class Order : BaseEntity
{
    public long ProductId { get; set; }
    public long CustomerId { get; set; }
    public Guid InventoryReservationId { get; set; } = Guid.NewGuid();
    [Range(1, 10000)]
    public int Quantity { get; set; } = 1;
    public DateTime Date { get; set; }
    [Range(0, 3)]
    public int OrderStatus { get; set; }
}
