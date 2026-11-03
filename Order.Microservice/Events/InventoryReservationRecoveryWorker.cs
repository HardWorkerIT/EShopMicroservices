using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Order.Microservice.Events;

public sealed class InventoryReservationRecoveryWorker : BackgroundService
{
    private static readonly TimeSpan RecoveryAge = TimeSpan.FromMinutes(5);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InventoryReservationRecoveryWorker> _logger;

    public InventoryReservationRecoveryWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<InventoryReservationRecoveryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var recovery = scope.ServiceProvider.GetRequiredService<InventoryReservationRecoveryService>();
                var count = await recovery.RecoverStaleReservationsAsync(DateTime.UtcNow - RecoveryAge, stoppingToken);
                if (count > 0) _logger.LogInformation("Checked {ReservationCount} pending inventory reservations.", count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Inventory reservation recovery failed; pending reservations will be retried.");
            }
        }
    }
}
