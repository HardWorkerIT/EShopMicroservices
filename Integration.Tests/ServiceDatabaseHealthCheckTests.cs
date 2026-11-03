using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;
using CustomerContext = Customer.Microservice.Data.ApplicationDbContext;
using OrderContext = Order.Microservice.Data.ApplicationDbContext;
using ProductContext = Product.Microservice.Data.ApplicationDbContext;

public sealed class ServiceDatabaseHealthCheckTests
{
    [Fact]
    public async Task Each_service_database_health_check_reports_a_reachable_database_as_healthy()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var customerOptions = new DbContextOptionsBuilder<CustomerContext>().UseSqlite(connection).Options;
        var orderOptions = new DbContextOptionsBuilder<OrderContext>().UseSqlite(connection).Options;
        var productOptions = new DbContextOptionsBuilder<ProductContext>().UseSqlite(connection).Options;
        await using var customer = new CustomerContext(customerOptions);
        await using var order = new OrderContext(orderOptions);
        await using var product = new ProductContext(productOptions);
        await customer.Database.EnsureCreatedAsync();
        await order.Database.EnsureCreatedAsync();
        await product.Database.EnsureCreatedAsync();

        var results = new[]
        {
            await new Customer.Microservice.HealthChecks.DatabaseHealthCheck(customer)
                .CheckHealthAsync(new HealthCheckContext(), CancellationToken.None),
            await new Order.Microservice.HealthChecks.DatabaseHealthCheck(order)
                .CheckHealthAsync(new HealthCheckContext(), CancellationToken.None),
            await new Product.Microservice.HealthChecks.DatabaseHealthCheck(product)
                .CheckHealthAsync(new HealthCheckContext(), CancellationToken.None)
        };

        Assert.All(results, result => Assert.Equal(HealthStatus.Healthy, result.Status));
    }
}
