using System;

namespace Order.Microservice.Entities;

public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; }
    public string Payload { get; set; }
    public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedUtc { get; set; }
    public int Attempts { get; set; }
    public string LastError { get; set; }
}
