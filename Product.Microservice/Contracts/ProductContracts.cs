using System.ComponentModel.DataAnnotations;
using System;

namespace Product.Microservice.Contracts;

public sealed record ProductResponse(int Id, string Name, int Rate, int InventoryCount);

public sealed class InventoryReservationRequest
{
    public Guid ReservationId { get; init; }

    [Range(1, 10000)]
    public int Quantity { get; init; }
}

public sealed class ProductWriteRequest
{
    [Required, StringLength(150)]
    public string Name { get; init; }

    [Range(0, int.MaxValue)]
    public int Rate { get; init; }

    [Range(0, int.MaxValue)]
    public int InventoryCount { get; init; }
}
