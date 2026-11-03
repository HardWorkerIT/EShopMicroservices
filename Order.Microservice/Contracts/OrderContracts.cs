using System;
using System.ComponentModel.DataAnnotations;

namespace Order.Microservice.Contracts;

public sealed record OrderResponse(int Id, long ProductId, long CustomerId, int Quantity, DateTime Date, int OrderStatus);

public sealed class OrderWriteRequest
{
    [Range(1, long.MaxValue)]
    public long ProductId { get; init; }
    [Range(1, long.MaxValue)]
    public long CustomerId { get; init; }
    [Range(1, 10000)]
    public int Quantity { get; init; } = 1;
    public DateTime Date { get; init; }
    [Range(0, 3)]
    public int OrderStatus { get; init; }
}

public sealed class UpdateOrderStatusRequest
{
    [Range(0, 3)]
    public int OrderStatus { get; init; }
}
