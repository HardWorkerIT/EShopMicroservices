using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Product.Microservice.Data;

namespace Product.Microservice.Services;

public enum InventoryOperationResult
{
    Success,
    ProductNotFound,
    InsufficientInventory,
    InventoryLimitExceeded,
    InvalidQuantity,
    ReservationConflict
}

public interface IInventoryService
{
    Task<InventoryOperationResult> ReserveAsync(int productId, int quantity, System.Guid reservationId, CancellationToken cancellationToken);
    Task<InventoryOperationResult> ReleaseAsync(int productId, int quantity, System.Guid reservationId, CancellationToken cancellationToken);
}

public sealed class InventoryService : IInventoryService
{
    private readonly ApplicationDbContext _context;

    public InventoryService(ApplicationDbContext context) => _context = context;

    public async Task<InventoryOperationResult> ReserveAsync(int productId, int quantity, System.Guid reservationId, CancellationToken cancellationToken)
    {
        if (quantity is < 1 or > 10000 || reservationId == System.Guid.Empty) return InventoryOperationResult.InvalidQuantity;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var existing = await _context.InventoryReservations.AsNoTracking().SingleOrDefaultAsync(item => item.Id == reservationId, cancellationToken);
        if (existing is not null)
        {
            return existing.ProductId == productId && existing.Quantity == quantity && !existing.IsReleased
                ? InventoryOperationResult.Success
                : InventoryOperationResult.ReservationConflict;
        }

        var updated = await _context.Products
            .Where(product => product.Id == productId && product.InventoryCount >= quantity)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                product => product.InventoryCount,
                product => product.InventoryCount - quantity), cancellationToken);

        if (updated == 0)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            var racedReservation = await _context.InventoryReservations.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == reservationId, cancellationToken);
            if (racedReservation is not null)
            {
                return racedReservation.ProductId == productId &&
                    racedReservation.Quantity == quantity &&
                    !racedReservation.IsReleased
                    ? InventoryOperationResult.Success
                    : InventoryOperationResult.ReservationConflict;
            }

            return await _context.Products.AnyAsync(product => product.Id == productId, cancellationToken)
                ? InventoryOperationResult.InsufficientInventory
                : InventoryOperationResult.ProductNotFound;
        }

        var reservation = new Entities.InventoryReservation
        {
            Id = reservationId,
            ProductId = productId,
            Quantity = quantity
        };
        _context.InventoryReservations.Add(reservation);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return InventoryOperationResult.Success;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.Entry(reservation).State = EntityState.Detached;
            var persisted = await _context.InventoryReservations.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == reservationId, cancellationToken);
            if (persisted is not null && persisted.ProductId == productId && persisted.Quantity == quantity && !persisted.IsReleased)
                return InventoryOperationResult.Success;
            throw;
        }
    }

    public async Task<InventoryOperationResult> ReleaseAsync(int productId, int quantity, System.Guid reservationId, CancellationToken cancellationToken)
    {
        if (quantity is < 1 or > 10000 || reservationId == System.Guid.Empty) return InventoryOperationResult.InvalidQuantity;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var reservation = await _context.InventoryReservations.AsNoTracking().SingleOrDefaultAsync(item => item.Id == reservationId, cancellationToken);
        if (reservation is not null)
        {
            if (reservation.ProductId != productId || reservation.Quantity != quantity) return InventoryOperationResult.ReservationConflict;
            if (reservation.IsReleased) return InventoryOperationResult.Success;
        }

        if (reservation is null)
        {
            var tombstone = new Entities.InventoryReservation
            {
                Id = reservationId,
                ProductId = productId,
                Quantity = quantity,
                IsReleased = true
            };
            _context.InventoryReservations.Add(tombstone);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _context.Entry(tombstone).State = EntityState.Detached;
                var persisted = await _context.InventoryReservations.AsNoTracking()
                    .SingleOrDefaultAsync(item => item.Id == reservationId, cancellationToken);
                if (persisted is not null && persisted.ProductId == productId && persisted.Quantity == quantity && persisted.IsReleased)
                    return InventoryOperationResult.Success;
                throw;
            }
        }
        else
        {
            var updated = await _context.Products
                .Where(product => product.Id == productId && product.InventoryCount <= int.MaxValue - quantity)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    product => product.InventoryCount,
                    product => product.InventoryCount + quantity), cancellationToken);
            if (updated == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return await _context.Products.AnyAsync(product => product.Id == productId, cancellationToken)
                    ? InventoryOperationResult.InventoryLimitExceeded
                    : InventoryOperationResult.ProductNotFound;
            }

            var claimed = await _context.InventoryReservations
                .Where(item => item.Id == reservationId && !item.IsReleased)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsReleased, true), cancellationToken);
            if (claimed == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return InventoryOperationResult.Success;
            }
        }

        if (reservation is null) await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return InventoryOperationResult.Success;
    }
}
