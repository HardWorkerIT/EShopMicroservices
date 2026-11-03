using Aggregator.WebApi.Customer;
using Aggregator.WebApi.Order;
using Aggregator.WebApi.Product;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Threading;
using Polly.Timeout;

namespace Aggregator.WebApi.Controllers;

[Route("api/v1/[controller]")]
[ApiController]
public class ShopController : ControllerBase
{
    private readonly ICustomerService _customerService;
    private readonly IOrderService _orderService;
    private readonly IProductService _productService;

    public ShopController(
        ICustomerService customerService,
        IOrderService orderService,
        IProductService productService)
    {
        _customerService = customerService;
        _orderService = orderService;
        _productService = productService;
    }

    [HttpGet("Customers/{customerName}")]
    public async Task<IActionResult> GetAllByCustomerId(string customerName, CancellationToken cancellationToken)
    {
        CustomerApi customerApi;
        try
        {
            customerApi = await _customerService.GetCustomerByNameAsync(customerName, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Customer service is unavailable.");
        }
        catch (TimeoutRejectedException)
        {
            return Problem(statusCode: StatusCodes.Status504GatewayTimeout, title: "Customer service timed out.");
        }

        if (customerApi is null)
        {
            return NotFound();
        }

        List<OrderApi> orderApis;
        try
        {
            orderApis = await _orderService.GetOrdersByCustomerIdAsync(customerApi.Id, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Order service is unavailable.");
        }
        catch (TimeoutRejectedException)
        {
            return Problem(statusCode: StatusCodes.Status504GatewayTimeout, title: "Order service timed out.");
        }

        // Orders are independent reads. Fetch products concurrently instead of
        // adding one full network round-trip per order to the response latency.
        ProductApi[] productApis;
        try
        {
            productApis = await Task.WhenAll(
                orderApis.Select(order => _productService.GetProductByIdAsync(order.ProductId, cancellationToken)));
        }
        catch (HttpRequestException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Product service is unavailable.");
        }
        catch (TimeoutRejectedException)
        {
            return Problem(statusCode: StatusCodes.Status504GatewayTimeout, title: "Product service timed out.");
        }

        return Ok(productApis.Where(product => product is not null));
    }
}
