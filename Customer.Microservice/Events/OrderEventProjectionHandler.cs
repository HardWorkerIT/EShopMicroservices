using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Customer.Microservice.Data;
using Customer.Microservice.Entities;
using Microsoft.EntityFrameworkCore;

namespace Customer.Microservice.Events;

public sealed class OrderEventProjectionHandler
{
    private readonly ApplicationDbContext _db;

    public OrderEventProjectionHandler(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var message = JsonSerializer.Deserialize<OrderCreatedEvent>(payload.Span);
        if (message is null) throw new JsonException("Order event payload is empty.");

        if (await _db.OrderProjections.AnyAsync(item => item.EventId == message.EventId, cancellationToken))
            return;

        _db.OrderProjections.Add(new OrderProjection
        {
            EventId = message.EventId,
            OrderId = message.OrderId,
            CustomerId = message.CustomerId,
            ProductId = message.ProductId,
            Quantity = message.Quantity,
            OccurredUtc = message.OccurredUtc
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    private sealed record OrderCreatedEvent(Guid EventId, int OrderId, long ProductId, long CustomerId, int Quantity, DateTime OccurredUtc);
}
