using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Product.Microservice.Data;
using Product.Microservice.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Product.Microservice.Services;
using Product.Microservice.Swagger;

namespace Product.Microservice.Controllers
{
    [Route("api/v1/[controller]")]
    [ApiController]
    public class ProductController : ControllerBase
    {
        private readonly IApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IInventoryService _inventoryService;

        public ProductController(IApplicationDbContext context, IConfiguration configuration, IInventoryService inventoryService)
        {
            _context = context;
            _configuration = configuration;
            _inventoryService = inventoryService;
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(ProductWriteRequest request)
        {
            var product = new Entities.Product { Name = request.Name, Rate = request.Rate, InventoryCount = request.InventoryCount };
            _context.Products.Add(product);
            await _context.SaveChangesAsync();
            var response = new ProductResponse(product.Id, product.Name, product.Rate, product.InventoryCount);
            return CreatedAtAction(nameof(GetById), new { id = product.Id }, response);
        }

        [HttpGet]
        public async Task<IActionResult> GetAll(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] int? minRate = null,
            [FromQuery] int? maxRate = null,
            [FromQuery] string name = null,
            [FromQuery] string sortBy = "id",
            [FromQuery] bool descending = false)
        {
            if (page < 1 || pageSize < 1 || pageSize > 100 || page > (long)int.MaxValue / pageSize + 1 || minRate < 0 || maxRate < 0 || (minRate.HasValue && maxRate.HasValue && minRate > maxRate))
                return BadRequest("Invalid pagination or price range.");

            var query = _context.Products.AsNoTracking();
            if (minRate.HasValue) query = query.Where(product => product.Rate >= minRate.Value);
            if (maxRate.HasValue) query = query.Where(product => product.Rate <= maxRate.Value);
            if (!string.IsNullOrWhiteSpace(name)) query = query.Where(product => product.Name.StartsWith(name));
            var orderedQuery = sortBy?.ToLowerInvariant() switch
            {
                "id" => descending ? query.OrderByDescending(product => product.Id) : query.OrderBy(product => product.Id),
                "name" => descending ? query.OrderByDescending(product => product.Name) : query.OrderBy(product => product.Name),
                "rate" => descending ? query.OrderByDescending(product => product.Rate) : query.OrderBy(product => product.Rate),
                _ => null
            };
            if (orderedQuery is null) return BadRequest("Invalid sortBy value.");
            var totalCount = await query.CountAsync();
            var items = await orderedQuery.Skip((page - 1) * pageSize).Take(pageSize)
                .Select(product => new ProductResponse(product.Id, product.Name, product.Rate, product.InventoryCount)).ToListAsync();
            return Ok(new { items, totalCount, page, pageSize });
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var product = await _context.Products.AsNoTracking().Where(a => a.Id == id)
                .Select(item => new ProductResponse(item.Id, item.Name, item.Rate, item.InventoryCount)).FirstOrDefaultAsync();
            if (product == null) return NotFound();
            return Ok(product);
        }

        [HttpPost("{id}/reserve")]
        [AllowAnonymous]
        [ServiceKeyRequired]
        public async Task<IActionResult> ReserveInventory(int id, InventoryReservationRequest request, CancellationToken cancellationToken)
        {
            if (!HasValidServiceKey()) return Unauthorized();
            return MapInventoryResult(await _inventoryService.ReserveAsync(id, request.Quantity, request.ReservationId, cancellationToken));
        }

        [HttpPost("{id}/release")]
        [AllowAnonymous]
        [ServiceKeyRequired]
        public async Task<IActionResult> ReleaseInventory(int id, InventoryReservationRequest request, CancellationToken cancellationToken)
        {
            if (!HasValidServiceKey()) return Unauthorized();
            return MapInventoryResult(await _inventoryService.ReleaseAsync(id, request.Quantity, request.ReservationId, cancellationToken));
        }

        private IActionResult MapInventoryResult(InventoryOperationResult result) => result switch
        {
            InventoryOperationResult.Success => NoContent(),
            InventoryOperationResult.ProductNotFound => NotFound(),
            InventoryOperationResult.InsufficientInventory => Conflict(new { message = "Insufficient inventory." }),
            InventoryOperationResult.InventoryLimitExceeded => Conflict(new { message = "Inventory quantity would exceed the supported limit." }),
            InventoryOperationResult.InvalidQuantity => BadRequest(new { message = "Quantity must be between 1 and 10000." }),
            InventoryOperationResult.ReservationConflict => Conflict(new { message = "Reservation ID was already used with different data or released." }),
            _ => Problem(statusCode: StatusCodes.Status500InternalServerError)
        };

        private bool HasValidServiceKey()
        {
            var expected = _configuration["ServiceAuth:Key"];
            var supplied = Request.Headers["X-Service-Key"].ToString();
            return !string.IsNullOrEmpty(expected) &&
                CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied));
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var product = await _context.Products.Where(a => a.Id == id).FirstOrDefaultAsync();
            if (product == null) return NotFound();
            _context.Products.Remove(product);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Update(int id, ProductWriteRequest productData)
        {
            var product = await _context.Products.FirstOrDefaultAsync(a => a.Id == id);
            if (product == null) return NotFound();
            product.Name = productData.Name;
            product.Rate = productData.Rate;
            product.InventoryCount = productData.InventoryCount;
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
