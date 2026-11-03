using System;

namespace Order.Microservice.Entities;

public enum InventoryReservationAttemptStatus
{
    Pending = 0,
    Committed = 1,
    Compensated = 2
}

public sealed class InventoryReservationAttempt
{
    public Guid Id { get; set; }
    public long ProductId { get; set; }
    public long CustomerId { get; set; }
    public int Quantity { get; set; }
    public InventoryReservationAttemptStatus Status { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public int Attempts { get; set; }
    public string LastError { get; set; }
}
