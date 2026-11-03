using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Customer.Microservice.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Order.Microservice.Clients;
using Order.Microservice.Contracts;
using Product.Microservice.Contracts;
using Product.Microservice.Services;
using Xunit;
using CustomerContext = Customer.Microservice.Data.ApplicationDbContext;
using CustomerEntity = Customer.Microservice.Entities.Customer;
using OrderContext = Order.Microservice.Data.ApplicationDbContext;
using OrderEntity = Order.Microservice.Entities.Order;
using ProductContext = Product.Microservice.Data.ApplicationDbContext;
using ProductEntity = Product.Microservice.Entities.Product;
using ReservationAttempt = Order.Microservice.Entities.InventoryReservationAttempt;
using ReservationAttemptStatus = Order.Microservice.Entities.InventoryReservationAttemptStatus;

public class CollectionQueryTests
{
    [Fact]
    public async Task Customer_list_applies_allow_listed_sort_and_page()
    {
        await using var connection = await OpenDatabase();
        var options = new DbContextOptionsBuilder<CustomerContext>().UseSqlite(connection).Options;
        await using var context = new CustomerContext(options);
        await context.Database.EnsureCreatedAsync();
        context.Customers.AddRange(new CustomerEntity { Name = "Gamma" }, new CustomerEntity { Name = "Alpha" }, new CustomerEntity { Name = "Beta" });
        await context.SaveChangesAsync();
        var controller = new CustomerController(context);

        var result = Assert.IsType<OkObjectResult>(await controller.GetAll(page: 2, pageSize: 1, sortBy: "name"));
        var items = GetProperty<List<Customer.Microservice.Contracts.CustomerResponse>>(result.Value, "items");

        Assert.Equal("Beta", Assert.Single(items).Name);
    }

    [Fact]
    public async Task Product_catalog_filters_prefix_and_rate_then_sorts()
    {
        await using var connection = await OpenDatabase();
        var options = new DbContextOptionsBuilder<ProductContext>().UseSqlite(connection).Options;
        await using var context = new ProductContext(options);
        await context.Database.EnsureCreatedAsync();
        context.Products.AddRange(
            new ProductEntity { Name = "Phone case", Rate = 50 },
            new ProductEntity { Name = "Phone pro", Rate = 200 },
            new ProductEntity { Name = "Tablet", Rate = 300 });
        await context.SaveChangesAsync();
        var controller = new Product.Microservice.Controllers.ProductController(
            context, new ConfigurationBuilder().Build(), new InventoryService(context));

        var result = Assert.IsType<OkObjectResult>(await controller.GetAll(
            page: 1, pageSize: 10, minRate: 100, name: "Phone", sortBy: "rate", descending: true));
        var items = GetProperty<List<ProductResponse>>(result.Value, "items");

        Assert.Equal("Phone pro", Assert.Single(items).Name);
        Assert.Equal(1, GetProperty<int>(result.Value, "totalCount"));
    }

    [Fact]
    public async Task Order_list_sorts_only_the_authenticated_customers_orders()
    {
        await using var connection = await OpenDatabase();
        var options = new DbContextOptionsBuilder<OrderContext>().UseSqlite(connection).Options;
        await using var context = new OrderContext(options);
        await context.Database.EnsureCreatedAsync();
        context.Orders.AddRange(
            new OrderEntity { CustomerId = 42, ProductId = 1, Date = new DateTime(2025, 1, 1) },
            new OrderEntity { CustomerId = 42, ProductId = 2, Date = new DateTime(2025, 2, 1) },
            new OrderEntity { CustomerId = 7, ProductId = 3, Date = new DateTime(2025, 3, 1) });
        await context.SaveChangesAsync();
        var controller = new OrderController(context, new NoopInventoryClient());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("customer_id", "42") }, "test"))
            }
        };

        var result = Assert.IsType<OkObjectResult>(await controller.GetAll(sortBy: "date", descending: true));
        var items = GetProperty<List<OrderResponse>>(result.Value, "items");

        Assert.Equal(2, items.Count);
        Assert.True(items[0].Date > items[1].Date);
        Assert.All(items, item => Assert.Equal(42, item.CustomerId));
    }

    [Fact]
    public async Task Creating_an_order_persists_its_outbox_event_with_the_order()
    {
        await using var connection = await OpenDatabase();
        var options = new DbContextOptionsBuilder<OrderContext>().UseSqlite(connection).Options;
        await using var context = new OrderContext(options);
        await context.Database.EnsureCreatedAsync();
        var inventory = new NoopInventoryClient();
        var controller = CreateOrderController(context, inventory, customerId: 42);

        var result = Assert.IsType<CreatedAtActionResult>(await controller.Create(
            new OrderWriteRequest { ProductId = 9, CustomerId = 42, Quantity = 2, Date = new DateTime(2026, 11, 2) },
            CancellationToken.None));

        Assert.Equal(1, inventory.ReserveCalls);
        Assert.Equal(1, await context.Orders.CountAsync());
        Assert.Equal(Order.Microservice.Entities.InventoryReservationAttemptStatus.Committed,
            (await context.InventoryReservationAttempts.SingleAsync()).Status);
        var outboxMessage = await context.OutboxMessages.SingleAsync();
        Assert.Equal("orders.created.v1", outboxMessage.Type);
        using var eventDocument = JsonDocument.Parse(outboxMessage.Payload);
        Assert.Equal(Convert.ToInt32(result.RouteValues["id"]), eventDocument.RootElement.GetProperty("OrderId").GetInt32());
    }

    [Fact]
    public async Task Creating_an_order_for_another_customer_is_forbidden_before_reserving_stock()
    {
        await using var connection = await OpenDatabase();
        var options = new DbContextOptionsBuilder<OrderContext>().UseSqlite(connection).Options;
        await using var context = new OrderContext(options);
        await context.Database.EnsureCreatedAsync();
        var inventory = new NoopInventoryClient();
        var controller = CreateOrderController(context, inventory, customerId: 42);

        var result = await controller.Create(
            new OrderWriteRequest { ProductId = 9, CustomerId = 7, Quantity = 1, Date = DateTime.UtcNow },
            CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        Assert.Equal(0, inventory.ReserveCalls);
        Assert.Empty(await context.Orders.ToListAsync());
        Assert.Empty(await context.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task Cancelled_order_transaction_releases_previously_reserved_stock()
    {
        await using var connection = await OpenDatabase();
        var options = new DbContextOptionsBuilder<OrderContext>().UseSqlite(connection).Options;
        await using var context = new OrderContext(options);
        await context.Database.EnsureCreatedAsync();
        var inventory = new NoopInventoryClient();
        var controller = CreateOrderController(context, inventory, customerId: 42);
        using var cancellation = new CancellationTokenSource();
        inventory.CancelOnReserve = cancellation;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.Create(
            new OrderWriteRequest { ProductId = 9, CustomerId = 42, Quantity = 1, Date = DateTime.UtcNow },
            cancellation.Token));

        Assert.Equal(1, inventory.ReserveCalls);
        Assert.Equal(1, inventory.ReleaseCalls);
        Assert.Empty(await context.Orders.ToListAsync());
        Assert.Empty(await context.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task Reservation_recovery_releases_stale_pending_intents_and_marks_them_compensated()
    {
        await using var connection = await OpenDatabase();
        var options = new DbContextOptionsBuilder<OrderContext>().UseSqlite(connection).Options;
        await using var context = new OrderContext(options);
        await context.Database.EnsureCreatedAsync();
        var reservationId = Guid.NewGuid();
        context.InventoryReservationAttempts.Add(new ReservationAttempt
        {
            Id = reservationId,
            ProductId = 9,
            CustomerId = 42,
            Quantity = 2,
            UpdatedUtc = DateTime.UtcNow.AddMinutes(-10)
        });
        await context.SaveChangesAsync();
        var inventory = new NoopInventoryClient();

        var recovered = await new Order.Microservice.Events.InventoryReservationRecoveryService(context, inventory)
            .RecoverStaleReservationsAsync(DateTime.UtcNow.AddMinutes(-5), CancellationToken.None);

        Assert.Equal(1, recovered);
        Assert.Equal(reservationId, inventory.LastReleasedReservationId);
        var attempt = await context.InventoryReservationAttempts.SingleAsync();
        Assert.Equal(ReservationAttemptStatus.Compensated, attempt.Status);
        Assert.Equal(1, attempt.Attempts);
    }

    [Fact]
    public async Task Customer_list_rejects_page_offsets_that_would_overflow()
    {
        var options = new DbContextOptionsBuilder<CustomerContext>()
            .UseSqlite("Data Source=:memory:").Options;
        await using var context = new CustomerContext(options);
        var controller = new CustomerController(context);

        Assert.IsType<BadRequestObjectResult>(await controller.GetAll(page: int.MaxValue, pageSize: 100));
    }

    private static async Task<SqliteConnection> OpenDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static T GetProperty<T>(object value, string name)
        => (T)value.GetType().GetProperty(name).GetValue(value);

    private static OrderController CreateOrderController(OrderContext context, NoopInventoryClient inventory, long customerId)
    {
        var controller = new OrderController(context, inventory);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("customer_id", customerId.ToString()) }, "test"))
            }
        };
        return controller;
    }

    private sealed class NoopInventoryClient : IProductInventoryClient
    {
        public int ReserveCalls { get; private set; }
        public int ReleaseCalls { get; private set; }
        public Guid LastReleasedReservationId { get; private set; }
        public CancellationTokenSource CancelOnReserve { get; set; }
        public Task<HttpStatusCode> ReserveAsync(long productId, int quantity, Guid reservationId, CancellationToken cancellationToken)
        {
            ReserveCalls++;
            CancelOnReserve?.Cancel();
            return Task.FromResult(HttpStatusCode.NoContent);
        }
        public Task ReleaseAsync(long productId, int quantity, Guid reservationId, CancellationToken cancellationToken)
        {
            ReleaseCalls++;
            LastReleasedReservationId = reservationId;
            return Task.CompletedTask;
        }
    }
}
