using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace Customer.Microservice.Data
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

            if (await Customers.AnyAsync(cancellationToken) is false)
            {
                Customers.Add(new Entities.Customer
                {
                    City = "Moscow",
                    Name = "Guest"
                });

                await base.SaveChangesAsync(cancellationToken);
            }
        }

        public DbSet<Entities.Customer> Customers{ get; set; }
        public DbSet<Entities.OrderProjection> OrderProjections { get; set; }

        public Task<int> SaveChangesAsync()
        {
            return base.SaveChangesAsync();
        }
    }
}
