using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Customer.Microservice.Events;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using CustomerContext = Customer.Microservice.Data.ApplicationDbContext;

public sealed class CustomerProjectionTests
{
    [Fact]
    public async Task Repeated_order_event_delivery_creates_only_one_customer_projection()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CustomerContext>().UseSqlite(connection).Options;
        await using var context = new CustomerContext(options);
        await context.Database.EnsureCreatedAsync();
        var handler = new OrderEventProjectionHandler(context);
        var eventId = Guid.NewGuid();
        var occurredUtc = DateTime.UtcNow;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            EventId = eventId,
            OrderId = 123,
            ProductId = 45,
            CustomerId = 67,
            Quantity = 2,
            OccurredUtc = occurredUtc
        });

        await handler.HandleAsync(payload, CancellationToken.None);
        await handler.HandleAsync(payload, CancellationToken.None);

        var projection = await context.OrderProjections.SingleAsync();
        Assert.Equal(eventId, projection.EventId);
        Assert.Equal(123, projection.OrderId);
        Assert.Equal(45, projection.ProductId);
        Assert.Equal(67, projection.CustomerId);
        Assert.Equal(2, projection.Quantity);
        Assert.Equal(occurredUtc, projection.OccurredUtc);
    }
}
