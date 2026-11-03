using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Customer.Microservice.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Customer.Microservice.Contracts;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace Customer.Microservice.Controllers;

[Route("api/v1/[controller]")]
[ApiController]
public class CustomerController : ControllerBase
{
    private readonly IApplicationDbContext _context;

    public CustomerController(IApplicationDbContext context)
    {
        _context = context;
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create(CustomerWriteRequest request)
    {
        var customer = new Entities.Customer { Name = request.Name, Contact = request.Contact, City = request.City, Email = request.Email };
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = customer.Id }, ToResponse(customer));
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string sortBy = "id",
        [FromQuery] bool descending = false)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100 || page > (long)int.MaxValue / pageSize + 1) return BadRequest("Invalid pagination parameters.");
        var query = _context.Customers.AsNoTracking();
        var orderedQuery = sortBy?.ToLowerInvariant() switch
        {
            "id" => descending ? query.OrderByDescending(customer => customer.Id) : query.OrderBy(customer => customer.Id),
            "name" => descending ? query.OrderByDescending(customer => customer.Name) : query.OrderBy(customer => customer.Name),
            "city" => descending ? query.OrderByDescending(customer => customer.City) : query.OrderBy(customer => customer.City),
            _ => null
        };
        if (orderedQuery is null) return BadRequest("Invalid sortBy value.");
        var totalCount = await query.CountAsync();
        var items = await orderedQuery.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(customer => new CustomerResponse(customer.Id, customer.Name, customer.Contact, customer.City, customer.Email)).ToListAsync();
        return Ok(new { items, totalCount, page, pageSize });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var customer = await _context.Customers.AsNoTracking().Where(a => a.Id == id).FirstOrDefaultAsync();
        if (customer == null) return NotFound();
        return Ok(ToResponse(customer));
    }

    [HttpGet("Nickname/{name}")]
    public async Task<IActionResult> GetByName(string name)
    {
        var customer = await _context.Customers.AsNoTracking().Where(a => a.Name == name).FirstOrDefaultAsync();
        if (customer == null) return NotFound();
        return Ok(ToResponse(customer));
    }

    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> Delete(int id)
    {
        if (!OwnsCustomer(id)) return Forbid();
        var customer = await _context.Customers.Where(a => a.Id == id).FirstOrDefaultAsync();
        if (customer == null) return NotFound();
        _context.Customers.Remove(customer);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("{id}")]
    [Authorize]
    public async Task<IActionResult> Update(int id, CustomerWriteRequest customerData)
    {
        if (!OwnsCustomer(id)) return Forbid();
        var customer = await _context.Customers.FirstOrDefaultAsync(a => a.Id == id);

        if (customer == null) return NotFound();
        customer.City = customerData.City;
        customer.Name = customerData.Name;
        customer.Contact = customerData.Contact;
        customer.Email = customerData.Email;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private static CustomerResponse ToResponse(Entities.Customer customer) =>
        new(customer.Id, customer.Name, customer.Contact, customer.City, customer.Email);

    private bool OwnsCustomer(long customerId) =>
        long.TryParse(User.FindFirstValue("customer_id"), out var authenticatedCustomerId) && authenticatedCustomerId == customerId;
}
