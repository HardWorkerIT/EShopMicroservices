using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace Product.Microservice.Data
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

            if (await Products.AnyAsync(cancellationToken) is false)
            {
                Products.Add(new Entities.Product
                {
                    Name = "Computer 1",
                    Rate = 1000,
                    InventoryCount = 100
                });

                Products.Add(new Entities.Product
                {
                    Name = "Computer 2",
                    Rate = 1500,
                    InventoryCount = 100
                });

                Products.Add(new Entities.Product
                {
                    Name = "Computer 3",
                    Rate = 500,
                    InventoryCount = 100
                });

                await base.SaveChangesAsync(cancellationToken);
            }
        }

        public DbSet<Entities.Product> Products { get; set; }
        public DbSet<Entities.InventoryReservation> InventoryReservations { get; set; }

        public async Task<int> SaveChangesAsync()
        {
            return await base.SaveChangesAsync();
        }
    }
}
