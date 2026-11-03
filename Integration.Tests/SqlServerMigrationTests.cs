using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CustomerContext = Customer.Microservice.Data.ApplicationDbContext;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrderContext = Order.Microservice.Data.ApplicationDbContext;
using ProductContext = Product.Microservice.Data.ApplicationDbContext;
using Xunit;

public sealed class SqlServerMigrationTests
{
    [SqlServerFact]
    [Trait("Category", "SqlServer")]
    public async Task All_service_migrations_apply_to_clean_sql_server_databases()
    {
        var configuredConnectionString = Environment.GetEnvironmentVariable("ESHOP_SQLSERVER_ADMIN_CONNECTION");
        var adminConnection = new SqlConnectionStringBuilder(configuredConnectionString)
        {
            InitialCatalog = "master"
        };
        var databaseNames = new[]
        {
            $"EShopCustomer_{Guid.NewGuid():N}",
            $"EShopOrder_{Guid.NewGuid():N}",
            $"EShopProduct_{Guid.NewGuid():N}"
        };

        try
        {
            foreach (var databaseName in databaseNames)
            {
                await using var connection = new SqlConnection(adminConnection.ConnectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"CREATE DATABASE [{databaseName}]";
                await command.ExecuteNonQueryAsync();
            }

            await using (var customer = new CustomerContext(CreateOptions<CustomerContext>(adminConnection, databaseNames[0])))
                await MigrateAndAssertAppliedAsync(customer);

            await using (var order = new OrderContext(CreateOptions<OrderContext>(adminConnection, databaseNames[1])))
                await MigrateAndAssertAppliedAsync(order);

            await using (var product = new ProductContext(CreateOptions<ProductContext>(adminConnection, databaseNames[2])))
                await MigrateAndAssertAppliedAsync(product);
        }
        finally
        {
            foreach (var databaseName in databaseNames)
            {
                await using var connection = new SqlConnection(adminConnection.ConnectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"IF DB_ID(N'{databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]; END";
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    private static DbContextOptions<TContext> CreateOptions<TContext>(SqlConnectionStringBuilder adminConnection, string databaseName)
        where TContext : DbContext
    {
        var databaseConnection = new SqlConnectionStringBuilder(adminConnection.ConnectionString)
        {
            InitialCatalog = databaseName
        };

        return new DbContextOptionsBuilder<TContext>()
            .UseSqlServer(databaseConnection.ConnectionString)
            .Options;
    }

    private static async Task MigrateAndAssertAppliedAsync(DbContext context)
    {
        await context.Database.MigrateAsync();

        var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
        var pendingMigrations = await context.Database.GetPendingMigrationsAsync();

        Assert.NotEmpty(appliedMigrations);
        Assert.Empty(pendingMigrations);
    }
}

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ESHOP_SQLSERVER_ADMIN_CONNECTION")))
            Skip = "Set ESHOP_SQLSERVER_ADMIN_CONNECTION to run SQL Server integration tests.";
    }
}
