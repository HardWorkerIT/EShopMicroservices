using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Order.Microservice.Clients;
using Order.Microservice.Data;

namespace Order.Microservice.Events;

public sealed class InventoryReservationRecoveryService
{
    private readonly ApplicationDbContext _context;
    private readonly IProductInventoryClient _inventoryClient;

    public InventoryReservationRecoveryService(ApplicationDbContext context, IProductInventoryClient inventoryClient)
    {
        _context = context;
        _inventoryClient = inventoryClient;
    }

    public async Task<int> RecoverStaleReservationsAsync(DateTime olderThanUtc, CancellationToken cancellationToken)
    {
        var pending = await _context.InventoryReservationAttempts
            .Where(item => item.Status == Entities.InventoryReservationAttemptStatus.Pending && item.UpdatedUtc < olderThanUtc)
            .OrderBy(item => item.UpdatedUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (var attempt in pending)
        {
            attempt.Attempts++;
            try
            {
                await _inventoryClient.ReleaseAsync(attempt.ProductId, attempt.Quantity, attempt.Id, cancellationToken);
                attempt.Status = Entities.InventoryReservationAttemptStatus.Compensated;
                attempt.LastError = null;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                attempt.LastError = exception.Message;
            }
            attempt.UpdatedUtc = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return pending.Count;
    }
}
