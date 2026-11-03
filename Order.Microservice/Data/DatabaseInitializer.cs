using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Order.Microservice.Data;

public sealed class DatabaseInitializer : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    public DatabaseInitializer(IServiceScopeFactory scopeFactory, IConfiguration configuration, IHostEnvironment environment)
    { _scopeFactory = scopeFactory; _configuration = configuration; _environment = environment; }
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var seedDemoData = bool.TryParse(_configuration["SeedData:Enabled"], out var configured)
            ? configured
            : _environment.IsDevelopment();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .InitializeDatabaseAsync(seedDemoData, cancellationToken, _configuration.GetConnectionString("MigrationConnection"));
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
