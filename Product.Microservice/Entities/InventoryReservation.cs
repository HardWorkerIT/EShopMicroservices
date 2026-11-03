using System;

namespace Product.Microservice.Entities;

public sealed class InventoryReservation
{
    public Guid Id { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public bool IsReleased { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
