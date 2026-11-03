using Microsoft.EntityFrameworkCore;
using Order.Microservice.Enums;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace Order.Microservice.Data
{
    public class ApplicationDbContext : DbContext, IApplicationDbContext
    {

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public async Task InitializeDatabaseAsync(bool seedDemoData, CancellationToken cancellationToken, string migrationConnectionString = null)
        {
            var runtimeConnectionString = Database.GetConnectionString();
            if (!string.IsNullOrWhiteSpace(migrationConnectionString))
                Database.SetConnectionString(migrationConnectionString);
            try
            {
                await Database.MigrateAsync(cancellationToken);
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(migrationConnectionString))
                    Database.SetConnectionString(runtimeConnectionString);
            }

            if (!seedDemoData) return;

            if (await Orders.AnyAsync(cancellationToken) is false)
            {
                Orders.Add(new Entities.Order
                {
                    CustomerId = 1,
                    ProductId = 1,
                    InventoryReservationId = Guid.NewGuid(),
                    Date = DateTime.Now,
                    OrderStatus = (int)OrderStatus.InProgress
                });

                Orders.Add(new Entities.Order
                {
                    CustomerId = 1,
                    ProductId = 2,
                    InventoryReservationId = Guid.NewGuid(),
                    Date = DateTime.Now,
                    OrderStatus = (int)OrderStatus.InProgress
                });

                Orders.Add(new Entities.Order
                {
                    CustomerId = 1,
                    ProductId = 3,
                    InventoryReservationId = Guid.NewGuid(),
                    Date = DateTime.Now,
                    OrderStatus = (int)OrderStatus.InProgress
                });

                await base.SaveChangesAsync(cancellationToken);
            }
        }

        public DbSet<Entities.Order> Orders { get; set; }
        public DbSet<Entities.OutboxMessage> OutboxMessages { get; set; }
        public DbSet<Entities.InventoryReservationAttempt> InventoryReservationAttempts { get; set; }

        public Task<int> SaveChangesAsync()
        {
            return base.SaveChangesAsync();
        }
    }
}
