using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Order.Microservice.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Order.Microservice.Entities;
using Order.Microservice.Contracts;
using Order.Microservice.Clients;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace Customer.Microservice.Controllers;

[Route("api/v1/[controller]")]
[ApiController]
[Authorize]
public class OrderController : ControllerBase
{
    private readonly Order.Microservice.Data.ApplicationDbContext _context;
    private readonly IProductInventoryClient _inventoryClient;

    public OrderController(Order.Microservice.Data.ApplicationDbContext context, IProductInventoryClient inventoryClient)
    {
        _context = context;
        _inventoryClient = inventoryClient;
    }

    [HttpPost]
    public async Task<IActionResult> Create(OrderWriteRequest request, CancellationToken cancellationToken)
    {
        if (!long.TryParse(User.FindFirstValue("customer_id"), out var authenticatedCustomerId) || authenticatedCustomerId != request.CustomerId)
            return Forbid();
        var reservationId = Guid.NewGuid();
        var reservationAttempt = new Order.Microservice.Entities.InventoryReservationAttempt
        {
            Id = reservationId,
            ProductId = request.ProductId,
            CustomerId = request.CustomerId,
            Quantity = request.Quantity
        };
        _context.InventoryReservationAttempts.Add(reservationAttempt);
        await _context.SaveChangesAsync(cancellationToken);

        HttpStatusCode reservationStatus;
        try
        {
            reservationStatus = await _inventoryClient.ReserveAsync(request.ProductId, request.Quantity, reservationId, cancellationToken);
        }
        catch (HttpRequestException)
        {
            await TryCompensateAsync(reservationAttempt);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Product service is unavailable.");
        }

        if ((int)reservationStatus < 200 || (int)reservationStatus >= 300)
        {
            await TryCompensateAsync(reservationAttempt);
            if (reservationStatus == HttpStatusCode.NotFound) return NotFound(new { message = "Product not found." });
            if (reservationStatus == HttpStatusCode.Conflict) return Conflict(new { message = "Insufficient inventory." });
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Product service rejected the reservation.");
        }

        var order = new Order.Microservice.Entities.Order { ProductId = request.ProductId, CustomerId = request.CustomerId, Quantity = request.Quantity, InventoryReservationId = reservationId, Date = request.Date, OrderStatus = request.OrderStatus };
        var eventId = Guid.NewGuid();
        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                _context.Orders.Add(order);
                await _context.SaveChangesAsync(cancellationToken);
                _context.OutboxMessages.Add(new Order.Microservice.Entities.OutboxMessage
                {
                    Id = eventId,
                    Type = "orders.created.v1",
                    Payload = JsonSerializer.Serialize(new Order.Microservice.Events.OrderCreatedEvent(eventId, order.Id, order.ProductId, order.CustomerId, order.Quantity, order.Date))
                });
                reservationAttempt.Status = Order.Microservice.Entities.InventoryReservationAttemptStatus.Committed;
                reservationAttempt.UpdatedUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        catch
        {
            _context.ChangeTracker.Clear();
            await TryCompensateAsync(reservationAttempt);
            throw;
        }
        return CreatedAtAction(nameof(GetById), new { id = order.Id }, ToResponse(order));
    }

    private async Task TryCompensateAsync(Order.Microservice.Entities.InventoryReservationAttempt attempt)
    {
        try
        {
            await _inventoryClient.ReleaseAsync(attempt.ProductId, attempt.Quantity, attempt.Id, CancellationToken.None);
            var persistedAttempt = await _context.InventoryReservationAttempts
                .SingleOrDefaultAsync(item => item.Id == attempt.Id, CancellationToken.None);
            if (persistedAttempt is null) return;
            persistedAttempt.Status = Order.Microservice.Entities.InventoryReservationAttemptStatus.Compensated;
            persistedAttempt.UpdatedUtc = DateTime.UtcNow;
            persistedAttempt.LastError = null;
            await _context.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            var persistedAttempt = await _context.InventoryReservationAttempts
                .SingleOrDefaultAsync(item => item.Id == attempt.Id, CancellationToken.None);
            if (persistedAttempt is null) return;
            persistedAttempt.LastError = exception.Message;
            persistedAttempt.UpdatedUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(CancellationToken.None);
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string sortBy = "id",
        [FromQuery] bool descending = false)
    {
        if (!long.TryParse(User.FindFirstValue("customer_id"), out var customerId)) return Forbid();
        if (page < 1 || pageSize < 1 || pageSize > 100 || page > (long)int.MaxValue / pageSize + 1) return BadRequest("Invalid pagination parameters.");
        var query = _context.Orders.AsNoTracking().Where(order => order.CustomerId == customerId);
        var orderedQuery = ApplySorting(query, sortBy, descending);
        if (orderedQuery is null) return BadRequest("Invalid sortBy value.");
        var totalCount = await query.CountAsync();
        var items = await orderedQuery.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(order => new OrderResponse(order.Id, order.ProductId, order.CustomerId, order.Quantity, order.Date, order.OrderStatus)).ToListAsync();
        return Ok(new { items, totalCount, page, pageSize });
    }

    [HttpGet("Products/{productId}")]
    public async Task<IActionResult> GetAllByProductId(
        long productId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string sortBy = "id",
        [FromQuery] bool descending = false)
    {
        if (!long.TryParse(User.FindFirstValue("customer_id"), out var customerId)) return Forbid();
        if (page < 1 || pageSize < 1 || pageSize > 100 || page > (long)int.MaxValue / pageSize + 1) return BadRequest("Invalid pagination parameters.");
        var query = _context.Orders.AsNoTracking().Where(x => x.ProductId == productId && x.CustomerId == customerId);
        var orderedQuery = ApplySorting(query, sortBy, descending);
        if (orderedQuery is null) return BadRequest("Invalid sortBy value.");
        var totalCount = await query.CountAsync();
        var items = await orderedQuery.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(order => new OrderResponse(order.Id, order.ProductId, order.CustomerId, order.Quantity, order.Date, order.OrderStatus)).ToListAsync();
        return Ok(new { items, totalCount, page, pageSize });
    }

    [HttpGet("Customers/{customerId}")]
    public async Task<IActionResult> GetAllByCustomerId(
        long customerId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string sortBy = "id",
        [FromQuery] bool descending = false)
    {
        if (!long.TryParse(User.FindFirstValue("customer_id"), out var authenticatedCustomerId) || authenticatedCustomerId != customerId) return Forbid();
        if (page < 1 || pageSize < 1 || pageSize > 100 || page > (long)int.MaxValue / pageSize + 1) return BadRequest("Invalid pagination parameters.");
        var query = _context.Orders.AsNoTracking().Where(x => x.CustomerId == customerId);
        var orderedQuery = ApplySorting(query, sortBy, descending);
        if (orderedQuery is null) return BadRequest("Invalid sortBy value.");
        var totalCount = await query.CountAsync();
        var items = await orderedQuery.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(order => new OrderResponse(order.Id, order.ProductId, order.CustomerId, order.Quantity, order.Date, order.OrderStatus)).ToListAsync();
        return Ok(new { items, totalCount, page, pageSize });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        if (!long.TryParse(User.FindFirstValue("customer_id"), out var customerId)) return Forbid();
        var order = await _context.Orders.AsNoTracking().Where(a => a.Id == id && a.CustomerId == customerId).FirstOrDefaultAsync();
        if (order == null) return NotFound();
        return Ok(ToResponse(order));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!long.TryParse(User.FindFirstValue("customer_id"), out var customerId)) return Forbid();
        var order = await _context.Orders.Where(a => a.Id == id && a.CustomerId == customerId).FirstOrDefaultAsync();
        if (order == null) return NotFound();
        var releasing = order.OrderStatus != (int)Order.Microservice.Enums.OrderStatus.Cancelled;
        if (releasing)
        {
            try { await _inventoryClient.ReleaseAsync(order.ProductId, order.Quantity, order.InventoryReservationId, HttpContext.RequestAborted); }
            catch (HttpRequestException) { return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Product service is unavailable."); }
        }
        _context.Orders.Remove(order);
        try { await _context.SaveChangesAsync(); }
        catch
        {
            if (releasing)
            {
                var replacementReservationId = Guid.NewGuid();
                try
                {
                    await _inventoryClient.ReserveAsync(order.ProductId, order.Quantity, replacementReservationId, CancellationToken.None);
                    order.InventoryReservationId = replacementReservationId;
                    await _context.SaveChangesAsync(CancellationToken.None);
                }
                catch (HttpRequestException) { }
            }
            throw;
        }
        return NoContent();
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateOrderStatusRequest orderData)
    {
        if (!long.TryParse(User.FindFirstValue("customer_id"), out var customerId)) return Forbid();
        var order = await _context.Orders.FirstOrDefaultAsync(a => a.Id == id && a.CustomerId == customerId);

        if (order == null) return NotFound();
        var releasing = order.OrderStatus != (int)Order.Microservice.Enums.OrderStatus.Cancelled &&
            orderData.OrderStatus == (int)Order.Microservice.Enums.OrderStatus.Cancelled;
        if (releasing)
        {
            try { await _inventoryClient.ReleaseAsync(order.ProductId, order.Quantity, order.InventoryReservationId, HttpContext.RequestAborted); }
            catch (HttpRequestException) { return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Product service is unavailable."); }
        }
        order.OrderStatus = orderData.OrderStatus;
        try { await _context.SaveChangesAsync(); }
        catch
        {
            if (releasing)
            {
                var replacementReservationId = Guid.NewGuid();
                try
                {
                    await _inventoryClient.ReserveAsync(order.ProductId, order.Quantity, replacementReservationId, CancellationToken.None);
                    order.InventoryReservationId = replacementReservationId;
                    await _context.SaveChangesAsync(CancellationToken.None);
                }
                catch (HttpRequestException) { }
            }
            throw;
        }
        return NoContent();
    }

    private static OrderResponse ToResponse(Order.Microservice.Entities.Order order) =>
        new(order.Id, order.ProductId, order.CustomerId, order.Quantity, order.Date, order.OrderStatus);

    private static IOrderedQueryable<Order.Microservice.Entities.Order> ApplySorting(
        IQueryable<Order.Microservice.Entities.Order> query,
        string sortBy,
        bool descending) => (sortBy?.ToLowerInvariant(), descending) switch
        {
            ("id", false) => query.OrderBy(order => order.Id),
            ("id", true) => query.OrderByDescending(order => order.Id),
            ("date", false) => query.OrderBy(order => order.Date),
            ("date", true) => query.OrderByDescending(order => order.Date),
            ("status", false) => query.OrderBy(order => order.OrderStatus),
            ("status", true) => query.OrderByDescending(order => order.OrderStatus),
            _ => null
        };
}
