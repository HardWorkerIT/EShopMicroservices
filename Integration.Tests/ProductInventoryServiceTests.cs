using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Product.Microservice.Data;
using Product.Microservice.Services;
using Xunit;

public class ProductInventoryServiceTests
{
    [Fact]
    public async Task Reservations_do_not_oversell_stock()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;

        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var product = new Product.Microservice.Entities.Product { Name = "Keyboard", Rate = 25, InventoryCount = 2 };
        context.Products.Add(product);
        await context.SaveChangesAsync();
        var inventory = new InventoryService(context);
        var firstReservationId = System.Guid.NewGuid();
        var secondReservationId = System.Guid.NewGuid();

        Assert.Equal(InventoryOperationResult.Success, await inventory.ReserveAsync(product.Id, 1, firstReservationId, CancellationToken.None));
        Assert.Equal(InventoryOperationResult.Success, await inventory.ReserveAsync(product.Id, 1, secondReservationId, CancellationToken.None));
        Assert.Equal(InventoryOperationResult.InsufficientInventory, await inventory.ReserveAsync(product.Id, 1, System.Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(0, await context.Products.Where(item => item.Id == product.Id).Select(item => item.InventoryCount).SingleAsync());
    }

    [Fact]
    public async Task Reservation_for_a_missing_product_returns_not_found()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var result = await new InventoryService(context).ReserveAsync(404, 1, System.Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(InventoryOperationResult.ProductNotFound, result);
    }

    [Fact]
    public async Task Reserve_and_release_are_idempotent_for_the_same_reservation_id()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var product = new Product.Microservice.Entities.Product { Name = "Mouse", Rate = 10, InventoryCount = 2 };
        context.Products.Add(product);
        await context.SaveChangesAsync();
        var service = new InventoryService(context);
        var reservationId = System.Guid.NewGuid();

        Assert.Equal(InventoryOperationResult.Success, await service.ReserveAsync(product.Id, 1, reservationId, CancellationToken.None));
        Assert.Equal(InventoryOperationResult.Success, await service.ReserveAsync(product.Id, 1, reservationId, CancellationToken.None));
        Assert.Equal(1, await context.Products.Where(item => item.Id == product.Id).Select(item => item.InventoryCount).SingleAsync());

        Assert.Equal(InventoryOperationResult.Success, await service.ReleaseAsync(product.Id, 1, reservationId, CancellationToken.None));
        Assert.Equal(InventoryOperationResult.Success, await service.ReleaseAsync(product.Id, 1, reservationId, CancellationToken.None));
        Assert.Equal(2, await context.Products.Where(item => item.Id == product.Id).Select(item => item.InventoryCount).SingleAsync());
        Assert.Equal(InventoryOperationResult.ReservationConflict, await service.ReserveAsync(product.Id, 1, reservationId, CancellationToken.None));
    }

    [Fact]
    public async Task Releasing_an_unknown_reservation_creates_a_tombstone_against_late_reserve()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var service = new InventoryService(context);
        var reservationId = System.Guid.NewGuid();

        Assert.Equal(InventoryOperationResult.Success, await service.ReleaseAsync(404, 2, reservationId, CancellationToken.None));
        Assert.Equal(InventoryOperationResult.Success, await service.ReleaseAsync(404, 2, reservationId, CancellationToken.None));
        Assert.Equal(InventoryOperationResult.ReservationConflict, await service.ReserveAsync(404, 2, reservationId, CancellationToken.None));
    }
}
