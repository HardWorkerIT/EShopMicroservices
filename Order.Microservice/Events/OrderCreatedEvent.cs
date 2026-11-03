using System;

namespace Order.Microservice.Events;

public sealed record OrderCreatedEvent(Guid EventId, int OrderId, long ProductId, long CustomerId, int Quantity, DateTime OccurredUtc);
